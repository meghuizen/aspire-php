using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>Carries the PHP version and entry point resolved for a resource.</summary>
internal sealed class PhpEnvironmentAnnotation : IResourceAnnotation
{
    /// <summary>The PHP version in major.minor form, for example "8.5".</summary>
    public string? Version { get; set; }

    /// <summary>The resolved path to the local <c>php</c> executable, when running as an executable.</summary>
    public string? PhpExecutablePath { get; set; }

    /// <summary>The script the application runs, relative to the app directory. Null for web applications.</summary>
    public string? ScriptPath { get; set; }
}

/// <summary>Marks a resource as using Composer, and records the executable that runs it.</summary>
internal sealed class PhpComposerAnnotation(string executableName) : IResourceAnnotation
{
    public string ExecutableName { get; } = executableName;
}

/// <summary>The arguments passed to Composer, including the command itself (for example "install").</summary>
internal sealed class PhpInstallCommandAnnotation(string[] args) : IResourceAnnotation
{
    public string[] Args { get; } = args;
}

/// <summary>Points at the child resource that installs Composer dependencies.</summary>
internal sealed class PhpPackageInstallerAnnotation(IResource installerResource) : IResourceAnnotation
{
    public IResource Resource { get; } = installerResource;
}

/// <summary>
/// PHP extensions the application needs. Accumulates across calls, so several
/// <c>WithPhpExtension</c> calls add up rather than replacing each other.
/// </summary>
internal sealed class PhpExtensionAnnotation : IResourceAnnotation
{
    private readonly List<string> _extensions = [];

    public IReadOnlyList<string> Extensions => _extensions;

    public void Add(IEnumerable<string> extensions)
    {
        foreach (var extension in extensions)
        {
            // Keep the caller's order but never install the same extension twice.
            if (!_extensions.Contains(extension, StringComparer.Ordinal))
            {
                _extensions.Add(extension);
            }
        }
    }
}

/// <summary>php.ini settings applied to the application.</summary>
internal sealed class PhpIniSettingAnnotation : IResourceAnnotation
{
    // Ordered so the generated ini file and the -d arguments come out in a stable order, which keeps
    // generated Dockerfiles byte-identical between runs and therefore keeps Docker layer caching working.
    public SortedDictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);
}

/// <summary>Xdebug configuration for the application.</summary>
internal sealed class PhpXdebugAnnotation(int port, string mode) : IResourceAnnotation
{
    public int Port { get; } = port;

    public string Mode { get; } = mode;
}

/// <summary>Enables FrankenPHP worker mode, keeping the PHP process alive between requests.</summary>
internal sealed class PhpWorkerModeAnnotation(string? workerScript) : IResourceAnnotation
{
    /// <summary>The worker script relative to the document root. Defaults to the front controller.</summary>
    public string? WorkerScript { get; } = workerScript;
}

/// <summary>Marks that the application opted into PHP-level OpenTelemetry instrumentation.</summary>
internal sealed class PhpOpenTelemetryAnnotation : IResourceAnnotation;
