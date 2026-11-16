using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using kaliteConfig.Input.Interop;

namespace kaliteConfig.Input;

public sealed class MouseLogitechProtocol : IMouseProtocol
{
    public const ushort LogitechVendorId = 0x046D;

    private const byte LongReportId = 0x11;
    private const int LongReportSize = 20;
    private const byte SoftwareId = 0x01;
    private const int FeatureSetId = 0x0001;
    private const int DpiFeatureId = 0x2201;
    private const int ExtendedRateFeatureId = 0x8061;
    private const int StandardRateFeatureId = 0x8060;
    private const int ReplyTimeoutMs = 1000;

    private static readonly int[] AllRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };

    private readonly object _gate = new();
    private string _deviceId = string.Empty;
    private string _path = string.Empty;
    private int _deviceIndex = 0xFF;
    private int _featureDpi;
    private int _featureRateExtended;
    private int _featureRateStandard;
    private int[] _rates = Array.Empty<int>();

    public string Id => "logitech-hidpp";
    public bool EnabledByDefault => true;
    public string LastError { get; private set; } = string.Empty;
    public IReadOnlyList<int> SupportedRates => _rates;

    public static bool IsLogitech(ushort vendorId) => vendorId == LogitechVendorId;

    public bool Matches(MouseDevice device)
    {
        return device is not null && !device.IsVirtual && device.VendorId == LogitechVendorId;
    }

    public MouseDeviceSettings? TryRead(MouseDevice device)
    {
        lock (_gate)
        {
            LastError = string.Empty;
            if (!EnsureSession(device, out string sessionError))
            {
                LastError = sessionError;
                return null;
            }
            var dpi = ReadDpi(out string dpiError);
            var rate = ReadRate(out string rateError);
            if (dpi is null && rate is null)
            {
                LastError = "Logitech HID++ answered, but this model exposes neither DPI nor report rate. "
                    + dpiError + " " + rateError;
                return null;
            }
            _rates = rate is null ? Array.Empty<int>() : rate.Rates;
            var settings = new MouseDeviceSettings
            {
                DpiX = dpi?.Dpi ?? 0,
                DpiY = dpi?.Dpi ?? 0,
                RateHz = rate?.Hz ?? 0,
                LodIndex = 0,
                LodCount = 0,
                SupportsDpi = dpi is not null,
                SupportsRate = rate is not null,
                SupportsLod = false,
                SupportsMulticlick = false,
                SupportsSpdt = false,
                IsV2 = true,
                ModelName = device.Product,
            };
            LastError = string.Empty;
            return settings;
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
            if (!EnsureSession(device, out string sessionError))
            {
                LastError = sessionError;
                return false;
            }
            if (string.Equals(setting, "report-rate", StringComparison.OrdinalIgnoreCase))
                return WriteRate(value);
            if (string.Equals(setting, "dpi", StringComparison.OrdinalIgnoreCase))
                return WriteDpi(value);
            LastError = "This Logitech protocol does not expose that setting.";
            return false;
        }
    }

    private bool WriteRate(string value)
    {
        if (!int.TryParse(value, out int rate) || rate <= 0)
        {
            LastError = "That report rate is not a number.";
            return false;
        }
        if (_featureRateExtended > 0)
        {
            int code = RateCode(rate);
            if (code < 0)
            {
                LastError = rate + " Hz is not one of the HID++ extended report rates.";
                return false;
            }
            byte[]? reply = Send(_path, _deviceIndex, _featureRateExtended, 3, new byte[] { (byte)code, 0, 0 }, out string error);
            if (reply is null)
            {
                LastError = "The Logitech report-rate command failed: " + error;
                return false;
            }
            return true;
        }
        if (_featureRateStandard > 0)
        {
            if (rate <= 0 || 1000 % rate != 0)
            {
                LastError = "This Logitech model uses millisecond intervals, so " + rate + " Hz is not an exact value.";
                return false;
            }
            int interval = 1000 / rate;
            if (interval < 1 || interval > 8)
            {
                LastError = "This Logitech model supports 1 to 8 ms report intervals only.";
                return false;
            }
            byte[]? reply = Send(_path, _deviceIndex, _featureRateStandard, 2, new byte[] { (byte)interval, 0, 0 }, out string error);
            if (reply is null)
            {
                LastError = "The Logitech report-rate command failed: " + error;
                return false;
            }
            return true;
        }
        LastError = "This Logitech mouse did not expose a report-rate feature.";
        return false;
    }

    private bool WriteDpi(string value)
    {
        if (!int.TryParse(value, out int dpi) || dpi < 10 || dpi > 30000)
        {
            LastError = "That DPI value is outside the Logitech range of 10 to 30000.";
            return false;
        }
        if (_featureDpi <= 0)
        {
            LastError = "This Logitech mouse did not expose the adjustable DPI feature.";
            return false;
        }
        byte hi = (byte)((dpi >> 8) & 0xFF);
        byte lo = (byte)(dpi & 0xFF);
        byte[]? reply = Send(_path, _deviceIndex, _featureDpi, 3, new byte[] { 0, hi, lo }, out string error);
        if (reply is null)
            reply = Send(_path, _deviceIndex, _featureDpi, 3, new byte[] { 0, hi, lo, 0 }, out error);
        if (reply is null)
        {
            LastError = "The Logitech DPI command failed: " + error;
            return false;
        }
        return true;
    }

    private bool EnsureSession(MouseDevice device, out string error)
    {
        error = string.Empty;
        if (device is null)
        {
            error = "No mouse selected.";
            return false;
        }
        if (string.Equals(_deviceId, device.StableId, StringComparison.OrdinalIgnoreCase) && _path.Length > 0)
        {
            if (LookupFeature(_path, _deviceIndex, FeatureSetId) > 0) return true;
        }
        _deviceId = string.Empty;
        _path = string.Empty;
        _featureDpi = 0;
        _featureRateExtended = 0;
        _featureRateStandard = 0;
        int failures = 0;
        var details = new List<string>();
        foreach (var face in Candidates(device))
        {
            int baseIndex = 0xFF;
            if (LookupFeature(face.DevicePath, baseIndex, FeatureSetId) == 0)
            {
                baseIndex = 0x00;
                if (LookupFeature(face.DevicePath, baseIndex, FeatureSetId) == 0)
                {
                    failures++;
                    details.Add(ShortPath(face.DevicePath) + ": no HID++ 2.0 answer");
                    continue;
                }
            }
            var indices = CandidateIndices(device, baseIndex);
            foreach (int index in indices)
            {
                if (index != baseIndex && LookupFeature(face.DevicePath, index, FeatureSetId) == 0) continue;
                int dpi = LookupFeature(face.DevicePath, index, DpiFeatureId);
                int extended = LookupFeature(face.DevicePath, index, ExtendedRateFeatureId);
                int standard = LookupFeature(face.DevicePath, index, StandardRateFeatureId);
                if (dpi == 0 && extended == 0 && standard == 0) continue;
                _path = face.DevicePath;
                _deviceIndex = index;
                _featureDpi = dpi;
                _featureRateExtended = extended;
                _featureRateStandard = standard;
                _deviceId = device.StableId;
                return true;
            }
            details.Add(ShortPath(face.DevicePath) + ": HID++ 2.0 present, no settings exposed");
            failures++;
        }
        error = "Logitech HID++ 2.0 did not find readable settings. Tried " + failures
            + (failures == 1 ? " interface: " : " interfaces: ") + string.Join("; ", details)
            + ". Close Logitech G HUB if it is holding the device, then rescan.";
        return false;
    }

    private static int[] CandidateIndices(MouseDevice device, int baseIndex)
    {
        var list = new List<int>();
        if (device.BehindReceiver)
        {
            for (int index = 1; index <= 6; index++) list.Add(index);
            if (baseIndex == 0xFF) list.Add(0xFF);
        }
        else
        {
            if (baseIndex == 0xFF) list.Add(0xFF);
            for (int index = 1; index <= 6; index++) list.Add(index);
        }
        if (!list.Contains(baseIndex)) list.Add(baseIndex);
        return list.ToArray();
    }

    private DpiReading? ReadDpi(out string error)
    {
        error = string.Empty;
        if (_featureDpi <= 0)
        {
            error = "DPI feature not present.";
            return null;
        }
        byte[]? reply = Send(_path, _deviceIndex, _featureDpi, 2, new byte[] { 0, 0, 0 }, out error);
        if (reply is null || reply.Length < 3)
        {
            if (error.Length == 0) error = "DPI response was too short.";
            return null;
        }
        int dpi = (reply[1] << 8) | reply[2];
        if (dpi < 10 || dpi > 30000)
        {
            error = "DPI response was out of range (" + dpi + ").";
            return null;
        }
        return new DpiReading(dpi);
    }

    private RateReading? ReadRate(out string error)
    {
        error = string.Empty;
        if (_featureRateExtended > 0)
        {
            var rates = new List<int>();
            int mask = 0;
            for (int connection = 0; connection < 2; connection++)
            {
                byte[]? caps = Send(_path, _deviceIndex, _featureRateExtended, 1, new byte[] { (byte)connection, 0, 0 }, out _);
                if (caps is not null && caps.Length > 1 && caps[1] != 0) mask = caps[1];
                if (mask == 0)
                {
                    caps = Send(_path, _deviceIndex, _featureRateExtended, 0, new byte[] { (byte)connection, 0, 0 }, out _);
                    if (caps is not null && caps.Length > 1 && caps[1] != 0) mask = caps[1];
                }
                if (mask != 0) break;
            }
            if (mask == 0) mask = 0x7F;
            for (int bit = 0; bit < 7; bit++)
                if ((mask & (1 << bit)) != 0) rates.Add(AllRates[bit]);
            for (int connection = 0; connection < 2; connection++)
            {
                byte[]? active = Send(_path, _deviceIndex, _featureRateExtended, 2, new byte[] { (byte)connection, 0, 0 }, out _);
                if (active is null || active.Length == 0) continue;
                int code = active[0] <= 6 ? active[0] : active.Length > 1 && active[1] <= 6 ? active[1] : -1;
                if (code < 0) continue;
                int hz = AllRates[code];
                if (!rates.Contains(hz)) rates.Add(hz);
                rates.Sort();
                return new RateReading(hz, rates.ToArray(), true);
            }
            error = "The extended report-rate feature did not return an active rate.";
            return null;
        }
        if (_featureRateStandard > 0)
        {
            var rates = new List<int>();
            byte[]? caps = Send(_path, _deviceIndex, _featureRateStandard, 0, new byte[] { 0, 0, 0 }, out _);
            int mask = caps is not null && caps.Length > 0 ? caps[0] : 0;
            if (mask != 0)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((mask & (1 << bit)) == 0) continue;
                    int intervalRate = 1000 / (bit + 1);
                    if (!rates.Contains(intervalRate)) rates.Add(intervalRate);
                }
            }
            byte[]? active = Send(_path, _deviceIndex, _featureRateStandard, 1, new byte[] { 0, 0, 0 }, out _);
            if (active is null || active.Length == 0)
            {
                error = "The report-rate feature did not return an active interval.";
                return null;
            }
            int interval = active[0];
            if (interval < 1 || interval > 8)
            {
                error = "The report-rate response was out of range (" + interval + " ms).";
                return null;
            }
            int hz = 1000 / interval;
            if (!rates.Contains(hz)) rates.Add(hz);
            rates.Sort();
            return new RateReading(hz, rates.ToArray(), false);
        }
        error = "Report-rate feature not present.";
        return null;
    }

    private static int RateCode(int rate)
    {
        for (int i = 0; i < AllRates.Length; i++)
            if (AllRates[i] == rate) return i;
        return -1;
    }

    private static int LookupFeature(string path, int deviceIndex, int featureId)
    {
        byte[]? reply = Send(path, deviceIndex, 0, 0, new byte[] { (byte)((featureId >> 8) & 0xFF), (byte)(featureId & 0xFF), 0 }, out _);
        if (reply is null || reply.Length == 0) return 0;
        return reply[0];
    }

    private static byte[]? Send(string path, int deviceIndex, int featureIndex, int function, byte[] args, out string error)
    {
        error = string.Empty;
        IntPtr handle = NativeInput.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            error = "open refused (code " + Marshal.GetLastWin32Error() + ")";
            return null;
        }
        try
        {
            byte[] report = new byte[LongReportSize];
            report[0] = LongReportId;
            report[1] = (byte)deviceIndex;
            report[2] = (byte)featureIndex;
            report[3] = (byte)((function << 4) | SoftwareId);
            Array.Copy(args, 0, report, 4, Math.Min(args.Length, 16));
            if (!WriteReport(handle, report, out string writeError))
            {
                error = writeError;
                return null;
            }
            long deadline = Environment.TickCount64 + ReplyTimeoutMs;
            while (true)
            {
                int remaining = (int)(deadline - Environment.TickCount64);
                if (remaining <= 0)
                {
                    error = "no reply";
                    return null;
                }
                byte[]? input = ReadReport(handle, remaining, out string readError);
                if (input is null)
                {
                    error = readError;
                    return null;
                }
                if (input.Length < 7 || input[0] != LongReportId) continue;
                if (input[1] != (byte)deviceIndex) continue;
                if (input[2] == 0xFF)
                {
                    int code = input.Length > 5 ? input[5] : 0;
                    error = "HID++ error 0x" + code.ToString("X2");
                    return null;
                }
                if (input[2] != (byte)featureIndex) continue;
                if ((input[3] & 0xF0) != (function << 4)) continue;
                int replySoftware = input[3] & 0x0F;
                if (replySoftware != SoftwareId && replySoftware != 0) continue;
                int size = Math.Min(16, input.Length - 4);
                var payload = new byte[size];
                if (size > 0) Array.Copy(input, 4, payload, 0, size);
                return payload;
            }
        }
        finally
        {
            try { NativeInput.CloseHandle(handle); } catch { }
        }
    }

    private static bool WriteReport(IntPtr handle, byte[] report, out string error)
    {
        error = string.Empty;
        uint written = 0;
        if (NativeInput.WriteFile(handle, report, (uint)report.Length, out written, IntPtr.Zero)
            && written == (uint)report.Length)
        {
            return true;
        }
        int firstError = Marshal.GetLastWin32Error();
        if (NativeInput.HidD_SetOutputReport(handle, report, (uint)report.Length)) return true;
        error = "output report refused (code " + firstError + ", " + Marshal.GetLastWin32Error() + ")";
        return false;
    }

    private static byte[]? ReadReport(IntPtr handle, int timeoutMs, out string error)
    {
        error = string.Empty;
        byte[] buffer = new byte[64];
        uint bytesRead = 0;
        bool ok = false;
        var task = Task.Run(() =>
        {
            ok = NativeInput.ReadFile(handle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero);
        });
        bool finished;
        try
        {
            finished = task.Wait(Math.Max(20, timeoutMs));
        }
        catch
        {
            finished = true;
        }
        if (!finished)
        {
            NativeInput.CancelIoEx(handle, IntPtr.Zero);
            try { task.Wait(120); } catch { }
            error = "read timed out";
            return null;
        }
        if (!ok)
        {
            error = "read failed (code " + Marshal.GetLastWin32Error() + ")";
            return null;
        }
        if (bytesRead == 0)
        {
            error = "empty input report";
            return null;
        }
        var result = new byte[(int)Math.Min(bytesRead, (uint)buffer.Length)];
        Array.Copy(buffer, result, result.Length);
        return result;
    }

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
        if (list.Count > 8) list.RemoveRange(8, list.Count - 8);
        return list;
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
        public DpiReading(int dpi)
        {
            Dpi = dpi;
        }

        public int Dpi { get; }
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
