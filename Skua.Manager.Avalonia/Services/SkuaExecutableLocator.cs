using System;
using System.IO;

namespace Skua.Manager.Avalonia.Services;

internal static class SkuaExecutableLocator
{
    public static string ExecutablePath { get; } = Resolve();

    public static string WorkingDirectory =>
        Path.GetDirectoryName(ExecutablePath) ?? AppContext.BaseDirectory;

    private static string Resolve()
    {
        string? appImageEnv = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImageEnv))
        {
            string? hostDir = Path.GetDirectoryName(appImageEnv);
            if (!string.IsNullOrEmpty(hostDir))
            {
                string[] appImageCandidates = [ "Bonxie-x86_64.AppImage", "Bonxie.AppImage", "Bonxie" ];
                foreach (string name in appImageCandidates)
                {
                    string p = Path.Combine(hostDir, name);
                    if (File.Exists(p) && !string.Equals(p, appImageEnv, StringComparison.Ordinal))
                        return p;
                }
            }
        }

        string[] directCandidates = [ "Bonxie-x86_64.AppImage", "Bonxie.AppImage", "Bonxie", "Skua" ];

        foreach (string name in directCandidates)
        {
            string p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p))
                return p;
        }

        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] searchDirs =
        [
            Path.Combine(userHome, "Games", "Bonxie-linux-AppImage"),
            Path.Combine(userHome, "Applications"),
            Path.Combine(userHome, "Desktop"),
            Path.Combine(userHome, "Downloads"),
            Path.Combine(userHome, ".local", "bin"),
            Path.Combine(userHome, ".local", "share", "Bonxie"),
            "/opt/Bonxie"
        ];

        foreach (string dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string name in directCandidates)
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p))
                    return p;
            }
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in directCandidates)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(repositoryRoot, "Build", "Bonxie-x86_64.AppImage"),
            Path.Combine(repositoryRoot, "Build", "Bonxie"),
            Path.Combine(repositoryRoot, "Build", "Skua"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Release", "net10.0", "Bonxie"),
            Path.Combine(repositoryRoot, "Skua.App.Avalonia", "bin", "Debug", "net10.0", "Bonxie")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "Bonxie");
    }
}
