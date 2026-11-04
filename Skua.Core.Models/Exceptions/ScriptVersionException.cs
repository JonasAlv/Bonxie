namespace Skua.Core.Models;

public class ScriptVersionException : ScriptCompileException
{
    public string RequiredVersion { get; }
    public string CurrentVersion { get; }
    public string UpdateUrl { get; }

    public ScriptVersionException(string requiredVersion, string currentVersion, string updateUrl = "https://github.com/JonasAlv/Bonxie/releases")
        : base($"This script requires Bonxie {requiredVersion} or higher.\nYour current version is {currentVersion}.\nPlease update Bonxie to run this script.", string.Empty)
    {
        RequiredVersion = requiredVersion;
        CurrentVersion = currentVersion;
        UpdateUrl = updateUrl;
    }
}
