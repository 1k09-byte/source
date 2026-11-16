using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using kaliteConfig.Input.Interop;

namespace kaliteConfig.Input;

public sealed class MouseRazerProtocol : IMouseProtocol
{
    public const ushort RazerVendorId = 0x1532;

    private const int ReportSize = 90;
    private const byte TransactionId = 0x1F;
    private const byte StatusSuccess = 0x02;
    private const byte StatusBusy = 0x01;
    private const byte DriverMode = 0x03;
    private const byte NormalMode = 0x00;
    private const int OffStatus = 0;
    private const int OffTransaction = 1;
    private const int OffDataSize = 5;
    private const int OffClass = 6;
    private const int OffCommand = 7;
    private const int OffArgs = 8;
    private const int OffCrc = 88;
    private const int OffReserved = 89;

    private static readonly int[] ExtendedRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
    private static readonly int[] LegacyRates = new int[] { 125, 500, 1000 };

    private readonly object _gate = new();
    private string _deviceId = string.Empty;
    private string _path = string.Empty;
    private int _featureLength;
    private bool _useRate2;
    private byte _dpiStorage;
    private int[] _rates = Array.Empty<int>();

    public string Id => "razer";
    public bool EnabledByDefault => true;
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<int> SupportedRates => _rates;

    public static bool IsRazer(ushort vendorId) => vendorId == RazerVendorId;

    public bool Matches(MouseDevice device)
    {
        return device is not null && !device.IsVirtual && device.VendorId == RazerVendorId;
    }

    public MouseDeviceSettings? TryRead(MouseDevice device)
    {
        lock (_gate)
        {
            LastError = string.Empty;
            if (!EnsurePath(device, out string pathError))
            {
                LastError = pathError;
                return null;
            }
            bool changed;
            EnterDriver(_path, _featureLength, out changed);
            try
            {
                var dpi = ReadDpi(_path, _featureLength);
                var rate = ReadRate(_path, _featureLength);
                if (dpi is null && rate is null)
                {
                    LastError = "The Razer control interface answered, but this model did not return DPI or report rate.";
                    return null;
                }
                _rates = rate is null ? Array.Empty<int>() : rate.Rates;
                _useRate2 = rate is not null && rate.Extended;
                if (dpi is not null) _dpiStorage = dpi.Storage;
                var settings = new MouseDeviceSettings
                {
                    DpiX = dpi?.X ?? 0,
                    DpiY = dpi?.Y ?? 0,
                    RateHz = rate?.Hz ?? 0,
                    LodIndex = 0,
                    LodCount = 0,
                    SupportsDpi = dpi is not null,
                    SupportsRate = rate is not null,
                    SupportsLod = false,
                    SupportsMulticlick = false,
                    SupportsSpdt = false,
                    IsV2 = false,
                    ModelName = device.Product,
                };
                LastError = string.Empty;
                return settings;
            }
            finally
            {
                if (changed) SetMode(_path, _featureLength, NormalMode);
            }
        }
    }

    public IReadOnlyList<MouseSettingSpec> Describe(MouseDevice device, MouseDeviceSettings settings)
    {
        return Array.Empty<MouseSettingSpec>();
    }

    public bool TryWrite(MouseDevice device, string setting, string value)
    {
        lock (_gate)
        {
            LastError = string.Empty;
            if (!EnsurePath(device, out string pathError))
            {
                LastError = pathError;
                return false;
            }
            bool changed;
            EnterDriver(_path, _featureLength, out changed);
            try
            {
                if (string.Equals(setting, "report-rate", StringComparison.OrdinalIgnoreCase))
                    return WriteRate(value);
                if (string.Equals(setting, "dpi", StringComparison.OrdinalIgnoreCase))
                    return WriteDpi(value);
                LastError = "This Razer protocol does not expose that setting.";
                return false;
            }
            finally
            {
                if (changed) SetMode(_path, _featureLength, NormalMode);
            }
        }
    }

