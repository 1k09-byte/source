using System;

namespace kaliteConfig.Services.ChangeLog;

public sealed class ChangeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Subsystem { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Hive { get; set; } = "HKLM";
    public string KeyPath { get; set; } = "";
    public string ValueName { get; set; } = "";
    public int? BeforeValue { get; set; }
    public int? AfterValue { get; set; }
    public string Result { get; set; } = ChangeResults.Verified;
    public string SessionId { get; set; } = "";
    public string? RevertsId { get; set; }
    public string? Detail { get; set; }
}

public static class ChangeResults
{
    public const string Verified = "verified";
    public const string Failed = "failed";
    public const string Reverted = "reverted";
    public const string Drifted = "drifted";
    public const string Informational = "info";
}
