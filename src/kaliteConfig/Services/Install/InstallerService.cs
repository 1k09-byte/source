using kaliteConfig.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace kaliteConfig.Services
{
    public class InstallerService : IInstallerService
    {
        private static readonly HttpClient _httpClient = new();

        public async Task UninstallBrowserAsync(BrowserInstallItem item, CancellationToken ct)
        {
            string searchKeyword = InstallerProductRouter.Resolve(item?.Name, item?.ProductId);
            await Task.Run(() =>
            {

                string[] hives = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                string? uninstallString = null;

                foreach (var baseKey in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                {
                    foreach (var hive in hives)
                    {
                        using var key = baseKey.OpenSubKey(hive);
                        if (key != null)
                        {
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                using var subKey = key.OpenSubKey(subKeyName);
                                var displayName = subKey?.GetValue("DisplayName") as string;
                                if (displayName != null && displayName.Contains(searchKeyword, StringComparison.OrdinalIgnoreCase))
                                {
                                    uninstallString = subKey?.GetValue("QuietUninstallString") as string ?? subKey?.GetValue("UninstallString") as string;
                                    if (!string.IsNullOrEmpty(uninstallString))
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        if (uninstallString != null) break;
                    }
                    if (uninstallString != null) break;
                }

                if (!string.IsNullOrEmpty(uninstallString))
                {
                    string exePath = uninstallString;
                    string arguments = "";

                    if (exePath.StartsWith("\""))
                    {
                        int endQuote = exePath.IndexOf("\"", 1);
                        if (endQuote > 0)
                        {
                            arguments = exePath.Substring(endQuote + 1).Trim();
                            exePath = exePath.Substring(1, endQuote - 1);
                        }
                    }
                    else
                    {
                        int firstSpace = exePath.IndexOf(" ");
                        if (firstSpace > 0 && !exePath.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            int exeIndex = exePath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            if (exeIndex > 0)
                            {
                                arguments = exePath.Substring(exeIndex + 4).Trim();
                                exePath = exePath.Substring(0, exeIndex + 4);
                            }
                        }
                    }

                    if (searchKeyword == "Zen")
                    {
                        if (!arguments.Contains("/S")) arguments += " /S";
                    }
                    else if (searchKeyword == "Brave" || searchKeyword == "Vivaldi")
                    {
                        if (!arguments.Contains("--uninstall")) arguments += " --uninstall";
                        if (!arguments.Contains("--force-uninstall")) arguments += " --force-uninstall";
                        arguments += " --system-level";
                    }

                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = arguments,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            process.WaitForExit(30000);
                        }
                    }
                    catch { }
                }
                else if (!string.IsNullOrWhiteSpace(item?.WingetId))
                {
                    try
                    {
                        var wingetPsi = new ProcessStartInfo
                        {
                            FileName = "winget",
                            Arguments = $"uninstall --id \"{item!.WingetId}\" --exact --silent --accept-source-agreements --disable-interactivity",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        };
                        using var wingetProcess = Process.Start(wingetPsi);
                        if (wingetProcess != null)
                        {
                            wingetProcess.StandardOutput.ReadToEnd();
                            wingetProcess.StandardError.ReadToEnd();
                            wingetProcess.WaitForExit(180_000);
                        }
                    }
                    catch { }
                    WingetInventory.Invalidate();
                }

                try
                {
                    string[] procNames = searchKeyword switch
                    {
                        "Zen" => new[] { "zen" },
                        "Brave" => new[] { "brave" },
                        "Vivaldi" => new[] { "vivaldi" },
                        "Helium" => new[] { "chrome", "helium" },
                        "Epic Games Launcher" => new[] { "EpicGamesLauncher" },
                        "EA app" => new[] { "EADesktop", "EAClientService" },
                        "Ubisoft Connect" => new[] { "upc", "UbisoftConnect" },
                        "Minecraft Launcher" => new[] { "MinecraftLauncher" },
                        "Steam" => new[] { "steam", "SteamService" },
                        "Discord" => new[] { "Discord" },
                        "Telegram" => new[] { "Telegram" },
                        "WhatsApp" => new[] { "WhatsApp" },
                        _ => Array.Empty<string>()
                    };
                    
                    foreach (var procName in procNames)
                    {
                        foreach (var p in Process.GetProcessesByName(procName))
                        {
                            p.Kill();
                        }
                    }
                }
                catch { }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                
                var pathsToNuke = new List<string>();

                if (searchKeyword == "Zen")
                {
                    pathsToNuke.Add(Path.Combine(localAppData, "Programs", "Zen Browser"));
                    pathsToNuke.Add(Path.Combine(localAppData, "zen"));
                }
                else if (searchKeyword == "Brave")
                {
                    pathsToNuke.Add(Path.Combine(localAppData, "BraveSoftware"));
                    pathsToNuke.Add(Path.Combine(progFiles, "BraveSoftware"));
                    pathsToNuke.Add(Path.Combine(progFilesX86, "BraveSoftware"));
                }
                else if (searchKeyword == "Vivaldi")
                {
                    pathsToNuke.Add(Path.Combine(localAppData, "Vivaldi"));
                    pathsToNuke.Add(Path.Combine(progFiles, "Vivaldi"));
                    pathsToNuke.Add(Path.Combine(progFilesX86, "Vivaldi"));
                }
                else if (searchKeyword == "Helium")
                {
                    pathsToNuke.Add(Path.Combine(localAppData, "imput", "Helium"));
                    pathsToNuke.Add(Path.Combine(localAppData, "Helium"));
                    pathsToNuke.Add(Path.Combine(localAppData, "Programs", "Helium"));
                    pathsToNuke.Add(Path.Combine(progFiles, "imput", "Helium"));
                    pathsToNuke.Add(Path.Combine(progFiles, "Helium"));
                    pathsToNuke.Add(Path.Combine(progFilesX86, "imput", "Helium"));
                    pathsToNuke.Add(Path.Combine(progFilesX86, "Helium"));
                }

                foreach (var path in pathsToNuke)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            Directory.Delete(path, true);
                        }
                        catch { }
                    }
                }

                try
                {
                    void DeleteForcelist(string path)
                    {
                        try
                        {
                            using var lm = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path, true);
                            lm?.DeleteSubKeyTree("ExtensionInstallForcelist", false);
                        }
                        catch { }
                        try
                        {
                            using var cu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, true);
                            cu?.DeleteSubKeyTree("ExtensionInstallForcelist", false);
                        }
                        catch { }
                    }

                    if (searchKeyword == "Brave")
                    {
                        DeleteForcelist(@"SOFTWARE\Policies\BraveSoftware\Brave");
                    }
                    else if (searchKeyword == "Vivaldi")
                    {
                        DeleteForcelist(@"SOFTWARE\Policies\Vivaldi");
                    }
                    else if (searchKeyword == "Helium")
                    {
                        DeleteForcelist(@"SOFTWARE\Policies\Helium");
                        DeleteForcelist(@"SOFTWARE\Policies\Chromium");
                    }
                }
                catch { }
            });
        }

        private string? GetBrowserInstallDirectory(BrowserInstallItem item)
        {
            if (string.IsNullOrEmpty(item.InstalledCheckPath)) return null;

            var paths = item.InstalledCheckPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim());

                if (expanded.Contains('*'))
                {
                    string baseDir = expanded.Substring(0, expanded.IndexOf('*'));
                    baseDir = Path.GetDirectoryName(baseDir) ?? string.Empty;
                    string searchPattern = Path.GetFileName(expanded);

                    if (Directory.Exists(baseDir))
                    {
                        try
                        {
                            var files = Directory.GetFiles(baseDir, searchPattern, SearchOption.AllDirectories);
                            if (files.Length > 0) return Path.GetDirectoryName(files[0]);
                        }
                        catch { }
                    }
                    continue;
                }

                if (File.Exists(expanded))
                {
                    return Path.GetDirectoryName(expanded);
                }
            }

            if (IsFirefoxFamily(item))
            {
                string[] executables = GetFirefoxExecutables(item);
                foreach (string root in GetFirefoxSearchRoots(item))
                {
                    string? found = FindFirefoxAppDirectory(root, executables);
                    if (found is not null) return found;
                }
            }

            return null;
        }

        private static bool IsFirefoxFamily(BrowserInstallItem item)
            => item.Name.Contains("Zen", StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains("Firefox", StringComparison.OrdinalIgnoreCase);

        public bool IsBrowserInstalled(BrowserInstallItem item)
        {
            if (GetBrowserInstallDirectory(item) != null) return true;
            if (IsRegisteredInUninstall(item)) return true;
            return WingetInventory.IsInstalled(item.WingetId);
        }

        private static bool IsRegisteredInUninstall(BrowserInstallItem item)
        {
            string key = !string.IsNullOrWhiteSpace(item.ProductId) ? item.ProductId.Trim() : item.Name.Trim();
            if (key.Length < 3) return false;

            string[] hives =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                foreach (string hive in hives)
                {
                    try
                    {
                        using var baseKey = root.OpenSubKey(hive);
                        if (baseKey is null) continue;
                        foreach (string name in baseKey.GetSubKeyNames())
                        {
                            using var sub = baseKey.OpenSubKey(name);
                            if (sub?.GetValue("DisplayName") is not string display) continue;
                            if (display.Contains(key, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                    catch { }
                }
            }

            return false;
        }

        public void DetectUpdate(BrowserInstallItem item)
        {
            item.UpdateAvailable = false;
            if (string.IsNullOrWhiteSpace(item.LatestVersion)) return;
            if (!IsBrowserInstalled(item)) return;

            string? installed = ReadInstalledVersion(item);
            if (string.IsNullOrWhiteSpace(installed)) return;

            item.InstalledVersion = installed!;
            if (CompareVersions(installed!, item.LatestVersion) < 0)
            {
                item.UpdateAvailable = true;
                item.Status = BrowserInstallStatus.UpdateAvailable;
            }
        }

        public async Task<HashSet<string>> GetUpgradableWingetIdsAsync(CancellationToken ct)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "upgrade --accept-source-agreements --disable-interactivity",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                if (process is null) return ids;

                string stdout = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(ct);

                int idColumn = -1;
                foreach (string raw in stdout.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (idColumn < 0)
                    {
                        int i = line.IndexOf("Id", StringComparison.Ordinal);
                        if (i >= 0 && line.StartsWith("Name", StringComparison.OrdinalIgnoreCase)) idColumn = i;
                        continue;
                    }

                    string rest = line.Length > idColumn ? line.Substring(idColumn).Trim() : string.Empty;
                    if (rest.Length == 0) continue;
                    int space = rest.IndexOf(' ');
                    string id = (space >= 0 ? rest.Substring(0, space) : rest).Trim();
                    if (!LooksLikeWingetId(id)) continue;
                    ids.Add(id);
                }
            }
            catch {  }
            return ids;
        }

        private static bool LooksLikeWingetId(string token)
        {
            if (!token.Contains('.') || token[0] == '.' || token[^1] == '.') return false;
            foreach (char c in token)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static string? ReadInstalledVersion(BrowserInstallItem item)
        {
            if (WingetInventory.TryGetVersion(item.WingetId, out string inventoryVersion)
                && !string.IsNullOrWhiteSpace(inventoryVersion))
            {
                return inventoryVersion;
            }

            foreach (string raw in item.InstalledCheckPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
                try
                {
                    if (File.Exists(expanded))
                    {
                        var info = FileVersionInfo.GetVersionInfo(expanded);
                        if (!string.IsNullOrWhiteSpace(info.ProductVersion)) return info.ProductVersion.Trim();
                        if (!string.IsNullOrWhiteSpace(info.FileVersion)) return info.FileVersion.Trim();
                    }
                }
                catch {  }
            }
            return null;
        }

        internal static int CompareVersions(string a, string b)
        {
            string[] pa = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
            string[] pb = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int na = ParseSegment(i < pa.Length ? pa[i] : "0");
                int nb = ParseSegment(i < pb.Length ? pb[i] : "0");
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }

        private static int ParseSegment(string s)
        {
            string digits = new string(s.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out int v) ? v : 0;
        }

        private static List<string> GetHeliumUserDataDirs()
        {
            var candidates = new List<string>();
            string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, "imput", "Helium", "User Data"));

            try
            {
                string usersRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System)[..^7], "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (var dir in Directory.GetDirectories(usersRoot))
                    {
                        string p = Path.Combine(dir, "AppData", "Local", "imput", "Helium", "User Data");
                        if (!candidates.Contains(p)) candidates.Add(p);
                    }
                }
            }
            catch { }

            return candidates;
        }

        private static void EnsureHeliumServicesEnabled()
        {
            try
            {
                var candidates = GetHeliumUserDataDirs();

                var required = new Dictionary<string, JsonNode>
                {
                    ["helium.services.enabled"] = JsonValue.Create(true)!,
                    ["helium.services.user_consented"] = JsonValue.Create(true)!,
                    ["helium.services.consented"] = JsonValue.Create(true)!,
                    ["helium.completed_onboarding"] = JsonValue.Create(true)!,
                    ["helium.did_onboarding"] = JsonValue.Create(true)!,
                    ["helium.services.ext_proxy"] = JsonValue.Create(true)!,
                    ["helium.services.extension_proxy"] = JsonValue.Create(true)!,
                    ["helium.services.extension_updating"] = JsonValue.Create(true)!,
                };

                foreach (var userData in candidates)
                {
                    try
                    {
                        string prefPath = Path.Combine(userData, "Default", "Preferences");
                        if (!File.Exists(prefPath))
                        {
                            try { Directory.CreateDirectory(Path.GetDirectoryName(prefPath)!); } catch { }
                        }
                        if (IsFileLocked(prefPath)) continue;

                        string json = File.Exists(prefPath) ? File.ReadAllText(prefPath) : "{}";
                        if (string.IsNullOrWhiteSpace(json)) json = "{}";
                        JsonNode? root = JsonNode.Parse(json) ?? new JsonObject();
                        bool changed = false;
                        foreach (var kv in required)
                        {
                            if (SetNestedPref(root, kv.Key, kv.Value)) changed = true;
                        }
                        if (changed)
                        {
                            string tmp = prefPath + ".kaliteConfig.tmp";
                            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                            File.Move(tmp, prefPath, overwrite: true);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static bool SetNestedPref(JsonNode root, string dottedPath, JsonNode value)
        {
            string[] parts = dottedPath.Split('.');
            JsonNode current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (current is not JsonObject obj) return false;
                JsonNode? next = obj[parts[i]];
                if (next is not JsonObject && next is not null && next.GetValueKind() != JsonValueKind.Object)
                {
                    return false;
                }
                if (next is null)
                {
                    var created = new JsonObject();
                    obj[parts[i]] = created;
                    current = created;
                }
                else
                {
                    current = next;
                }
            }
            if (current is JsonObject leaf)
            {
                string last = parts[^1];
                JsonNode? existing = leaf[last];
                if (existing != null && JsonNode.DeepEquals(existing, value)) return false;
                leaf[last] = JsonNode.Parse(value.ToJsonString());
                return true;
            }
            return false;
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private const string HeliumUnpackRoot = @"C:\kcext";

        private const int HeliumLoadFlagBudget = 230;

        private const int MaxShortcutArgumentLength = 259;

        private void SeedHeliumExtensionsFromStore(BrowserInstallItem item)
        {
            List<ExtensionItem> selected = new();
            foreach (var ext in item.Extensions)
            {
                if (ext.IsSelected && ext.IsAvailable && !string.IsNullOrEmpty(ext.ChromiumExtensionId))
                    selected.Add(ext);
            }

            string commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string cacheDir = Path.Combine(commonData, "kaliteConfig", "extensions");
            try { Directory.CreateDirectory(cacheDir); } catch { }
            try { Directory.CreateDirectory(HeliumUnpackRoot); } catch { }

            var unpackedDirs = new List<string>();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int slot = 0;
            foreach (var ext in selected)
            {
                slot++;
                try
                {
                    string id = ext.ChromiumExtensionId!;
                    string crxPath = Path.Combine(cacheDir, id + ".crx");
                    if (!TryDownloadCrx(id, crxPath)) continue;
                    if (!TryParseCrx(crxPath, out string? version, out _) || version is null) continue;

                    string dest = Path.Combine(HeliumUnpackRoot, slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    string manifestPath = Path.Combine(dest, "manifest.json");
                    if (Directory.Exists(dest))
                    {
                        bool stale;
                        try
                        {
                            string? oldVersion = File.Exists(manifestPath)
                                ? JsonNode.Parse(File.ReadAllText(manifestPath))?["version"]?.GetValue<string>()
                                : null;
                            stale = oldVersion != version;
                        }
                        catch
                        {
                            stale = true;
                        }
                        if (stale)
                        {
                            try { Directory.Delete(dest, true); } catch { }
                        }
                    }
                    if (!Directory.Exists(dest) && !TryExtractCrx(crxPath, dest)) continue;

                    if (!File.Exists(manifestPath))
                    {
                        try { Directory.Delete(dest, true); } catch { }
                        continue;
                    }

                    unpackedDirs.Add(dest);
                    keep.Add(dest);
                }
                catch
                {
                }
            }

            try
            {
                foreach (var leftover in Directory.GetDirectories(HeliumUnpackRoot))
                {
                    if (keep.Contains(leftover)) continue;
                    try { Directory.Delete(leftover, true); } catch { }
                }
            }
            catch { }

            var loadable = new List<string>();
            int used = 0;
            foreach (var dir in unpackedDirs)
            {
                int cost = dir.Length + (loadable.Count == 0 ? 0 : 1);
                if (used + cost > HeliumLoadFlagBudget)
                {
                    UpdateCheckService.LogDiag($"Helium: {dir} left out, shortcut argument budget reached");
                    continue;
                }
                loadable.Add(dir);
                used += cost;
            }

            string? loadFlag = loadable.Count == 0
                ? null
                : "--load-extension=\"" + string.Join(",", loadable) + "\"";

            string infoPath = Path.Combine(HeliumUnpackRoot, "helium-load-extension.txt");
            try
            {
                if (loadFlag is null)
                {
                    if (File.Exists(infoPath)) File.Delete(infoPath);
                }
                else File.WriteAllText(infoPath, loadFlag);
            }
            catch { }

            PatchHeliumShortcuts(loadFlag);
        }

        private static void PatchHeliumShortcuts(string? loadFlag)
        {
            try
            {
                var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidates = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"),
                };
                foreach (var dir in candidates)
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) searchDirs.Add(dir);
                }

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;

                foreach (var dir in searchDirs)
                {
                    string[] shortcuts;
                    try { shortcuts = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                    catch { continue; }

                    foreach (var lnk in shortcuts)
                    {
                        try
                        {
                            dynamic shortcut = shell.CreateShortcut(lnk);
                            string target = (string)shortcut.TargetPath;
                            if (!IsHeliumShortcutTarget(target)) continue;

                            string args = (string)shortcut.Arguments;
                            string cleaned = StripLoadExtensionArgument(args);
                            string desired = loadFlag is null
                                ? cleaned
                                : (cleaned.Length > 0 ? cleaned + " " + loadFlag : loadFlag);

                            if (string.Equals(args, desired, StringComparison.Ordinal)) continue;
                            if (desired.Length > MaxShortcutArgumentLength)
                            {
                                UpdateCheckService.LogDiag($"Helium: skipped {lnk}, arguments would exceed the shortcut limit");
                                continue;
                            }

                            shortcut.Arguments = desired;
                            shortcut.Save();
                        }
                        catch { }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsHeliumShortcutTarget(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            string file = Path.GetFileName(target);
            if (!file.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
                && !file.Equals("helium.exe", StringComparison.OrdinalIgnoreCase)) return false;
            return target.Contains("helium", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripLoadExtensionArgument(string args)
        {
            if (string.IsNullOrEmpty(args)) return string.Empty;
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                args, "--load-extension=(?:\"[^\"]*\"|\\S*)", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned, "--load-extension\\S*", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return cleaned.Trim();
        }

        private static void RemoveHeliumForcelist()
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                foreach (var parent in new[] { @"SOFTWARE\Policies\Helium", @"SOFTWARE\Policies\Chromium" })
                {
                    try
                    {
                        using var key = hive.OpenSubKey(parent, true);
                        key?.DeleteSubKey("ExtensionInstallForcelist", false);
                    }
                    catch { }
                }
            }
        }

        private bool TryDownloadCrx(string id, string crxPath)
        {
            try
            {
                if (File.Exists(crxPath) && new FileInfo(crxPath).Length > 1024)
                {
                    if (HasValidCrxMagic(crxPath)) return true;
                    try { File.Delete(crxPath); } catch { }
                }

                string url = $"https://clients2.google.com/service/update2/crx?response=redirect&prodversion=152.0.7977.82&acceptformat=crx2,crx3&x=id%3D{id}%26installsource%3Dondemand%26uc";
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                byte[] data = _httpClient.GetByteArrayAsync(url, cts.Token).GetAwaiter().GetResult();
                if (data.Length < 1024) return false;
                string tmp = crxPath + ".download";
                File.WriteAllBytes(tmp, data);
                File.Move(tmp, crxPath, overwrite: true);
                return HasValidCrxMagic(crxPath);
            }
            catch (Exception)
            {
                return File.Exists(crxPath) && HasValidCrxMagic(crxPath);
            }
        }

        private static bool HasValidCrxMagic(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] magic = new byte[4];
                if (fs.Read(magic, 0, 4) != 4) return false;
                return magic[0] == (byte)'C' && magic[1] == (byte)'r' && magic[2] == (byte)'2' && magic[3] == (byte)'4';
            }
            catch { return false; }
        }

        private static bool TryParseCrx(string crxPath, out string? version, out JsonNode? manifest)
        {
            version = null;
            manifest = null;
            try
            {
                using var fs = new FileStream(crxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                byte[] magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != (byte)'C' || magic[1] != (byte)'r' || magic[2] != (byte)'2' || magic[3] != (byte)'4')
                    return false;
                uint format = br.ReadUInt32();
                long zipOffset;
                if (format == 3)
                {
                    uint headerSize = br.ReadUInt32();
                    zipOffset = 12 + headerSize;
                }
                else if (format == 2)
                {
                    uint pubkeyLen = br.ReadUInt32();
                    uint sigLen = br.ReadUInt32();
                    zipOffset = 16 + pubkeyLen + sigLen;
                }
                else return false;

                fs.Seek(zipOffset, SeekOrigin.Begin);
                var zipBytes = new byte[fs.Length - zipOffset];
                int read = 0;
                while (read < zipBytes.Length)
                {
                    int n = fs.Read(zipBytes, read, zipBytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != zipBytes.Length) return false;
                using var zipStream = new MemoryStream(zipBytes, writable: false);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                var entry = archive.GetEntry("manifest.json");
                if (entry is null) return false;
                using var reader = new StreamReader(entry.Open());
                string json = reader.ReadToEnd();
                manifest = JsonNode.Parse(json);
                version = manifest?["version"]?.GetValue<string>();
                return !string.IsNullOrEmpty(version) && manifest is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryExtractCrx(string crxPath, string destDir)
        {
            try
            {
                if (Directory.Exists(destDir))
                {
                    if (File.Exists(Path.Combine(destDir, "manifest.json"))) return true;
                    try { Directory.Delete(destDir, true); } catch { return false; }
                }

                using var fs = new FileStream(crxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                br.ReadBytes(4);
                uint format = br.ReadUInt32();
                long zipOffset = format == 3 ? 12 + br.ReadUInt32() : 16 + br.ReadUInt32() + br.ReadUInt32();
                fs.Seek(zipOffset, SeekOrigin.Begin);
                var zipBytes = new byte[fs.Length - zipOffset];
                int read = 0;
                while (read < zipBytes.Length)
                {
                    int n = fs.Read(zipBytes, read, zipBytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != zipBytes.Length) return false;
                using var zipStream = new MemoryStream(zipBytes, writable: false);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                Directory.CreateDirectory(destDir);
                foreach (var entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(target, Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                return File.Exists(Path.Combine(destDir, "manifest.json"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string? DeployExtensions(BrowserInstallItem item)
        {
            try
            {
                if (item.Name.Contains("Brave", StringComparison.OrdinalIgnoreCase))
                {
                    var chromeInstaller = new ChromiumExtensionInstaller();
                    chromeInstaller.InstallExtensions(item.Extensions, @"SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallForcelist");
                }
                else if (item.Name.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase))
                {
                    var chromeInstaller = new ChromiumExtensionInstaller();
                    chromeInstaller.InstallExtensions(item.Extensions, @"SOFTWARE\Policies\Vivaldi\ExtensionInstallForcelist");
                }
                else if (item.Name.Contains("Helium", StringComparison.OrdinalIgnoreCase))
                {
                    RemoveHeliumForcelist();
                    EnsureHeliumServicesEnabled();
                    SeedHeliumExtensionsFromStore(item);
                }
                else if (item.Name.Contains("Zen", StringComparison.OrdinalIgnoreCase)
                    || item.Name.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
                {
                    return DeployFirefoxExtensions(item);
                }
            }
            catch (Exception ex)
            {
                return $"Extensions were not applied: {ex.Message}";
            }

            return null;
        }

        private string? DeployFirefoxExtensions(BrowserInstallItem item)
        {
            var managed = new List<string>();
            var selected = new List<string>();
            foreach (var ext in item.Extensions)
            {
                if (!ext.IsAvailable || string.IsNullOrWhiteSpace(ext.FirefoxAddonSlug)) continue;
                string url = $"https://addons.mozilla.org/firefox/downloads/latest/{ext.FirefoxAddonSlug.Trim()}";
                managed.Add(url);
                if (ext.IsSelected) selected.Add(url);
            }

            if (managed.Count == 0) return null;

            string? appDir = ResolveFirefoxAppDirectory(item);
            string policyDir = appDir is null ? string.Empty : Path.Combine(appDir, "distribution");
            string policyPath = Path.Combine(policyDir, "policies.json");

            if (appDir is null)
            {
                return selected.Count == 0
                    ? null
                    : $"Extensions were not applied: could not find the {item.Name} application folder.";
            }

            if (selected.Count == 0 && !File.Exists(policyPath)) return null;

            var firefoxInstaller = new FirefoxExtensionInstaller();
            return firefoxInstaller.SetInstallUrls(policyDir, managed, selected)
                ? null
                : $"Extensions were not applied: could not write {policyPath}.";
        }

        private static readonly string[] FirefoxAppExecutables =
        {
            "firefox.exe",
            "zen.exe",
        };

        private string? ResolveFirefoxAppDirectory(BrowserInstallItem item)
        {
            if (!IsFirefoxFamily(item)) return null;

            string? detected = GetBrowserInstallDirectory(item);
            if (detected is not null && IsFirefoxAppDirectory(detected)) return detected;

            string[] executables = GetFirefoxExecutables(item);
            foreach (string root in GetFirefoxSearchRoots(item))
            {
                string? found = FindFirefoxAppDirectory(root, executables);
                if (found is not null) return found;
            }

            return detected;
        }

        private static string[] GetFirefoxExecutables(BrowserInstallItem item)
        {
            if (item.Name.Contains("Zen", StringComparison.OrdinalIgnoreCase))
                return new[] { "zen.exe", "firefox.exe" };
            return new[] { "firefox.exe" };
        }

        private static IEnumerable<string> GetFirefoxSearchRoots(BrowserInstallItem item)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var roots = new List<string>();

            if (item.Name.Contains("Zen", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(Path.Combine(programFiles, "Zen Browser"));
                roots.Add(Path.Combine(local, "Programs", "Zen Browser"));
                roots.Add(Path.Combine(local, "zen"));
                roots.Add(Path.Combine(desktop, "Zen Browser"));
            }
            else
            {
                roots.Add(programFiles);
                roots.Add(programFilesX86);
            }

            return roots.Where(Directory.Exists);
        }

        private static bool IsFirefoxAppDirectory(string directory)
        {
            try
            {
                if (File.Exists(Path.Combine(directory, "application.ini"))) return true;
                return FirefoxAppExecutables.Any(name => File.Exists(Path.Combine(directory, name)));
            }
            catch
            {
                return false;
            }
        }

        private static string? FindFirefoxAppDirectory(string root, string[] executables)
        {
            try
            {
                foreach (string exe in executables)
                {
                    string? fallback = null;
                    foreach (string file in EnumerateFilesBounded(root, exe, 3))
                    {
                        string? dir = Path.GetDirectoryName(file);
                        if (dir is null) continue;
                        if (File.Exists(Path.Combine(dir, "application.ini"))) return dir;
                        fallback ??= dir;
                    }
                    if (fallback is not null) return fallback;
                }
            }
            catch
            {
            }

            return null;
        }

        private static IEnumerable<string> EnumerateFilesBounded(string root, string pattern, int maxDepth)
        {
            var pending = new Queue<(string Directory, int Depth)>();
            pending.Enqueue((root, 0));

            while (pending.Count > 0)
            {
                var (directory, depth) = pending.Dequeue();

                string[] files;
                try { files = Directory.GetFiles(directory, pattern); }
                catch { files = Array.Empty<string>(); }
                foreach (string file in files) yield return file;

                if (depth >= maxDepth) continue;

                string[] children;
                try { children = Directory.GetDirectories(directory); }
                catch { continue; }

                foreach (string child in children)
                {
                    string name = Path.GetFileName(child);
                    if (name.Equals("Profiles", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("cache", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("cache2", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("startupCache", StringComparison.OrdinalIgnoreCase))
                        continue;
                    pending.Enqueue((child, depth + 1));
                }
            }
        }

        public string? ApplyExtensions(BrowserInstallItem item)
        {
            if (item is null) return "No app selected.";
            return DeployExtensions(item);
        }

        public async Task InstallBrowserAsync(BrowserInstallItem item, IProgress<BrowserInstallStatus> progress, IProgress<double> downloadProgress, IProgress<string> errorProgress, CancellationToken ct)
        {
            await RunInstallAsync(item, progress, downloadProgress, errorProgress, ct, isUpdate: false);
        }

        public async Task UpdateBrowserAsync(BrowserInstallItem item, IProgress<BrowserInstallStatus> progress, IProgress<double> downloadProgress, IProgress<string> errorProgress, CancellationToken ct)
        {
            await RunInstallAsync(item, progress, downloadProgress, errorProgress, ct, isUpdate: true);
        }

        private async Task RunInstallAsync(BrowserInstallItem item, IProgress<BrowserInstallStatus> progress, IProgress<double> downloadProgress, IProgress<string> errorProgress, CancellationToken ct, bool isUpdate)
        {
            if (IsBrowserInstalled(item) && !isUpdate)
            {
                string? warning = DeployExtensions(item);
                if (warning is not null) errorProgress.Report(warning);
                progress.Report(BrowserInstallStatus.AlreadyInstalled);
                return;
            }

            if (isUpdate) progress.Report(BrowserInstallStatus.Downloading);

            if (!string.IsNullOrEmpty(item.ToolInstallDir))
            {
                await InstallPortableToolAsync(item, progress, downloadProgress, errorProgress, ct);
                return;
            }

            if (!string.IsNullOrEmpty(item.WingetId))
            {
                await InstallViaWingetAsync(item, progress, errorProgress, ct);
                return;
            }

            progress.Report(BrowserInstallStatus.Downloading);
            downloadProgress.Report(0);
            string tempPath = Path.Combine(Path.GetTempPath(), item.InstallerFileName);
            try
            {
                using var response = await _httpClient.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalRead = 0L;
                var bytesRead = 0;
                var lastReport = DateTime.UtcNow;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    totalRead += bytesRead;

                    if (totalBytes != -1 && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                    {
                        var percentage = (double)totalRead / totalBytes * 100.0;
                        downloadProgress.Report(percentage);
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CleanUp(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                CleanUp(tempPath);
                errorProgress.Report($"Download failed: {ex.Message}");
                progress.Report(BrowserInstallStatus.Failed);
                return;
            }

            ct.ThrowIfCancellationRequested();

            progress.Report(BrowserInstallStatus.Installing);
            try
            {
                var isMsi = Path.GetExtension(tempPath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = isMsi 
                        ? $"/c start /wait \"\" msiexec.exe /i \"{tempPath}\" {item.SilentInstallArgs}" 
                        : $"/c start /wait \"\" \"{tempPath}\" {item.SilentInstallArgs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process is not null)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
                    try
                    {
                        await process.WaitForExitAsync(timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                        errorProgress.Report("Installer timed out after 10 minutes and was terminated. Verification will continue if files were written.");
                    }

                    try
                    {
                        if (process.HasExited && process.ExitCode != 0)
                        {
                        }
                    }
                    catch { }
                }
                else
                {
                    await Task.Delay(2000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                CleanUp(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                CleanUp(tempPath);
                errorProgress.Report($"Install failed: {ex.Message}");
                progress.Report(BrowserInstallStatus.Failed);
                return;
            }

            bool verified = false;
            for (int i = 0; i < 240; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (IsBrowserInstalled(item))
                {
                    verified = true;
                    await Task.Delay(2000, ct);
                    break;
                }
                await Task.Delay(500, ct);
            }

            if (!verified)
            {
                errorProgress?.Report("Installation failed verification check.");
                progress?.Report(BrowserInstallStatus.Failed);
                return;
            }

            string? deployWarning = DeployExtensions(item);
            if (deployWarning is not null) errorProgress?.Report(deployWarning);

            progress?.Report(BrowserInstallStatus.Installed);

            CleanUp(tempPath);
        }

        private async Task InstallViaWingetAsync(BrowserInstallItem item, IProgress<BrowserInstallStatus> progress, IProgress<string> errorProgress, CancellationToken ct)
        {
            progress.Report(BrowserInstallStatus.Installing);
            try
            {
			bool elevated = WindowsSettingsService.IsElevated();
			string baseArgs = $"install --id \"{item.WingetId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

			int exitCode = 1;
			string stdout = string.Empty;
			string stderr = string.Empty;
			bool retriedMachineScope = false;
			bool refreshedSources = false;
			for (; ; )
			{
				var attempt = await RunWingetAsync(baseArgs, ct);
				exitCode = attempt.ExitCode;
				stdout = attempt.Stdout;
				stderr = attempt.Stderr;
				retriedMachineScope = false;
				if (exitCode != 0 && elevated)
				{
					var retry = await RunWingetAsync(baseArgs + " --scope machine", ct);
					retriedMachineScope = true;
					if (retry.ExitCode == 0)
					{
						exitCode = 0;
						stdout = retry.Stdout;
						stderr = retry.Stderr;
					}
					else
					{
						stderr += "\n(note: also tried --scope machine, which failed too)";
					}
				}
				if (exitCode == 0) break;
				if (!refreshedSources && LooksLikeMissingPackage(stdout, stderr))
				{
					refreshedSources = true;
					UpdateCheckService.LogDiag($"winget {item.WingetId}: no package found, refreshing sources and retrying once");
					try { await RunWingetAsync("source update --disable-interactivity", ct); } catch { }
					continue;
			}
				break;
			}
                if (exitCode != 0)
                {
                    LogWingetFailure(item.WingetId, baseArgs, exitCode, stdout, stderr, retriedMachineScope);
                    errorProgress.Report(DescribeWingetFailure(item.WingetId, exitCode, stdout, stderr, elevated, retriedMachineScope));
                    progress.Report(BrowserInstallStatus.Failed);
                    return;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                UpdateCheckService.LogDiag($"winget {item.WingetId}: launcher threw {ex.GetType().Name} - {ex.Message}");
                errorProgress.Report(DescribeWingetLaunchFailure(item.WingetId, ex));
                progress.Report(BrowserInstallStatus.Failed);
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.WingetId))
            {
                WingetInventory.Invalidate();
                await WingetInventory.RefreshAsync();
            }

            bool verified = false;
            if (string.IsNullOrWhiteSpace(item.InstalledCheckPath))
            {
                verified = true;
            }
            else for (int i = 0; i < 60; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (IsBrowserInstalled(item)) { verified = true; break; }
                await Task.Delay(2000, ct);
            }
            if (!verified)
            {
                errorProgress.Report("winget finished but the app was not detected afterwards.");
                progress.Report(BrowserInstallStatus.Failed);
                return;
            }

            string? deployWarning = DeployExtensions(item);
            if (deployWarning is not null) errorProgress.Report(deployWarning);

            progress.Report(BrowserInstallStatus.Installed);
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunWingetAsync(string arguments, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start winget. Is App Installer installed?");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("winget timed out after 10 minutes and was terminated.");
            }
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }

        private static string DescribeWingetFailure(string wingetId, int exitCode, string stdout, string stderr, bool elevated, bool retriedMachineScope)
        {
            string? detail = PickWingetErrorLine(stdout, stderr);
            string message = $"winget could not install {wingetId} (exit code {exitCode}).";
            if (!string.IsNullOrEmpty(detail))
                message += " " + detail;
            if (retriedMachineScope)
                message += " Retried with machine scope — that failed too, so this is not just a scope problem.";
            else if (elevated)
                message += " kaliteConfig runs elevated; user-scope and Store installers often fail from an elevated caller.";
            message += " If every app fails the same way, run `winget source update` in a terminal and update App Installer from the Microsoft Store.";
            message += " Full log: %LOCALAPPDATA%\\Packages\\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\\LocalState\\DiagOutputDir.";
            return message;
        }	private static string TruncateForLog(string? t, int maxChars = 6000)
	{
		if (string.IsNullOrEmpty(t)) return string.Empty;
		string s = t.Replace('\r', '\n');
		return s.Length > maxChars ? s.Substring(0, maxChars) + "\n...(truncated)" : s;
	}

	private static void LogWingetFailure(string wingetId, string args, int exitCode, string stdout, string stderr, bool retriedMachineScope)
	{
		UpdateCheckService.LogDiag($"winget {wingetId} exit {exitCode} (scope-retry: {retriedMachineScope}) args: {args}\n--- stdout ---\n{TruncateForLog(stdout)}\n--- stderr ---\n{TruncateForLog(stderr)}");
	}

	private static string DescribeWingetLaunchFailure(string wingetId, Exception ex)
	{
		if (ex is System.ComponentModel.Win32Exception)
			return $"winget could not be started for {wingetId} ({ex.Message}). Reinstall or update \"App Installer\" from the Microsoft Store, then try again. Full detail in %LOCALAPPDATA%\\kaliteConfig\\logs\\updater.log.";
		return $"Install failed for {wingetId}: {ex.Message} Full detail in %LOCALAPPDATA%\\kaliteConfig\\logs\\updater.log.";
	}

	private static bool LooksLikeMissingPackage(string stdout, string stderr)
	{
		string hay = ((stderr ?? string.Empty) + "\n" + (stdout ?? string.Empty)).ToLowerInvariant();
		return hay.Contains("no package found matching input criteria");
	}

	internal static string? PickWingetErrorLine(string stdout, string stderr)
        {
            string? fallback = null;
            foreach (string raw in ((stderr ?? "") + "\n" + (stdout ?? "")).Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.StartsWith("Windows Package Manager", StringComparison.Ordinal)
                    || line.StartsWith("\u00A9", StringComparison.Ordinal)
                    || line.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase)) continue;
                fallback ??= line;
                if (line.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("exit code", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("no package", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0
                    || line.IndexOf("agreement", StringComparison.OrdinalIgnoreCase) >= 0)
                    return line.Length > 300 ? line.Substring(0, 300) + "\u2026" : line;
            }
            if (fallback is not null && fallback.Length > 300) fallback = fallback.Substring(0, 300) + "\u2026";
            return fallback;
        }

        private async Task InstallPortableToolAsync(BrowserInstallItem item, IProgress<BrowserInstallStatus> progress, IProgress<double> downloadProgress, IProgress<string> errorProgress, CancellationToken ct)
        {
            progress.Report(BrowserInstallStatus.Downloading);
            downloadProgress.Report(0);
            string tempPath = Path.Combine(Path.GetTempPath(), item.InstallerFileName);

            try
            {
                using var response = await _httpClient.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalRead = 0L;
                var bytesRead = 0;
                var lastReport = DateTime.UtcNow;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) != 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    totalRead += bytesRead;
                    if (totalBytes != -1 && (DateTime.UtcNow - lastReport).TotalMilliseconds > 100)
                    {
                        downloadProgress.Report((double)totalRead / totalBytes * 100.0);
                        lastReport = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException) { CleanUp(tempPath); throw; }
            catch (Exception ex)
            {
                CleanUp(tempPath);
                errorProgress.Report($"Download failed: {ex.Message}");
                progress.Report(BrowserInstallStatus.Failed);
                return;
            }

            ct.ThrowIfCancellationRequested();
            progress.Report(BrowserInstallStatus.Installing);

            try
            {
                string toolDir = Environment.ExpandEnvironmentVariables(item.ToolInstallDir);
                Directory.CreateDirectory(toolDir);

                using (var archive = ZipFile.OpenRead(tempPath))
                {
                    bool hasExeEntries = archive.Entries.Any(e => e.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    foreach (var entry in archive.Entries)
                    {
                        if (hasExeEntries && !entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                        string destPath = Path.Combine(toolDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }

                PreAcceptSysinternalsEula(toolDir, item.Name);
                CreateStartMenuShortcut(item.Name, ResolveToolExe(toolDir, item.ToolExeName), toolDir);

                progress.Report(BrowserInstallStatus.Installed);
            }
            catch (OperationCanceledException) { CleanUp(tempPath); throw; }
            catch (Exception ex)
            {
                errorProgress.Report($"Install failed: {ex.Message}");
                progress.Report(BrowserInstallStatus.Failed);
            }
            finally
            {
                CleanUp(tempPath);
            }
        }

        private static void PreAcceptSysinternalsEula(string toolDir, string toolName)
        {
            try
            {
                string subKey = @"SOFTWARE\Sysinternals\" + (string.IsNullOrWhiteSpace(toolName) ? "kaliteTools" : toolName.Trim());
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey);
                key.SetValue("EulaAccepted", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }

            try
            {
                string marker = Path.Combine(toolDir, "EulaAccepted.txt");
                if (!File.Exists(marker)) File.WriteAllText(marker, "EULA accepted by kaliteConfig installer.");
            }
            catch { }
        }

        private static string ResolveToolExe(string toolDir, string preferredName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    string preferred = Path.Combine(toolDir, preferredName.Trim());
                    if (File.Exists(preferred)) return preferred;
                }
                string? first = Directory.GetFiles(toolDir, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!string.IsNullOrEmpty(first)) return first;
            }
            catch { }
            return Path.Combine(toolDir, preferredName ?? string.Empty);
        }

        private static void CreateStartMenuShortcut(string name, string targetExe, string workingDir)
        {
            try
            {
                if (!File.Exists(targetExe)) return;
                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                string shortcutDir = Path.Combine(programsDir, "Programs", "kaliteTools");
                Directory.CreateDirectory(shortcutDir);

                string escapedTarget = targetExe.Replace("'", "''");
                string escapedDir = workingDir.Replace("'", "''");
                string escapedLnk = Path.Combine(shortcutDir, $"{name}.lnk").Replace("'", "''");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{escapedLnk}'); $s.TargetPath = '{escapedTarget}'; $s.WorkingDirectory = '{escapedDir}'; $s.Save()\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
            }
            catch { }
        }

        private static void CleanUp(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