    private bool WriteRate(string value)
    {
        if (!int.TryParse(value, out int rate) || rate <= 0)
        {
            LastError = "That report rate is not a number.";
            return false;
        }
        if (_useRate2)
        {
            int selector = WriteSelectorExtended(rate);
            if (selector == 0)
            {
                LastError = "This Razer mouse did not report " + rate + " Hz, so it was not written.";
                return false;
            }
            byte[]? reply = Transact(_path, _featureLength, 0x00, 0x40, new byte[] { 0x00, (byte)selector }, out string error);
            if (reply is null)
            {
                LastError = "The Razer polling command failed: " + error;
                return false;
            }
            return true;
        }
        int legacy = WriteSelectorLegacy(rate);
        if (legacy == 0)
        {
            LastError = rate + " Hz is not available through this Razer command. This model supports 125, 500 or 1000 Hz.";
            return false;
        }
        byte[]? oldReply = Transact(_path, _featureLength, 0x00, 0x05, new byte[] { (byte)legacy }, out string oldError);
        if (oldReply is null)
        {
            LastError = "The Razer polling command failed: " + oldError;
            return false;
        }
        return true;
    }

    private bool WriteDpi(string value)
    {
        if (!int.TryParse(value, out int dpi) || dpi < 100 || dpi > 45000)
        {
            LastError = "That DPI value is outside the Razer range of 100 to 45000.";
            return false;
        }
        byte high = (byte)((dpi >> 8) & 0xFF);
        byte low = (byte)(dpi & 0xFF);
        byte[] first = new byte[] { _dpiStorage, high, low, high, low, 0, 0 };
        if (Transact(_path, _featureLength, 0x04, 0x05, first, out string firstError) is not null) return true;
        byte[] second = new byte[] { 0x01, high, low, high, low, 0, 0 };
        if (Transact(_path, _featureLength, 0x04, 0x05, second, out _) is not null)
        {
            _dpiStorage = 0x01;
            return true;
        }
        LastError = "The Razer mouse refused the DPI command: " + firstError;
        return false;
    }

    private bool EnsurePath(MouseDevice device, out string error)
    {
        error = string.Empty;
        if (device is null)
        {
            error = "No mouse selected.";
            return false;
        }
        if (string.Equals(_deviceId, device.StableId, StringComparison.OrdinalIgnoreCase) && _path.Length > 0)
        {
            if (Probe(_path, _featureLength)) return true;
        }
        _path = string.Empty;
        _featureLength = 0;
        _deviceId = string.Empty;
        var failures = new List<string>();
        foreach (var face in Candidates(device))
        {
            int length = FeatureReportLength(face.DevicePath);
            if (length < ReportSize && face.FeatureLength >= ReportSize) length = face.FeatureLength;
            if (length < ReportSize)
            {
                failures.Add(ShortPath(face.DevicePath) + ": no " + ReportSize + "-byte feature report");
                continue;
            }
            if (Probe(face.DevicePath, length))
            {
                _path = face.DevicePath;
                _featureLength = length;
                _deviceId = device.StableId;
                return true;
            }
            failures.Add(ShortPath(face.DevicePath) + ": no answer");
        }
        error = "Razer control interface did not answer. Tried " + failures.Count
            + (failures.Count == 1 ? " interface: " : " interfaces: ") + string.Join("; ", failures)
            + ". Close Razer Synapse if it is running, then rescan.";
        return false;
    }

    private static bool Probe(string path, int length)
    {
        if (Transact(path, length, 0x00, 0x84, new byte[] { 0, 0 }, out _) is not null) return true;
        if (Transact(path, length, 0x00, 0x81, new byte[] { 0, 0 }, out _) is not null) return true;
        return false;
    }

