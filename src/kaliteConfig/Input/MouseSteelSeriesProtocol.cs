using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using kaliteConfig.Input.Interop;

namespace kaliteConfig.Input;

public sealed class MouseSteelSeriesProtocol : IMouseProtocol
{
    public const ushort SteelSeriesVendorId = 0x1038;

    private const byte RateCommand = 0x04;
    private const int VendorPage = 0xFFC0;
    private const int DefaultReportLength = 32;
    private const int OpenDelayMs = 60;

    private static readonly int[] DefaultRates = new int[] { 125, 250, 500, 1000 };

    private readonly object _gate = new();

    public string Id => "steelseries-vendor";
    public bool EnabledByDefault => true;
    public bool CanReadRate => false;
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<int> SupportedRates => DefaultRates;

    public static bool IsSteelSeries(ushort vendorId) => vendorId == SteelSeriesVendorId;

    public bool Matches(MouseDevice device)
    {
        if (device is null || device.IsVirtual || device.VendorId != SteelSeriesVendorId) return false;
        return ControlInterface(device) is not null;
    }

    public MouseDeviceSettings? TryRead(MouseDevice device)
    {
        lock (_gate)
        {
            LastError = string.Empty;
            var face = ControlInterface(device);
            if (face is null)
            {
                LastError = "This SteelSeries mouse exposes no vendor control interface.";
                return null;
            }
            return new MouseDeviceSettings
            {
                DpiX = 0,
                DpiY = 0,
                RateHz = 0,
                LodIndex = 0,
                LodCount = 0,
                SupportsDpi = false,
                SupportsRate = true,
                SupportsLod = false,
                SupportsMulticlick = false,
                SupportsSpdt = false,
                IsV2 = true,
                ModelName = device.Product,
            };
        }
    }

    public IReadOnlyList<MouseSettingSpec> Describe(MouseDevice device, MouseDeviceSettings settings)
    {
        var specs = new List<MouseSettingSpec>();
        var options = new List<string>();
        foreach (int rate in DefaultRates) options.Add(rate + " Hz");
        specs.Add(new MouseSettingSpec("report-rate", "Report rate", MouseSettingGroup.Sensor, MouseSettingKind.Choice)
        {
            Hint = "Written straight to the mouse. SteelSeries does not report the value back, so this app can confirm the write but not read the setting.",
            Options = options,
            Index = -1,
        });
        return specs;
    }

    public bool TryWrite(MouseDevice device, string setting, string value)
    {
        lock (_gate)
        {
            LastError = string.Empty;
            if (string.Equals(setting, "report-rate", StringComparison.OrdinalIgnoreCase))
                return WriteRate(device, value);
            LastError = "This SteelSeries protocol does not expose that setting.";
            return false;
        }
    }

    private bool WriteRate(MouseDevice device, string value)
    {
        if (!int.TryParse(value, out int rate) || rate <= 0)
        {
            LastError = "That report rate is not a number.";
            return false;
        }
        int code = RateCode(rate);
        if (code < 0)
        {
            LastError = rate + " Hz is not a rate this SteelSeries command accepts. Choose 125, 250, 500 or 1000 Hz.";
            return false;
        }
        var face = ControlInterface(device);
        if (face is null)
        {
            LastError = "This SteelSeries mouse exposes no vendor control interface.";
            return false;
        }
        return SendRate(face.DevicePath, code, out string error) || Fail("The SteelSeries report-rate command failed: " + error);
    }

    private bool Fail(string message)
    {
        LastError = message;
        return false;
    }

    private static int RateCode(int rate)
    {
        return rate switch
        {
            125 => 0x04,
            250 => 0x03,
            500 => 0x02,
            1000 => 0x01,
            _ => -1,
        };
    }

    private static bool SendRate(string path, int code, out string error)
    {
        error = string.Empty;
        IntPtr handle = NativeInput.CreateFileW(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            int first = Marshal.GetLastWin32Error();
            handle = NativeInput.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                error = "open refused (code " + first + ")";
                return false;
            }
        }
        try
        {
            int length = ReportLength(handle);
            byte[] report = new byte[length];
            report[0] = 0x00;
            report[1] = RateCommand;
            report[2] = 0x00;
            report[3] = (byte)code;
            uint written = 0;
            if (NativeInput.WriteFile(handle, report, (uint)report.Length, out written, IntPtr.Zero)
                && written == (uint)report.Length)
            {
                Thread.Sleep(OpenDelayMs);
                return true;
            }
            int writeError = Marshal.GetLastWin32Error();
            if (NativeInput.HidD_SetOutputReport(handle, report, (uint)report.Length))
            {
                Thread.Sleep(OpenDelayMs);
                return true;
            }
            error = "output report refused (code " + writeError + ", " + Marshal.GetLastWin32Error() + ")";
            return false;
        }
        finally
        {
            try { NativeInput.CloseHandle(handle); } catch { }
        }
    }

    private static int ReportLength(IntPtr handle)
    {
        try
        {
            if (!NativeInput.HidD_GetPreparsedData(handle, out IntPtr preparsed) || preparsed == IntPtr.Zero)
                return DefaultReportLength;
            try
            {
                IntPtr caps = Marshal.AllocHGlobal(64);
                try
                {
                    for (int i = 0; i < 64; i++) Marshal.WriteByte(caps, i, 0);
                    if (NativeInput.HidP_GetCaps(preparsed, caps) < 0) return DefaultReportLength;
                    int output = (ushort)Marshal.ReadInt16(caps, 6);
                    if (output >= 4 && output <= 512) return output;
                    int feature = (ushort)Marshal.ReadInt16(caps, 8);
                    if (feature >= 4 && feature <= 512) return feature;
                }
                finally
                {
                    Marshal.FreeHGlobal(caps);
                }
            }
            finally
            {
                try { NativeInput.HidD_FreePreparsedData(preparsed); } catch { }
            }
        }
        catch { }
        return DefaultReportLength;
    }

    private static MouseInterfaceInfo? ControlInterface(MouseDevice device)
    {
        MouseInterfaceInfo? best = null;
        int bestScore = int.MinValue;
        foreach (var face in device.Interfaces)
        {
            if (face.DevicePath.Length == 0) continue;
            int score = 0;
            if (face.UsagePage == VendorPage) score += 40;
            else if (face.VendorDefined) score += 20;
            else continue;
            score += Math.Min(face.FeatureLength, 64);
            if (score > bestScore)
            {
                bestScore = score;
                best = face;
            }
        }
        return best;
    }
}
