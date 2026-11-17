using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public sealed class SmallProcessorMaskService
{
    private const string KernelKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string ValueName = "SmallProcessorMask";

    private const int RelationProcessorCore = 0;

    public ulong? Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KernelKeyPath, false);
            return key?.GetValue(ValueName) switch
            {
                int v => unchecked((ulong)(uint)v),
                long v => unchecked((ulong)v),
                byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    public string? Write(ulong mask)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(KernelKeyPath, true);
            if (key is null)
                return "The kernel Session Manager key could not be opened. Run the app as administrator.";

            if (mask == 0)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else if (mask <= uint.MaxValue)
            {
                key.SetValue(ValueName, unchecked((int)(uint)mask), RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(ValueName, unchecked((long)mask), RegistryValueKind.QWord);
            }

            ulong? after = Read();
            ulong? expected = mask == 0 ? null : mask;

            return after == expected ? null : "The value was written but reading it back did not match.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Administrator rights are required to change SmallProcessorMask. Restart kaliteConfig as administrator and try again.";
        }
        catch (Exception ex)
        {
            return "Could not write the value: " + ex.Message;
        }
    }

    /// <summary>
    /// One mask per physical core, each bit representing a logical processor of that core,
    /// ordered by the lowest logical processor index. The first thread of each core is its
    /// primary thread; the remaining bits are its SMT/HT siblings. Empty when the topology
    /// cannot be read.
    /// </summary>
    public static List<ulong> GetCoreMasks()
    {
        var masks = new List<ulong>();
        try
        {
            uint length = 0;
            Native.Kernel32.GetLogicalProcessorInformation(IntPtr.Zero, ref length);
            if (length == 0) return masks;

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!Native.Kernel32.GetLogicalProcessorInformation(buffer, ref length)) return masks;

                int entrySize = Marshal.SizeOf<SystemLogicalProcessorInformation>();
                int count = (int)(length / entrySize);
                for (int i = 0; i < count; i++)
                {
                    IntPtr entryPtr = (IntPtr)((long)buffer + (long)i * entrySize);
                    var entry = Marshal.PtrToStructure<SystemLogicalProcessorInformation>(entryPtr);
                    if (entry.Relationship != RelationProcessorCore) continue;
                    masks.Add((ulong)entry.ProcessorMask);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }

            masks.Sort((a, b) => LowestBit(a).CompareTo(LowestBit(b)));
        }
        catch
        {
            masks.Clear();
        }
        return masks;
    }

    private static int LowestBit(ulong value)
    {
        for (int bit = 0; bit < 64; bit++)
            if (((value >> bit) & 1UL) != 0) return bit;
        return 64;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemLogicalProcessorInformation
    {
        public UIntPtr ProcessorMask; // ULONG_PTR, 8 bytes on x64
        public uint Relationship;     // offset 8
        private uint _padding;        // aligns the union to offset 16
        public ulong Union0;
        public ulong Union1;
    }
}