    private void EnterDriver(string path, int length, out bool changed)
    {
        changed = false;
        byte[]? mode = Transact(path, length, 0x00, 0x84, new byte[] { 0, 0 }, out _);
        if (mode is not null && mode.Length > 0 && mode[0] == DriverMode) return;
        byte[]? set = Transact(path, length, 0x00, 0x04, new byte[] { DriverMode, 0 }, out _);
        if (set is not null && (mode is null || mode.Length == 0 || mode[0] != DriverMode)) changed = true;
    }

    private static void SetMode(string path, int length, byte mode)
    {
        Transact(path, length, 0x00, 0x04, new byte[] { mode, 0 }, out _);
    }

    private DpiReading? ReadDpi(string path, int length)
    {
        foreach (byte storage in new byte[] { 0x00, 0x01 })
        {
            byte[]? data = Transact(path, length, 0x04, 0x85, new byte[] { storage, 0, 0, 0, 0, 0, 0 }, out _);
            if (data is null || data.Length < 5) continue;
            int x = (data[1] << 8) | data[2];
            int y = (data[3] << 8) | data[4];
            if (x >= 100 && x <= 45000 && y >= 100 && y <= 45000)
                return new DpiReading(x, y, storage);
        }
        return null;
    }

    private RateReading? ReadRate(string path, int length)
    {
        byte[]? extended = Transact(path, length, 0x00, 0xC0, new byte[] { 0 }, out _);
        if (extended is not null && extended.Length > 0)
        {
            int selector = extended.Length > 1 ? extended[1] : 0;
            if (selector == 0) selector = extended[0];
            int hz = ReadSelectorExtended(selector);
            if (hz > 0) return new RateReading(hz, ExtendedRates, true);
        }
        byte[]? legacy = Transact(path, length, 0x00, 0x85, new byte[] { 0 }, out _);
        if (legacy is not null && legacy.Length > 0)
        {
            int hz = ReadSelectorLegacy(legacy[0]);
            if (hz > 0) return new RateReading(hz, LegacyRates, false);
        }
        return null;
    }

    private static int ReadSelectorExtended(int selector) => selector switch
    {
        0x01 => 8000,
        0x02 => 4000,
        0x04 => 2000,
        0x08 => 1000,
        0x10 => 500,
        0x20 => 250,
        0x40 => 125,
        _ => 0,
    };

    private static int WriteSelectorExtended(int rate) => rate switch
    {
        8000 => 0x01,
        4000 => 0x02,
        2000 => 0x04,
        1000 => 0x08,
        500 => 0x10,
        250 => 0x20,
        125 => 0x40,
        _ => 0,
    };

    private static int ReadSelectorLegacy(int selector) => selector switch
    {
        0x01 => 1000,
        0x02 => 500,
        0x08 => 125,
        _ => 0,
    };

    private static int WriteSelectorLegacy(int rate) => rate switch
    {
        1000 => 0x01,
        500 => 0x02,
        125 => 0x08,
        _ => 0,
    };

