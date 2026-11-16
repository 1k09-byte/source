using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace kaliteConfig.Services;

public sealed class StartupService
{

    private const string StartupTaskId = "KaliteConfigStartup";

    public const string ScheduledTaskName = "kaliteConfig";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "kaliteConfig";

    private const int ErrorInsufficientBuffer = 122;

    public const string TrayArg = "--tray";

    public static bool IsPackaged
    {
        get
        {
            try
            {
                uint len = 0;
                return Native.Kernel32.GetCurrentPackageFullName(ref len, IntPtr.Zero) == ErrorInsufficientBuffer;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
        {
            return await IsStartupTaskEnabledAsync();
        }

        bool current = IsScheduledTaskCurrent();
        if (current && ReadRunValue(RunValueName) != null)
        {
            DeleteRunValue(RunValueName);
        }
        return current;
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (IsPackaged)
        {
            return await SetStartupTaskEnabledAsync(enabled);
        }

        try
        {
            DeleteRunValue(RunValueName);
            return enabled ? RegisterScheduledTask() : DeleteScheduledTask();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsScheduledTaskCurrent()
    {
        try
        {
            if (!RunSchtasks($"/query /tn \"{ScheduledTaskName}\" /xml ONE", out string xml))
            {
                return false;
            }

            string exe = ExecutablePath();
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }
            return xml.Contains(exe, StringComparison.OrdinalIgnoreCase)
                && xml.Contains(TrayArg, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool RegisterScheduledTask()
    {
        string exe = ExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            return false;
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), $"kaliteConfig-startup-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exe), new System.Text.UnicodeEncoding(false, true));
        }
        catch
        {
            return false;
        }

        bool created;
        try
        {
            created = RunSchtasks($"/create /tn \"{ScheduledTaskName}\" /xml \"{xmlPath}\" /f", out _);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }

        if (!created) return false;
        return IsScheduledTaskCurrent();
    }

    internal static void EnsureScheduledTaskHealthy()
    {
        if (IsPackaged) return;
        if (!RunSchtasks($"/query /tn \"{ScheduledTaskName}\"", out _)) return;
        if (!RunSchtasks($"/query /tn \"{ScheduledTaskName}\" /xml ONE", out string xml)) return;

        string exe = ExecutablePath();
        bool healthy = !string.IsNullOrEmpty(exe)
            && xml.Contains(exe, StringComparison.OrdinalIgnoreCase)
            && xml.Contains(TrayArg, StringComparison.OrdinalIgnoreCase)
            && xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", StringComparison.OrdinalIgnoreCase)
            && xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", StringComparison.OrdinalIgnoreCase)
            && xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", StringComparison.OrdinalIgnoreCase);
        if (!healthy) RegisterScheduledTask();
    }

    private static string BuildTaskXml(string exe)
    {
        string sid = string.Empty;
        try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? string.Empty; }
        catch { }

        string user = string.IsNullOrEmpty(sid) ? string.Empty : $"<UserId>{sid}</UserId>";
        string command = System.Security.SecurityElement.Escape(exe) ?? string.Empty;

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
               "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
               "<RegistrationInfo><Description>kaliteConfig background helper</Description><URI>\\kaliteConfig</URI></RegistrationInfo>" +
               "<Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT15S</Delay></LogonTrigger></Triggers>" +
               "<Principals><Principal id=\"Author\">" + user +
               "<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
               "<Settings>" +
               "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
               "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
               "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
               "<AllowHardTerminate>true</AllowHardTerminate>" +
               "<StartWhenAvailable>true</StartWhenAvailable>" +
               "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
               "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
               "<AllowStartOnDemand>true</AllowStartOnDemand>" +
               "<Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun>" +
               "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority>" +
               "</Settings>" +
               "<Actions Context=\"Author\"><Exec><Command>\"" + command + "\"</Command><Arguments>" + TrayArg + "</Arguments></Exec></Actions>" +
               "</Task>";
    }

    internal static bool DeleteScheduledTask()
    {
        RunSchtasks($"/delete /tn \"{ScheduledTaskName}\" /f", out _);
        return !RunSchtasks($"/query /tn \"{ScheduledTaskName}\"", out _);
    }

    internal static string? ReadRunValue(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    internal static void DeleteRunValue(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    internal static string ExecutablePath()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            try
            {
                using var proc = System.Diagnostics.Process.GetCurrentProcess();
                path = proc.MainModule?.FileName;
            }
            catch
            {
            }
        }
        return path ?? string.Empty;
    }

    public static bool IsTrayLaunch(string? launchArguments)
    {
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Contains(TrayArg, StringComparer.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(launchArguments)
            && launchArguments.Contains(StartupTaskId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RunSchtasks(string args, out string output)
    {
        output = string.Empty;
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            output = stdout + stderr;
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsStartupTaskEnabledAsync()
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            return task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> SetStartupTaskEnabledAsync(bool enabled)
    {
        try
        {
            var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return true;
            }

            var state = await task.RequestEnableAsync();
            return state == Windows.ApplicationModel.StartupTaskState.Enabled;
        }
        catch
        {
            return false;
        }
    }
}
