using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[Flags]
public enum DepotPlatform
{
    Unknown = 0,
    Windows = 1,
    Linux   = 2,
    MacOS   = 4,
    Shared  = 8
}

public sealed class DepotPlatformResult
{
    public uint DepotId { get; init; }
    public DepotPlatform Platform { get; init; }
    public string Source { get; init; } = "";
    public string Details { get; init; } = "";
}

public static class SteamDepotPlatformResolver
{
    public static DepotPlatformResult ResolveFromOsList(uint depotId, string? osList)
    {
        string os = (osList ?? "").Trim().ToLowerInvariant();

        if (os.Contains("windows") || os == "win")
            return new DepotPlatformResult { DepotId = depotId, Platform = DepotPlatform.Windows, Source = "Steam", Details = "oslist=windows" };

        if (os.Contains("linux"))
            return new DepotPlatformResult { DepotId = depotId, Platform = DepotPlatform.Linux, Source = "Steam", Details = "oslist=linux" };

        if (os.Contains("macos") || os.Contains("macosx") || os == "mac")
            return new DepotPlatformResult { DepotId = depotId, Platform = DepotPlatform.MacOS, Source = "Steam", Details = "oslist=macos" };

        return new DepotPlatformResult { DepotId = depotId, Platform = DepotPlatform.Unknown, Source = "Manifest", Details = "oslist не указан" };
    }

    public static DepotPlatform DetectFromFileNames(IEnumerable<string> fileNames)
    {
        bool windows = false;
        bool linux = false;
        bool macos = false;

        foreach (string raw in fileNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string path = raw.Replace('\\', '/').ToLowerInvariant();
            string name = Path.GetFileName(path);

            // Windows
            if (name.EndsWith(".exe") || name.EndsWith(".dll") ||
                name.EndsWith(".bat") || name.EndsWith(".cmd"))
                windows = true;

            // Linux
            if (name.EndsWith(".so") || name.Contains(".so.") ||
                name.EndsWith(".x86_64") || name.EndsWith(".x86"))
                linux = true;

            // macOS
            if (name.EndsWith(".dylib") ||
                path.Contains(".app/contents/macos/") ||
                path.Contains(".app/contents/frameworks/"))
                macos = true;
        }

        DepotPlatform result = DepotPlatform.Unknown;

        if (windows) result |= DepotPlatform.Windows;
        if (linux)   result |= DepotPlatform.Linux;
        if (macos)   result |= DepotPlatform.MacOS;

        return result == DepotPlatform.Unknown
            ? DepotPlatform.Shared
            : result;
    }

    public static bool IsCompatible(DepotPlatform platform, string selectedOs)
    {
        selectedOs = (selectedOs ?? "").Trim().ToLowerInvariant();

        if (selectedOs == "all")
            return platform != DepotPlatform.Unknown;

        if (platform == DepotPlatform.Shared)
            return true;

        if (platform == DepotPlatform.Unknown)
            return false;

        return selectedOs switch
        {
            "windows" => (platform & DepotPlatform.Windows) != 0,
            "linux"   => (platform & DepotPlatform.Linux) != 0,
            "macos"   => (platform & DepotPlatform.MacOS) != 0,
            _ => false
        };
    }

    public static string GetPlatformName(DepotPlatform platform)
    {
        if (platform == DepotPlatform.Unknown)
            return "неизвестно";

        if (platform == DepotPlatform.Shared)
            return "общий";

        var names = new List<string>();
        if ((platform & DepotPlatform.Windows) != 0) names.Add("Windows");
        if ((platform & DepotPlatform.Linux) != 0) names.Add("Linux");
        if ((platform & DepotPlatform.MacOS) != 0) names.Add("macOS");
        return string.Join(", ", names);
    }
}