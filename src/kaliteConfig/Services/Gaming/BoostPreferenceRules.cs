using System;
using System.Collections.Generic;
using System.Linq;

namespace kaliteConfig.Services;

public readonly record struct ThreadBoostPreference(
    string Description, string StartAddress, bool Enabled, int Tid, int CreatorPid);

public static class BoostPreferenceRules
{
    public const string ProcessLayerName = "process";
    public const string ThreadLayerName = "thread";

    public static IReadOnlyList<string> KeeperBoostApplyOrder { get; } =
        new[] { ProcessLayerName, ThreadLayerName };

    public static async System.Threading.Tasks.Task ApplyBoostLayersInOrderAsync(
        System.Func<System.Threading.Tasks.Task> processLayer,
        System.Func<System.Threading.Tasks.Task> threadLayer)
    {
        await processLayer();
        await threadLayer();
    }

    public static string NormalizeDescription(string? description)
    {
        string text = (description ?? string.Empty).Trim();
        return text.Length == 0 || string.Equals(text, "(unnamed)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : text;
    }

    public static string NormalizeAddress(string? startAddress)
    {
        string text = (startAddress ?? string.Empty).Trim();
        return text == "-" ? string.Empty : text;
    }

    public static bool IdentityMatches(
        string storedDescription, string storedStartAddress,
        string description, string startAddress)
    {
        string storedName = NormalizeDescription(storedDescription);
        if (storedName.Length > 0)
        {
            return string.Equals(storedName, NormalizeDescription(description), StringComparison.OrdinalIgnoreCase);
        }

        string storedAddress = NormalizeAddress(storedStartAddress);
        return storedAddress.Length > 0
            && string.Equals(storedAddress, NormalizeAddress(startAddress), StringComparison.OrdinalIgnoreCase);
    }

    public static bool? ResolveDesiredState(
        IEnumerable<ThreadBoostPreference> prefs,
        int tid, string description, string startAddress,
        IReadOnlyCollection<int>? liveTids = null)
    {
        bool? wanted = null;
        foreach (var p in prefs)
        {
            bool fromThisRun = p.CreatorPid == Environment.ProcessId;
            if (fromThisRun && p.Tid == tid) return p.Enabled;
            if (fromThisRun && (liveTids is null || liveTids.Contains(p.Tid))) continue;

            if (IdentityMatches(p.Description, p.StartAddress, description, startAddress))
                wanted = p.Enabled;
        }
        return wanted;
    }
}
