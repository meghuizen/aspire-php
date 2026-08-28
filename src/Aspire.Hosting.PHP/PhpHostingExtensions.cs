#pragma warning disable ASPIREDOCKERFILEBUILDER001

using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

/// <summary>
/// Adds PHP applications to an <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
public static partial class PhpHostingExtensions
{
    /// <summary>The document root used when none is given. Matches Laravel, Symfony and most modern PHP layouts.</summary>
    public const string DefaultDocumentRoot = "public";

    private const string ComposerInstallHelpLink = "https://getcomposer.org/download/";
    private const string PhpInstallHelpLink = "https://www.php.net/downloads";

    /// <summary>
    /// Adds a PHP worker or CLI application that runs a single script.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The directory holding the application. Also the Docker build context when publishing.</param>
    /// <param name="scriptPath">The script to run, relative to <paramref name="appDirectory"/>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>. Defaults to <see cref="PhpRunMode.Auto"/>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Runs <c>php &lt;scriptPath&gt;</c>. Use <c>WithArgs</c> to pass arguments to the script.
    /// <para>
    /// PHP does not have to be installed. Under <see cref="PhpRunMode.Auto"/> a local <c>php</c> is used when one
    /// is on the PATH; otherwise the application runs in a container with <paramref name="appDirectory"/>
    /// bind-mounted, so edits still take effect immediately.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// builder.AddPhpApp("worker", "../php-worker", "worker.php")
    ///        .WithComposer();
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    public static IResourceBuilder<IPhpResource> AddPhpApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string scriptPath,
        PhpRunMode runMode = PhpRunMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var resolvedDirectory = Path.GetFullPath(appDirectory, builder.AppHostDirectory);
        var version = PhpVersionDetector.DetectVersion(resolvedDirectory) ?? PhpVersionDetector.DefaultPhpVersion;
        var resolution = ResolveRunMode(builder, name, runMode, version);