    private static List<MouseInterfaceInfo> Candidates(MouseDevice device)
    {
        var list = new List<MouseInterfaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in device.Interfaces)
        {
            if (face.DevicePath.Length == 0 || !seen.Add(face.DevicePath)) continue;
            list.Add(face);
        }
        if (device.InterfacePath.Length > 0 && seen.Add(device.InterfacePath))
            list.Add(new MouseInterfaceInfo { DevicePath = device.InterfacePath, VendorDefined = false });
        list.Sort((left, right) =>
        {
            int vendor = right.VendorDefined.CompareTo(left.VendorDefined);
            if (vendor != 0) return vendor;
            return right.FeatureLength.CompareTo(left.FeatureLength);
        });
        return list;
    }

    private static int FeatureReportLength(string path)
    {
        IntPtr handle = NativeInput.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return 0;
        try
        {
            if (!NativeInput.HidD_GetPreparsedData(handle, out IntPtr preparsed) || preparsed == IntPtr.Zero) return 0;
            try
            {
                IntPtr caps = Marshal.AllocHGlobal(64);
                try
                {
                    for (int i = 0; i < 64; i++) Marshal.WriteByte(caps, i, 0);
                    if (NativeInput.HidP_GetCaps(preparsed, caps) < 0) return 0;
                    return (ushort)Marshal.ReadInt16(caps, 8);
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
        catch
        {
            return 0;
        }
        finally
        {
            try { NativeInput.CloseHandle(handle); } catch { }
        }
    }

    private static byte[]? Transact(string path, int length, byte commandClass, byte commandId, byte[] args, out string error)
    {
        error = string.Empty;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            IntPtr handle = NativeInput.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                error = "open refused (code " + Marshal.GetLastWin32Error() + ")";
                return null;
            }
            try
            {
                int offset = length == ReportSize ? 0 : 1;
                byte[] frame = new byte[length];
                frame[offset + OffStatus] = 0x00;
                frame[offset + OffTransaction] = TransactionId;
                frame[offset + OffDataSize] = (byte)Math.Min(args.Length, 80);
                frame[offset + OffClass] = commandClass;
                frame[offset + OffCommand] = commandId;
                Array.Copy(args, 0, frame, offset + OffArgs, Math.Min(args.Length, 80));
                byte crc = 0;
                for (int i = 2; i < 88; i++) crc ^= frame[offset + i];
                frame[offset + OffCrc] = crc;
                frame[offset + OffReserved] = 0x00;
                if (!SetFeature(handle, frame))
                {
                    error = "feature write refused (code " + Marshal.GetLastWin32Error() + ")";
                }
                else
                {
                    Thread.Sleep(20);
                    byte[] reply = new byte[length];
                    reply[0] = 0x00;
                    if (!GetFeature(handle, reply))
                    {
                        error = "feature read refused (code " + Marshal.GetLastWin32Error() + ")";
                    }
                    else
                    {
                        byte status = reply[offset + OffStatus];
                        if (status == StatusSuccess)
                        {
                            int size = Math.Min((int)reply[offset + OffDataSize], 80);
                            var data = new byte[size];
                            if (size > 0) Array.Copy(reply, offset + OffArgs, data, 0, size);
                            return data;
                        }
                        error = "status 0x" + status.ToString("X2");
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + " " + ex.Message;
            }
            finally
            {
                try { NativeInput.CloseHandle(handle); } catch { }
            }
            Thread.Sleep(20);
        }
        return null;
    }

    private static bool SetFeature(IntPtr handle, byte[] data)
    {
        IntPtr native = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, native, data.Length);
            return NativeInput.HidD_SetFeature(handle, native, (uint)data.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    private static bool GetFeature(IntPtr handle, byte[] data)
    {
        IntPtr native = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, native, data.Length);
            bool ok = NativeInput.HidD_GetFeature(handle, native, (uint)data.Length);
            if (ok) Marshal.Copy(native, data, 0, data.Length);
            return ok;
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "(none)";
        int hash = path.LastIndexOf('#');
        string tail = hash >= 0 && hash + 1 < path.Length ? path.Substring(hash + 1) : path;
        int brace = tail.IndexOf('{');
        if (brace > 0) tail = tail.Substring(0, brace);
        if (tail.Length > 24) tail = tail.Substring(0, 24);
        int col = path.IndexOf("col", StringComparison.OrdinalIgnoreCase);
        string collection = col >= 0 && col + 5 <= path.Length ? path.Substring(col, 5) : string.Empty;
        return collection.Length > 0 ? collection + "/" + tail : tail;
    }

    private sealed class DpiReading
    {
        public DpiReading(int x, int y, byte storage)
        {
            X = x;
            Y = y;
            Storage = storage;
        }

        public int X { get; }
        public int Y { get; }
        public byte Storage { get; }
    }

    private sealed class RateReading
    {
        public RateReading(int hz, int[] rates, bool extended)
        {
            Hz = hz;
            Rates = rates;
            Extended = extended;
        }

        public int Hz { get; }
        public int[] Rates { get; }
        public bool Extended { get; }
    }
}
