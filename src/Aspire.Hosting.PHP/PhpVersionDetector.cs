using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aspire.Hosting.PHP;

/// <summary>
/// Works out which PHP version an application wants, and whether a usable PHP is installed locally.
/// </summary>
internal static partial class PhpVersionDetector
{
    /// <summary>Used when the application says nothing about which version it needs.</summary>
    public const string DefaultPhpVersion = "8.5";

    /// <summary>
    /// Reads the PHP version the application asks for, in major.minor form.
    /// </summary>
    /// <remarks>
    /// Looks at <c>.php-version</c> first because it is the most specific and the most explicit, then falls back
    /// to <c>composer.json</c>. Returns <see langword="null"/> when neither says anything, rather than guessing,
    /// so the caller can decide whether a default is appropriate.
    /// </remarks>
    public static string? DetectVersion(string appDirectory)
    {
        var phpVersionFile = Path.Combine(appDirectory, ".php-version");
        if (File.Exists(phpVersionFile))
        {
            var raw = File.ReadAllText(phpVersionFile).Trim();
            if (TryParseMajorMinor(raw, out var fileVersion))
            {
                return fileVersion;
            }
        }

        var composerFile = Path.Combine(appDirectory, "composer.json");
        if (File.Exists(composerFile))
        {
            return DetectVersionFromComposer(composerFile);
        }

        return null;
    }

    private static string? DetectVersionFromComposer(string composerFile)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(composerFile));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // config.platform.php wins: it is what Composer itself resolves dependencies against, so it is
            // the version the vendor directory was actually built for.
            if (root.TryGetProperty("config", out var config)
                && config.ValueKind == JsonValueKind.Object
                && config.TryGetProperty("platform", out var platform)
                && platform.ValueKind == JsonValueKind.Object
                && platform.TryGetProperty("php", out var platformPhp)
                && platformPhp.ValueKind == JsonValueKind.String
                && TryParseMajorMinor(platformPhp.GetString(), out var platformVersion))
            {
                return platformVersion;
            }

            if (root.TryGetProperty("require", out var require)
                && require.ValueKind == JsonValueKind.Object
                && require.TryGetProperty("php", out var requirePhp)
                && requirePhp.ValueKind == JsonValueKind.String
                && TryParseMajorMinor(requirePhp.GetString(), out var requireVersion))
            {
                return requireVersion;
            }
        }
        catch (JsonException)
        {
            // A composer.json we cannot parse is not worth failing the AppHost over. Fall through to the
            // default; Composer itself reports the syntax error far more clearly than we could.
        }
        catch (IOException)
        {
        }

        return null;
    }

    /// <summary>
    /// Pulls the first major.minor pair out of a version or Composer constraint string.
    /// </summary>
    /// <remarks>
    /// Handles the constraint forms that actually appear in composer.json: caret, greater-or-equal, wildcard,
    /// tilde and plain versions. Taking the first pair means a range such as <c>&gt;=8.4 &lt;8.6</c> resolves to
    /// its lower bound, which is the version the application is guaranteed to work on.
    /// </remarks>
    public static bool TryParseMajorMinor(string? value, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = MajorMinorRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        version = $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        return true;
    }

    /// <summary>
    /// Finds <c>php</c> on the PATH, returning its full path, or <see langword="null"/> when PHP is not installed.
    /// </summary>
    /// <remarks>
    /// Windows needs the executable extensions appended and treats PATH entries as case-insensitive; Linux and
    /// macOS use the bare name and require the file to be marked executable. Both are handled here so callers
    /// never have to branch on the operating system.
    /// </remarks>
    public static string? FindPhpExecutable()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        // PATHEXT is not consulted: PHP ships as php.exe everywhere, and the .bat/.cmd shims are what tools
        // such as Herd and XAMPP install. Anything else would not be launchable by Process.Start regardless.
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "php.exe", "php.cmd", "php.bat" }
            : ["php"];

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                string fullPath;
                try
                {
                    fullPath = Path.Combine(directory.Trim(), candidate);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry should be skipped, not crash the AppHost.
                    break;
                }

                if (File.Exists(fullPath) && IsExecutable(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            // On Unix a file called "php" that is not marked executable is not PHP, it is a stray file, and
            // launching it would fail with a confusing permission error at start time instead of here.
            return File.GetUnixFileMode(path).HasFlag(UnixFileMode.OtherExecute)
                || File.GetUnixFileMode(path).HasFlag(UnixFileMode.GroupExecute)
                || File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks a PHP executable for its own version, in major.minor form.
    /// </summary>
    public static string? GetExecutableVersion(string phpExecutablePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = phpExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // -r rather than -v so the answer needs no parsing beyond the regex below, and so an ini warning
            // printed ahead of the version banner cannot be mistaken for the version itself.
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add("echo PHP_MAJOR_VERSION, \".\", PHP_MINOR_VERSION;");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Read before waiting: a process that fills the pipe buffer blocks forever if nobody drains it.
            var output = process.StandardOutput.ReadToEnd().Trim();

            if (!process.WaitForExit(10_000))
            {
                // Do not leave a hung php process behind holding the AppHost's pipes open.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                }

                return null;
            }

            return TryParseMajorMinor(output, out var version) ? version : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="actual"/> is at least <paramref name="required"/>.
    /// Both are major.minor strings.
    /// </summary>
    public static bool SatisfiesVersion(string actual, string required)
    {
        if (!TryParseParts(actual, out var actualMajor, out var actualMinor)
            || !TryParseParts(required, out var requiredMajor, out var requiredMinor))
        {
            return false;
        }

        return actualMajor != requiredMajor
            ? actualMajor > requiredMajor
            : actualMinor >= requiredMinor;
    }

    private static bool TryParseParts(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        var parts = version.Split('.');
        return parts.Length >= 2
            && int.TryParse(parts[0], out major)
            && int.TryParse(parts[1], out minor);
    }

    [GeneratedRegex(@"(\d+)\.(\d+)")]
    private static partial Regex MajorMinorRegex();
}
