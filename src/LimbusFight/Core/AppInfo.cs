using System.Reflection;

namespace LimbusFight.Core;

/// <summary>Build identity shown in the window title, HUD and crash logs.</summary>
public static class AppInfo
{
    public const string Name = "Limbus Fight";

    /// <summary>"0.1.42+3f9c2ab" on CI builds, "0.1.0" on local builds.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        string? informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // Shorten "+<40-char sha>" to "+<7-char sha>".
        int plus = informational.IndexOf('+');
        if (plus >= 0 && informational.Length - plus - 1 > 7)
        {
            informational = informational[..(plus + 8)];
        }

        return informational;
    }
}
