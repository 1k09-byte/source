using System.Collections.Generic;

namespace kaliteConfig.Services.Personalize;

public sealed record MouseOptions(int PointerSpeed, bool EnhancedPrecision, int Trails, int WheelLines);

public interface ICursorService
{
    MouseOptions CurrentMouse();

    IReadOnlyList<SettingResult> ApplyMouse(MouseOptions options);
}
