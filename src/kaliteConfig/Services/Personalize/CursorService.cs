using System;
using System.Collections.Generic;
using System.Globalization;
using kaliteConfig.Services.Personalize.Native;

namespace kaliteConfig.Services.Personalize;

public sealed class CursorService : ICursorService
{
    public const string MouseKey = @"Control Panel\Mouse";
    public const string DesktopKey = @"Control Panel\Desktop";

    public const int MaxWheelLines = 100;
    public const int MaxTrails = 12;

    private const uint SpiFlags = NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE;

    private readonly SettingApplier _applier;

    public CursorService(SettingApplier applier) => _applier = applier;

    public MouseOptions CurrentMouse() => new(
        ReadInt(MouseKey, "MouseSensitivity", 10),
        ReadInt(MouseKey, "MouseSpeed", 0) == 1,
        ReadInt(MouseKey, "MouseTrails", 0),
        ReadInt(DesktopKey, "WheelScrollLines", 3));

    public IReadOnlyList<SettingResult> ApplyMouse(MouseOptions o)
    {
        if (o.PointerSpeed < 1 || o.PointerSpeed > 20)
            return Fail("cursor.mouse.speed", "Pointer speed must be between 1 and 20.");
        if (o.Trails < 0 || o.Trails > MaxTrails)
            return Fail("cursor.mouse.trails", $"Pointer trails must be between 0 and {MaxTrails}.");
        if (o.WheelLines < 1 || o.WheelLines > MaxWheelLines)
            return Fail("cursor.wheel", $"Scroll lines must be between 1 and {MaxWheelLines}.");

        string t1 = o.EnhancedPrecision ? "6" : "0";
        string t2 = o.EnhancedPrecision ? "10" : "0";
        string accel = o.EnhancedPrecision ? "1" : "0";

        return _applier.Apply(new[]
        {
            new SettingDef("cursor.mouse.speed", MouseKey, "MouseSensitivity", Microsoft.Win32.RegistryValueKind.String, o.PointerSpeed.ToString(CultureInfo.InvariantCulture), SyncSpeed),
            new SettingDef("cursor.mouse.t1", MouseKey, "MouseThreshold1", Microsoft.Win32.RegistryValueKind.String, t1, SyncMouseCurve),
            new SettingDef("cursor.mouse.t2", MouseKey, "MouseThreshold2", Microsoft.Win32.RegistryValueKind.String, t2, SyncMouseCurve),
            new SettingDef("cursor.mouse.accel", MouseKey, "MouseSpeed", Microsoft.Win32.RegistryValueKind.String, accel, SyncMouseCurve),
            new SettingDef("cursor.mouse.trails", MouseKey, "MouseTrails", Microsoft.Win32.RegistryValueKind.String, o.Trails.ToString(CultureInfo.InvariantCulture), SyncTrails),
            new SettingDef("cursor.wheel", DesktopKey, "WheelScrollLines", Microsoft.Win32.RegistryValueKind.String, o.WheelLines.ToString(CultureInfo.InvariantCulture), SyncWheel),
        });
    }

    private static bool SyncSpeed() =>
        NativeMethods.SystemParametersInfoPtr(NativeMethods.SPI_SETMOUSESPEED, 0,
            new IntPtr(ReadInt(MouseKey, "MouseSensitivity", 10)), SpiFlags);

    private static bool SyncMouseCurve()
    {
        var curve = new[]
        {
            ReadInt(MouseKey, "MouseThreshold1", 0),
            ReadInt(MouseKey, "MouseThreshold2", 0),
            ReadInt(MouseKey, "MouseSpeed", 0),
        };
        return NativeMethods.SystemParametersInfoIntArray(NativeMethods.SPI_SETMOUSE, 0, curve, SpiFlags);
    }

    private static bool SyncTrails() =>
        NativeMethods.SystemParametersInfoPtr(NativeMethods.SPI_SETMOUSETRAILS,
            (uint)ReadInt(MouseKey, "MouseTrails", 0), IntPtr.Zero, SpiFlags);

    private static bool SyncWheel() =>
        NativeMethods.SystemParametersInfoPtr(NativeMethods.SPI_SETWHEELSCROLLLINES,
            (uint)ReadInt(DesktopKey, "WheelScrollLines", 3), IntPtr.Zero, SpiFlags);

    private static IReadOnlyList<SettingResult> Fail(string id, string message) =>
        new[] { new SettingResult(id, false, message) };

    private static int ReadInt(string subKey, string name, int fallback)
    {
        var e = HkcuRegistry.ReadEntry(subKey, name);
        if (!e.Exists) return fallback;
        var text = Convert.ToString(e.Data, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