        return resolution.UseContainer
            ? ConfigureContainerApp(
                builder,
                new PhpContainerAppResource(name, resolvedDirectory),
                scriptPath,
                version,
                documentRoot: null)
            : ConfigureExecutableApp(
                builder,
                new PhpAppResource(name, resolution.PhpExecutablePath!, resolvedDirectory),
                scriptPath,
                version,
                resolution.PhpExecutablePath!,
                documentRoot: null);
    }

    /// <summary>
    /// Adds a PHP web application served over HTTP.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The directory holding the application. Also the Docker build context when publishing.</param>
    /// <param name="documentRoot">The document root relative to <paramref name="appDirectory"/>. Defaults to <c>public</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>. Defaults to <see cref="PhpRunMode.Auto"/>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Publishing produces a FrankenPHP container. FrankenPHP is Caddy with PHP compiled in, so the application
    /// is a single long-running process that binds a port, which is what lets it map onto one Aspire HTTP
    /// endpoint. Classic PHP-FPM cannot: it speaks FastCGI and needs a separate web server in front of it.
    /// </para>
    /// <para>
    /// Running with a local PHP uses PHP's built-in development server instead, which is single-threaded and
    /// unsuitable for production. What you run locally and what you deploy therefore differ here by design.
    /// Container run mode uses FrankenPHP and so matches production.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// builder.AddPhpWebApp("shop", "../shop")
    ///        .WithComposer()
    ///        .WithOpenTelemetry()
    ///        .WithExternalHttpEndpoints();
    ///
    /// builder.Build().Run();
    /// </code>
    /// </example>
    public static IResourceBuilder<IPhpWebResource> AddPhpWebApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string documentRoot = DefaultDocumentRoot,
        PhpRunMode runMode = PhpRunMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentRoot);

        var resolvedDirectory = Path.GetFullPath(appDirectory, builder.AppHostDirectory);
        var version = PhpVersionDetector.DetectVersion(resolvedDirectory) ?? PhpVersionDetector.DefaultPhpVersion;
        var resolution = ResolveRunMode(builder, name, runMode, version);

        return resolution.UseContainer
            ? ConfigureContainerApp(
                builder,
                new PhpWebContainerAppResource(name, resolvedDirectory, documentRoot),
                scriptPath: null,
                version,
                documentRoot)
            : ConfigureExecutableApp(
                builder,
                new PhpWebAppResource(name, resolution.PhpExecutablePath!, resolvedDirectory, documentRoot),
                scriptPath: null,
                version,
                resolution.PhpExecutablePath!,
                documentRoot);
    }

    // Decides between a local php process and a container. This has to happen here, before the resource is
    // created, because the two are different resource types: an ExecutableResource and a ContainerResource.
    private static RunModeResolution ResolveRunMode(
        IDistributedApplicationBuilder builder,
        string resourceName,
        PhpRunMode runMode,
        string requiredVersion)
    {
        // Publishing never runs anything locally, and PublishAsDockerFile only works on an executable resource,
        // so publish mode always takes the executable shape regardless of what is installed on this machine.
        if (builder.ExecutionContext.IsPublishMode)
        {
            return new RunModeResolution(UseContainer: false, PhpExecutablePath: "php");
        }

        if (runMode == PhpRunMode.Container)
        {
            return new RunModeResolution(UseContainer: true, PhpExecutablePath: null);
        }

        var phpPath = PhpVersionDetector.FindPhpExecutable();
        var installedVersion = phpPath is null ? null : PhpVersionDetector.GetExecutableVersion(phpPath);
        var satisfies = installedVersion is not null
            && PhpVersionDetector.SatisfiesVersion(installedVersion, requiredVersion);

        if (runMode == PhpRunMode.Executable)
        {
            // An explicit request for the local interpreter must fail loudly rather than quietly using a
            // container, because the caller asked for this shape on purpose.
            if (phpPath is null)
            {
                throw new DistributedApplicationException(
                    $"The PHP app '{resourceName}' is set to PhpRunMode.Executable but no 'php' was found on the PATH. " +
                    $"Install PHP {requiredVersion} ({PhpInstallHelpLink}), or use PhpRunMode.Auto to run it in a container instead.");
            }

            if (!satisfies)
            {
                throw new DistributedApplicationException(
                    $"The PHP app '{resourceName}' needs PHP {requiredVersion} but '{phpPath}' is version " +
                    $"{installedVersion ?? "unknown"}. Install a matching PHP, or use PhpRunMode.Auto to run it in a container instead.");
            }

            return new RunModeResolution(UseContainer: false, PhpExecutablePath: phpPath);
        }

        return satisfies
            ? new RunModeResolution(UseContainer: false, PhpExecutablePath: phpPath)
            : new RunModeResolution(UseContainer: true, PhpExecutablePath: null);
    }

    // Generic on the concrete resource type so the returned builder keeps it. A helper returning
    // IResourceBuilder<IPhpResource> would lose the web type, and IResourceBuilder<T> is covariant, so it
    // could not be widened back to IResourceBuilder<IPhpWebResource> afterwards.
    private static IResourceBuilder<TResource> ConfigureExecutableApp<TResource>(
        IDistributedApplicationBuilder builder,
        TResource resource,
        string? scriptPath,
        string version,
        string phpExecutablePath,
        string? documentRoot)
        where TResource : PhpAppResource
    {
        var isWeb = documentRoot is not null;
        var appDirectory = resource.AppDirectory;

        var resourceBuilder = builder.AddResource(resource)
            .WithAnnotation(new PhpEnvironmentAnnotation
            {
                Version = version,
                PhpExecutablePath = phpExecutablePath,
                ScriptPath = scriptPath
            })
            .WithRequiredCommand("php", PhpInstallHelpLink);

        if (isWeb)
        {
            if (builder.ExecutionContext.IsPublishMode)
            {
                // Publishing turns this into a container, and a container endpoint must name the port inside
                // the container. Aspire maps a host port onto it.
                resourceBuilder.WithHttpEndpoint(targetPort: PhpImages.DefaultWebContainerPort, env: "PORT");
            }
            else
            {
                // Running locally the target port is a real port on this machine, so let Aspire allocate a
                // free one. Pinning it would make two PHP apps collide.
                resourceBuilder.WithHttpEndpoint(env: "PORT");
            }

            var endpoint = resource.GetEndpoint("http");

            if (builder.ExecutionContext.IsPublishMode)
            {
                // The published container is served by a real web server, which has to be told the document
                // root. Without this it falls back to its own default, which only happens to be right when the
                // document root was left at the default too.
                resourceBuilder.WithEnvironment(context =>
                    ConfigureWebServerEnvironment(resource, context.EnvironmentVariables, endpoint, documentRoot!));
            }

            resourceBuilder.WithArgs(context =>
            {
                AddIniArguments(resource, context.Args);

                // PHP's built-in server. Development only: it is single-threaded and serves one request at a
                // time. Publishing uses FrankenPHP instead.
                context.Args.Add("-S");
                context.Args.Add(ReferenceExpression.Create(
                    $"0.0.0.0:{endpoint.Property(EndpointProperty.TargetPort)}"));
                context.Args.Add("-t");
                context.Args.Add(documentRoot!);
            });
        }
        else
        {
            resourceBuilder.WithArgs(context =>
            {
                AddIniArguments(resource, context.Args);
                context.Args.Add(scriptPath!);
            });
        }

        ConfigureCommon(builder, resourceBuilder, appDirectory, isWeb, usesContainer: false);
        ConfigurePublish(builder, resourceBuilder, resource, appDirectory);

        return resourceBuilder;
    }

    private static IResourceBuilder<TResource> ConfigureContainerApp<TResource>(
        IDistributedApplicationBuilder builder,
        TResource resource,
        string? scriptPath,
        string version,
        string? documentRoot)
        where TResource : PhpContainerAppResource
    {
        var isWeb = documentRoot is not null;
        var appDirectory = resource.AppDirectory;
        var name = resource.Name;

        var resourceBuilder = builder.AddResource(resource)
            .WithAnnotation(new PhpEnvironmentAnnotation
            {
                Version = version,
                PhpExecutablePath = null,
                ScriptPath = scriptPath
            })
            // The application is mounted rather than copied so edits are live and vendor/ is written back to
            // the host, where editors and static analysis can see it.
            .WithBindMount(appDirectory, PhpImages.AppBaseDirectory)
            // WithDockerfileBuilder replaces this, but it needs an image annotation to already be present.
            // The same placeholder AddDockerfile uses, for the same reason.
            .WithImage("placeholder")
            // An empty context: the run-mode image installs extensions and ini settings but copies nothing,
            // so pointing the build at the application directory would upload it to the daemon for no reason.
            .WithDockerfileBuilder(
                CreateEmptyBuildContext(name, appDirectory),
                context => PhpDockerfileGenerator.WriteDevDockerfile(resource, context));

        if (isWeb)
        {
            // A container endpoint must name the port inside the container; Aspire maps a host port onto it.
            // Fixed rather than allocated, because the container has its own network namespace and so cannot
            // collide with another PHP app.
            resourceBuilder.WithHttpEndpoint(targetPort: PhpImages.DefaultWebContainerPort, env: "PORT");
            var endpoint = resource.GetEndpoint("http");

            // No entrypoint or args: the image's own entrypoint starts the web server. It reads both of these.
            resourceBuilder.WithEnvironment(context =>
                ConfigureWebServerEnvironment(resource, context.EnvironmentVariables, endpoint, documentRoot!));
        }
        else
        {
            // Passed as the container command rather than as an ENTRYPOINT override, so the image's own
            // entrypoint still runs and applies the PHP_* environment settings before handing over.
            resourceBuilder.WithArgs("php", ToContainerPath(scriptPath!));
        }

        ConfigureCommon(builder, resourceBuilder, appDirectory, isWeb, usesContainer: true);

        return resourceBuilder;
    }

    /// <summary>
    /// Tells the image's web server which port to listen on and where the document root is.
    /// </summary>
    /// <remarks>
    /// The two servers read different variables, and neither reads the other's, so getting this wrong shows up
    /// as a container that starts and then serves nothing.
    /// </remarks>
    private static void ConfigureWebServerEnvironment(
        IPhpResource resource,
        IDictionary<string, object> environment,
        EndpointReference endpoint,
        string documentRoot)
    {
        var containerDocumentRoot = $"{PhpImages.AppBaseDirectory}/{ToContainerPath(documentRoot)}";
        var isApache = resource.TryGetLastAnnotation<PhpWebServerAnnotation>(out var webServer)
            && webServer.WebServer == PhpWebServer.Apache;

        if (isApache)
        {
            environment["APACHE_HTTP_PORT"] = endpoint.Property(EndpointProperty.TargetPort);
            environment["APACHE_DOCUMENT_ROOT"] = containerDocumentRoot;
        }
        else
        {
            environment["CADDY_HTTP_PORT"] = endpoint.Property(EndpointProperty.TargetPort);
            environment["CADDY_SERVER_ROOT"] = containerDocumentRoot;
        }
    }

    private static void ConfigureCommon<T>(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<T> resourceBuilder,
        string appDirectory,
        bool isWeb,
        bool usesContainer)
        where T : IPhpResource
    {
        resourceBuilder
            .WithIconName("Code")
            .WithOtlpExporter()
            .WithEnvironment(context =>
            {
                // The serversideup images print a large ASCII banner on every start, which would otherwise
                // flood the dashboard log pane on each restart.
                context.EnvironmentVariables["SHOW_WELCOME_MESSAGE"] = "false";

                // OPcache caches compiled bytecode. Off while developing so edits take effect immediately;
                // on when publishing, where it is the single largest easy performance win.
                context.EnvironmentVariables["PHP_OPCACHE_ENABLE"] =
                    context.ExecutionContext.IsPublishMode ? "1" : "0";

                if (isWeb)
                {
                    // Aspire terminates TLS at its own proxy, and the image's self-signed certificate would
                    // only get in the way.
                    context.EnvironmentVariables["SSL_MODE"] = "off";

                    // The images can run Laravel migrations and cache warming on start. That is a deployment
                    // decision, so it stays off unless the application asks for it explicitly.
                    context.EnvironmentVariables["AUTORUN_ENABLED"] = "false";
                }
            });

        if (builder.ExecutionContext.IsRunMode)
        {
            var resource = resourceBuilder.Resource;
            var mode = usesContainer ? "container" : "local php";

            builder.OnBeforeStart((evt, cancellationToken) =>
            {
                // Which shape was chosen changes how the application behaves, so it is stated rather than
                // left for the developer to infer from the dashboard.
                var logger = evt.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
                logger.LogInformation("PHP app '{Name}' is running in {Mode} mode.", resource.Name, mode);

                WarnOnVersionDrift(resource, usesContainer, logger);

                return Task.CompletedTask;
            });
        }
    }

    /// <summary>
    /// Warns when the local PHP is not the version the published image will use.
    /// </summary>
    /// <remarks>
    /// The run mode is decided while the resource is being created, but <c>WithPhpVersion</c> is applied after
    /// it returns. So an application that pins 8.4 on a machine with 8.5 installed would develop against 8.5
    /// and deploy 8.4 with nothing to indicate it. Checked here, at the last point before start, so the final
    /// pinned version is the one compared.
    /// </remarks>
    private static void WarnOnVersionDrift(IPhpResource resource, bool usesContainer, ILogger logger)
    {
        // Container mode builds from the pinned version, so the two cannot drift apart.
        if (usesContainer
            || !resource.TryGetLastAnnotation<PhpEnvironmentAnnotation>(out var environment)
            || environment.Version is not { } pinnedVersion
            || environment.PhpExecutablePath is not { } phpExecutablePath)
        {
            return;
        }

        var installedVersion = PhpVersionDetector.GetExecutableVersion(phpExecutablePath);
        if (installedVersion is null || string.Equals(installedVersion, pinnedVersion, StringComparison.Ordinal))
        {
            return;
        }

        // Each named placeholder maps to one argument, so the target version is not repeated in the template.
        logger.LogWarning(
            "PHP app '{Name}' targets PHP {PinnedVersion} and will publish a container on that version, but " +
            "'{PhpExecutablePath}' is PHP {InstalledVersion}, so you are developing against a different one. " +
            "Install the targeted version, or pass PhpRunMode.Container to develop on the version you deploy.",
            resource.Name,
            pinnedVersion,
            phpExecutablePath,
            installedVersion);
    }

    private static void ConfigurePublish(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<PhpAppResource> resourceBuilder,
        PhpAppResource resource,
        string appDirectory)
    {
        resourceBuilder.PublishAsDockerFile(container =>
        {
            // An application that ships its own Dockerfile has made a deliberate choice; do not overwrite it.
            if (File.Exists(Path.Combine(appDirectory, "Dockerfile")))
            {
                return;
            }

            container.WithDockerfileBuilder(
                appDirectory,
                context => PhpDockerfileGenerator.WritePublishDockerfile(resource, context));

            // A <dockerfile>.dockerignore replaces the context root's rather than merging with it, so an
            // authored .dockerignore wins outright and ours is only supplied when there is none.
            if (!File.Exists(Path.Combine(appDirectory, ".dockerignore"))
                && container.Resource.TryGetLastAnnotation<DockerfileBuildAnnotation>(out var dockerfile))
            {
                dockerfile.BuildContextIgnoreContent ??= PhpDockerfileGenerator.DefaultBuildContextIgnoreContent;
            }
        });
    }

    // The run-mode image copies nothing, so any empty directory works as its build context. One is created per
    // resource under the temp directory, keyed by the application path so two AppHosts cannot collide.
    private static string CreateEmptyBuildContext(string resourceName, string appDirectory)
    {
        var key = Math.Abs(
            StringComparer.Ordinal.GetHashCode($"{appDirectory}{resourceName}"))
            .ToString(CultureInfo.InvariantCulture);

        var contextPath = Path.Combine(Path.GetTempPath(), "aspire-php-context", $"{SanitizeForPath(resourceName)}-{key}");
        Directory.CreateDirectory(contextPath);
        return contextPath;
    }

    private static string SanitizeForPath(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    // Host paths may use backslashes on Windows. A backslash is a legal filename character on Linux, so this
    // conversion is only safe where the destination is known to be a Linux container.
    private static string ToContainerPath(string relativePath)
        => OperatingSystem.IsWindows() ? relativePath.Replace('\\', '/') : relativePath;

    private static void AddIniArguments(IPhpResource resource, IList<object> args)
    {
        if (!resource.TryGetLastAnnotation<PhpIniSettingAnnotation>(out var iniSettings))
        {
            return;
        }

        // Read inside the argument callback rather than when the setting is added, so WithPhpIniSetting calls
        // made after AddPhpApp are still picked up.
        foreach (var setting in iniSettings.Settings)
        {
            args.Add("-d");
            args.Add($"{setting.Key}={setting.Value}");
        }
    }

    private readonly record struct RunModeResolution(bool UseContainer, string? PhpExecutablePath);
}

#pragma warning restore ASPIREDOCKERFILEBUILDER001
