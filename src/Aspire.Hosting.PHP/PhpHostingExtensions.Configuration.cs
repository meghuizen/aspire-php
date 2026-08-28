#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Installs the application's Composer dependencies before it starts.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="install">
    /// When <see langword="true"/> (the default) the install runs automatically and the application waits for it.
    /// When <see langword="false"/> the resource is still created but has to be started by hand from the dashboard.
    /// </param>
    /// <param name="installArgs">Extra arguments appended to <c>composer install</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Adds a child resource that runs <c>composer install</c> in the application directory. In container run
    /// mode it runs inside the same image as the application, so Composer does not have to be installed locally
    /// either. Publishing installs dependencies inside the image instead and ignores this resource.
    /// </remarks>
    public static IResourceBuilder<T> WithComposer<T>(
        this IResourceBuilder<T> builder,
        bool install = true,
        string[]? installArgs = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] args = ["install", .. installArgs ?? []];

        builder
            .WithAnnotation(new PhpComposerAnnotation("composer"), ResourceAnnotationMutationBehavior.Replace)
            .WithAnnotation(new PhpInstallCommandAnnotation(args), ResourceAnnotationMutationBehavior.Replace);

        // Publishing runs composer inside the image, where the result can be baked into a layer. A separate
        // installer resource would run on the machine doing the publish, which is the wrong machine.
        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return builder;
        }

        var applicationBuilder = builder.ApplicationBuilder;
        var resource = builder.Resource;
        var installerName = $"{resource.Name}-composer";

        // Guard against a second WithComposer call replacing the arguments but adding a duplicate resource.
        if (applicationBuilder.TryCreateResourceBuilder<IResource>(installerName, out _))
        {
            return builder;
        }

        IResourceBuilder<IResource> installerBuilder = resource is PhpContainerAppResource containerResource
            ? CreateContainerInstaller(applicationBuilder, installerName, containerResource, args)
            : CreateExecutableInstaller(applicationBuilder, installerName, resource, args);

        installerBuilder
            .WithParentRelationship(resource)
            .ExcludeFromManifest();

        if (install)
        {
            builder.WaitForCompletion(installerBuilder);
        }
        else
        {
            installerBuilder.WithExplicitStart();
        }

        builder.WithAnnotation(new PhpPackageInstallerAnnotation(installerBuilder.Resource));

        return builder;
    }

    private static IResourceBuilder<IResource> CreateExecutableInstaller(
        IDistributedApplicationBuilder applicationBuilder,
        string installerName,
        IPhpResource resource,
        string[] args)
    {
        var installer = new PhpComposerInstallerResource(installerName, resource.AppDirectory);
        installer.Annotations.Add(NameValidationPolicyAnnotation.None);

        return applicationBuilder.AddResource(installer)
            .WithArgs(args)
            // Composer is a separate download from PHP, so it can be missing even when php is present.
            .WithRequiredCommand("composer", ComposerInstallHelpLink);
    }

    private static IResourceBuilder<IResource> CreateContainerInstaller(
        IDistributedApplicationBuilder applicationBuilder,
        string installerName,
        PhpContainerAppResource resource,
        string[] args)
    {
        var installer = new PhpComposerInstallerContainerResource(installerName);
        installer.Annotations.Add(NameValidationPolicyAnnotation.None);

        // Built from the same run-mode image as the application, so the install sees exactly the extensions
        // the application will run with. Composer refuses to install a package whose ext- requirement is
        // missing, so installing against a different set of extensions would give a misleading result.
        return applicationBuilder.AddResource(installer)
            .WithBindMount(resource.AppDirectory, PhpImages.AppBaseDirectory)
            .WithImage("placeholder")
            .WithDockerfileBuilder(
                CreateEmptyBuildContext(installerName, resource.AppDirectory),
                context => PhpDockerfileGenerator.WriteDevDockerfile(resource, context))
            .WithArgs(["composer", .. args])
            .WithEnvironment("SHOW_WELCOME_MESSAGE", "false");
    }

    /// <summary>
    /// Adds PHP extensions the application needs.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="extensions">Extension names, for example <c>pdo_pgsql</c>, <c>redis</c>, <c>opentelemetry</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Repeated calls accumulate rather than replacing each other, and duplicates are ignored.
    /// <para>
    /// In container mode and when publishing, the extensions are installed into the image. When running against
    /// a local PHP nothing can be installed for you, so the resource instead checks them at start and fails with
    /// a message naming what is missing.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithPhpExtension<T>(
        this IResourceBuilder<T> builder,
        params string[] extensions)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Length == 0)
        {
            return builder;
        }

        if (!builder.Resource.TryGetLastAnnotation<PhpExtensionAnnotation>(out var annotation))
        {
            annotation = new PhpExtensionAnnotation();
            builder.WithAnnotation(annotation);
        }

        annotation.Add(extensions);

        return builder;
    }

    /// <summary>
    /// Turns on PHP-level OpenTelemetry so traces from the application appear in the Aspire dashboard.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Installs the <c>opentelemetry</c> extension and sets <c>OTEL_PHP_AUTOLOAD_ENABLED</c>. Aspire already
    /// supplies the endpoint and service name through the standard <c>OTEL_*</c> variables, which is what the
    /// PHP SDK reads, so nothing else has to be configured.
    /// </para>
    /// <para>
    /// The application still needs the SDK itself:
    /// <c>composer require open-telemetry/sdk open-telemetry/exporter-otlp</c>.
    /// </para>
    /// <para>
    /// Worth knowing: PHP has no background thread, so outside FrankenPHP worker mode every request pays the
    /// export cost inline. For a web application, consider pairing this with <c>WithWorkerMode</c>.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithOpenTelemetry<T>(this IResourceBuilder<T> builder)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithAnnotation(new PhpOpenTelemetryAnnotation(), ResourceAnnotationMutationBehavior.Replace)
            .WithPhpExtension("opentelemetry")
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["OTEL_PHP_AUTOLOAD_ENABLED"] = "true";

                // Named explicitly because the PHP SDK does not fall back to the resource name the way the
                // .NET SDK does, and an unnamed service is very hard to find in the dashboard.
                context.EnvironmentVariables["OTEL_SERVICE_NAME"] = builder.Resource.Name;

                // http/protobuf rather than gRPC: gRPC needs a separate PHP extension, while this transport
                // only needs a PSR-18 client the SDK already pulls in.
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
            });
    }

    /// <summary>
    /// Sets a php.ini value for the application.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="key">The setting name, for example <c>memory_limit</c>.</param>
    /// <param name="value">The value, for example <c>512M</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Applied as <c>-d</c> arguments when running a local PHP, and as a generated ini file inside the image
    /// otherwise. Setting the same key twice keeps the last value.
    /// </remarks>
    public static IResourceBuilder<T> WithPhpIniSetting<T>(
        this IResourceBuilder<T> builder,
        string key,
        string value)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!builder.Resource.TryGetLastAnnotation<PhpIniSettingAnnotation>(out var annotation))
        {
            annotation = new PhpIniSettingAnnotation();
            builder.WithAnnotation(annotation);
        }

        annotation.Settings[key] = value;

        return builder;
    }

    /// <summary>
    /// Enables Xdebug for the application.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="port">The port your editor listens on. Xdebug's default is 9003.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Xdebug connects out to your editor rather than the other way round, so start the listener first. In
    /// VS Code that is a "Listen for Xdebug" launch configuration. The Aspire dashboard's own debug button
    /// cannot drive this: the Aspire VS Code extension only knows a fixed set of languages and PHP is not
    /// among them.
    /// </para>
    /// <para>
    /// Container mode also needs a <c>pathMappings</c> entry in the launch configuration mapping
    /// <c>/var/www/html</c> to the application directory, or breakpoints will never bind.
    /// </para>
    /// <para>
    /// Ignored when publishing. Xdebug in production is a serious performance and information-disclosure
    /// problem, so it is never written into a published image.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithXdebug<T>(this IResourceBuilder<T> builder, int port = 9003)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return builder;
        }

        builder.WithAnnotation(new PhpXdebugAnnotation(port, "debug"), ResourceAnnotationMutationBehavior.Replace);

        var isContainer = builder.Resource is PhpContainerAppResource;
        if (isContainer)
        {
            builder.WithPhpExtension("xdebug");
        }

        return builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables["XDEBUG_MODE"] = "debug";
            context.EnvironmentVariables["XDEBUG_SESSION"] = "1";

            // A container has to reach back out to the host. Docker Desktop and Podman provide
            // host.docker.internal on Windows and macOS; on Linux it only resolves when the container was
            // started with --add-host=host.docker.internal:host-gateway.
            var clientHost = isContainer ? "host.docker.internal" : "127.0.0.1";

            context.EnvironmentVariables["XDEBUG_CONFIG"] = $"client_host={clientHost} client_port={port}";
        });
    }

    /// <summary>
    /// Turns on FrankenPHP worker mode, keeping the PHP process alive between requests.
    /// </summary>
    /// <typeparam name="T">The PHP web resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="workerScript">
    /// The worker script relative to the document root. Defaults to <c>index.php</c>, the front controller.
    /// </param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Worker mode boots the application once and reuses it, which removes per-request startup cost and is the
    /// only way OpenTelemetry's batching exporter works properly in PHP.
    /// </para>
    /// <para>
    /// It changes the rules the application has to follow. Anything held in a global or static outlives the
    /// request that set it, so state has to be reset explicitly. The worker script must be the front controller
    /// itself and must loop over <c>frankenphp_handle_request</c>.
    /// </para>
    /// <para>
    /// Has no effect when running against a local PHP, which uses the built-in development server rather than
    /// FrankenPHP.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithWorkerMode<T>(
        this IResourceBuilder<T> builder,
        string? workerScript = null)
        where T : IPhpWebResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithAnnotation(new PhpWorkerModeAnnotation(workerScript), ResourceAnnotationMutationBehavior.Replace);

        var documentRoot = builder.Resource.DocumentRoot;
        var script = workerScript ?? "index.php";
        var workerPath = $"{PhpImages.AppBaseDirectory}/{ToContainerPath(documentRoot)}/{ToContainerPath(script)}";

        return builder.WithEnvironment(context =>
        {
            // Injected into Caddy's global options block. Newlines are required: the Caddyfile format is
            // line-based and will not parse a single-line block.
            context.EnvironmentVariables["CADDY_GLOBAL_OPTIONS"] =
                $"frankenphp {{\n\tworker {workerPath}\n}}";
        });
    }

    /// <summary>
    /// Pins the PHP version used to choose the container image, and required of a local PHP.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="version">The version in major.minor form, for example <c>8.5</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Only needed when the application does not already declare its version in <c>.php-version</c> or
    /// <c>composer.json</c>, which are read automatically.
    /// </remarks>
    public static IResourceBuilder<T> WithPhpVersion<T>(this IResourceBuilder<T> builder, string version)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!PhpVersionDetector.TryParseMajorMinor(version, out var majorMinor))
        {
            throw new ArgumentException(
                $"'{version}' is not a PHP version. Expected a major.minor version such as '8.5'.",
                nameof(version));
        }

        if (!builder.Resource.TryGetLastAnnotation<PhpEnvironmentAnnotation>(out var annotation))
        {
            annotation = new PhpEnvironmentAnnotation();
            builder.WithAnnotation(annotation);
        }

        annotation.Version = majorMinor;

        return builder;
    }
}

#pragma warning restore ASPIREDOCKERFILEBUILDER001
