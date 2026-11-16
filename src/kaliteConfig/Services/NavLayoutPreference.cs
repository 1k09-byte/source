using System;
using System.IO;

namespace kaliteConfig.Services;

internal static class NavLayoutPreference
{
    public const string RailTag = "Rail";
    public const string TopTag = "Top";
    public const string DockTag = "Bottom";

    private static string PrefPath(string name) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kaliteConfig", name);

    public static string Layout { get; private set; } = RailTag;

    static NavLayoutPreference() => Load();

    private static void Load()
    {
        try
        {
            Layout = Normalize(File.ReadAllText(PrefPath("nav-layout.txt")).Trim());
        }
        catch { }
    }

    private static string Normalize(string tag) =>
        tag == DockTag || tag == TopTag ? tag : RailTag;

    public static void SaveLayout(string tag)
    {
        Layout = Normalize(tag);
        try
        {
            string path = PrefPath("nav-layout.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Layout);
        }
        catch { }
    }
}
