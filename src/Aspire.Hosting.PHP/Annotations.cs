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

    /// <summary>
    /// Drops an extension that was added by default.
    /// </summary>
    /// <remarks>
    /// Only used to undo the defaults. Removing an extension the application asked for would silently break it.
    /// </remarks>
    public void Remove(string extension) => _extensions.Remove(extension);
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

/// <summary>Records which naming convention a PHP application's references translate into.</summary>
internal sealed class PhpConnectionConventionAnnotation(PhpConnectionConvention convention) : IResourceAnnotation
{
    public PhpConnectionConvention Convention { get; } = convention;
}

/// <summary>Carries the performance settings applied by <c>WithPhpOptimizations</c>.</summary>
internal sealed class PhpOptimizationAnnotation(PhpOptimizationOptions options) : IResourceAnnotation
{
    public PhpOptimizationOptions Options { get; } = options;
}

/// <summary>Records which web server serves a PHP web application.</summary>
internal sealed class PhpWebServerAnnotation(PhpWebServer webServer) : IResourceAnnotation
{
    public PhpWebServer WebServer { get; } = webServer;
}

/// <summary>Marks a collector as forwarding to Application Insights.</summary>
internal sealed class PhpApplicationInsightsAnnotation : IResourceAnnotation;

/// <summary>
/// Records how a console command is meant to run, so a deployment target can shape it correctly.
/// </summary>
/// <remarks>
/// The kind is not derivable from the resource itself — a one-shot migration and a long-running queue worker
/// are the same shape in the app model and differ only in intent. Deployment targets need that intent: on
/// Azure Container Apps a one-shot command is a Job and a queue worker is an app that must not scale to zero.
/// </remarks>
internal sealed class PhpConsoleKindAnnotation(PhpConsoleCommandKind kind, string? cronExpression = null)
    : IResourceAnnotation
{
    public PhpConsoleCommandKind Kind { get; } = kind;

    /// <summary>The cron expression, set only when <see cref="Kind"/> is <see cref="PhpConsoleCommandKind.Scheduled"/>.</summary>
    public string? CronExpression { get; } = cronExpression;
}

/// <summary>
/// Marks a PHP web resource as sitting behind a TLS-terminating reverse proxy.
/// </summary>
/// <remarks>
/// Carries the trusted proxy list so the Dockerfile generator knows whether to write the <c>$_SERVER</c>
/// shim, which is needed only by the applications that read those keys directly rather than an environment
/// variable. An empty list means the caller opted out.
/// </remarks>
internal sealed class PhpTrustedProxyAnnotation(string proxies) : IResourceAnnotation
{
    public string Proxies { get; } = proxies;

    public bool IsOptedOut => Proxies.Length == 0;
}

/// <summary>Records that a collector is meant to run alongside a specific application, not on its own.</summary>
/// <remarks>
/// PHP has no background thread, so an exporter has no "later" in which to flush and every request pays the
/// export cost inline. That is only avoided when the collector is reachable over localhost, which on a
/// deployment target means being a second container in the same unit rather than a separate one.
/// </remarks>
internal sealed class PhpCollectorColocationAnnotation(string applicationResourceName) : IResourceAnnotation
{
    public string ApplicationResourceName { get; } = applicationResourceName;
}

/// <summary>
/// Operating system packages the image needs, beyond PHP extensions.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="PhpExtensionAnnotation"/> because these are installed by the distribution's
/// package manager, not by the PHP extension installer.
/// </remarks>
internal sealed class PhpSystemPackageAnnotation : IResourceAnnotation
{
    private readonly List<string> _packages = [];

    public IReadOnlyList<string> Packages => _packages;

    public void Add(string package)
    {
        if (!_packages.Contains(package, StringComparer.Ordinal))
        {
            _packages.Add(package);
        }
    }
}
