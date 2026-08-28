#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// Everything the Dockerfile generator needs, gathered from a resource's annotations in one place.
/// </summary>
internal sealed record PhpDockerfileOptions
{
    public required string Image { get; init; }

    public required bool IsWeb { get; init; }

    public required bool UsesComposer { get; init; }

    public required string? ScriptPath { get; init; }

    public required IReadOnlyList<string> Extensions { get; init; }

    public required IReadOnlyDictionary<string, string> IniSettings { get; init; }

    /// <summary>Operating system packages to install, beyond PHP extensions.</summary>
    public required IReadOnlyList<string> SystemPackages { get; init; }

    /// <summary>Rebuild the Redis extension so it can serialize with igbinary.</summary>
    public required bool RebuildRedisForIgbinary { get; init; }

    /// <summary>Build a Composer autoloader with no filesystem fallback.</summary>
    public required bool ClassmapAuthoritative { get; init; }

    /// <summary>
    /// Write the <c>$_SERVER</c> shim that populates HTTPS and host from the forwarded headers.
    /// </summary>
    /// <remarks>
    /// Only for the applications that read those keys directly instead of an environment variable, which is
    /// WordPress, Joomla and Drupal. Laravel and Symfony are configured through the environment instead.
    /// </remarks>
    public required bool WriteForwardedHeaderShim { get; init; }

    /// <summary>
    /// Reads the options off a PHP resource.
    /// </summary>
    /// <param name="resource">The PHP resource carrying the annotations.</param>
    /// <param name="dockerfileResource">
    /// The resource the Dockerfile is being generated for. When publishing this is the container resource that
    /// <c>PublishAsDockerFile</c> substitutes in, which is where a late <c>WithDockerfileBaseImage</c> call
    /// lands; in run mode it is the PHP resource itself.
    /// </param>
    public static PhpDockerfileOptions Resolve(IPhpResource resource, IResource dockerfileResource)
    {
        var isWeb = resource is IPhpWebResource;

        // Checked on the substituted container first, then the PHP resource, so an override applied inside a
        // PublishAsDockerFile callback wins over one applied to the PHP resource builder.
        var baseImages = dockerfileResource.Annotations.OfType<DockerfileBaseImageAnnotation>().LastOrDefault()
            ?? resource.Annotations.OfType<DockerfileBaseImageAnnotation>().LastOrDefault();

        var image = ResolveImage(baseImages, resource, isWeb);

        resource.TryGetLastAnnotation<PhpEnvironmentAnnotation>(out var environment);
        resource.TryGetLastAnnotation<PhpExtensionAnnotation>(out var extensions);
        resource.TryGetLastAnnotation<PhpIniSettingAnnotation>(out var iniSettings);
        resource.TryGetLastAnnotation<PhpOptimizationAnnotation>(out var optimization);
        resource.TryGetLastAnnotation<PhpSystemPackageAnnotation>(out var systemPackages);

        return new PhpDockerfileOptions
        {
            Image = image,
            IsWeb = isWeb,
            UsesComposer = resource.TryGetLastAnnotation<PhpComposerAnnotation>(out _)
                || File.Exists(Path.Combine(resource.AppDirectory, "composer.json")),
            ScriptPath = NormalizeScriptPath(environment?.ScriptPath),
            Extensions = extensions?.Extensions ?? [],
            SystemPackages = systemPackages?.Packages ?? [],
            IniSettings = iniSettings?.Settings ?? new SortedDictionary<string, string>(StringComparer.Ordinal),
            RebuildRedisForIgbinary = optimization?.Options.IgbinaryForRedis ?? false,
            ClassmapAuthoritative = optimization?.Options.ComposerClassmapAuthoritative ?? false,
            // Skipped when the application already has an auto_prepend_file: PHP allows exactly one, so ours
            // would either be ignored or replace theirs. Writing the file anyway would leave the image
            // carrying something nothing references.
            WriteForwardedHeaderShim = isWeb
                && NeedsForwardedHeaderShim(resource)
                && !(iniSettings?.Settings.ContainsKey("auto_prepend_file") ?? false)
        };
    }

    /// <summary>
    /// Whether this application reads <c>$_SERVER</c> directly rather than a trusted-proxy environment
    /// variable, and so needs the shim written into the image.
    /// </summary>
    /// <remarks>
    /// An explicit opt-out suppresses it. Nothing is written when the caller never asked for proxy awareness
    /// either, which is the run-mode case: there is no proxy in front of a local process.
    /// </remarks>
    private static bool NeedsForwardedHeaderShim(IPhpResource resource)
    {
        if (resource.TryGetLastAnnotation<PhpTrustedProxyAnnotation>(out var proxies) && proxies.IsOptedOut)
        {
            return false;
        }

        var convention = resource.TryGetLastAnnotation<PhpConnectionConventionAnnotation>(out var annotation)
            ? annotation.Convention
            : PhpConnectionConvention.Generic;

        return PhpHostingExtensions.NeedsForwardedHeaderShim(convention);
    }

    private static string ResolveImage(DockerfileBaseImageAnnotation? baseImages, IPhpResource resource, bool isWeb)
    {
        var buildImage = baseImages?.BuildImage;
        var runtimeImage = baseImages?.RuntimeImage;

        // Generated PHP images are single stage, so there is no build image to be different from the runtime
        // one. Saying so is better than silently picking one and producing an image the caller did not ask for.
        if (buildImage is not null && runtimeImage is not null
            && !string.Equals(buildImage, runtimeImage, StringComparison.Ordinal))
        {
            throw new DistributedApplicationException(
                $"The PHP app '{resource.Name}' set different build and runtime images with WithDockerfileBaseImage, " +
                "but generated PHP Dockerfiles are single stage because there is nothing to compile away. " +
                "Set runtimeImage only, or supply your own Dockerfile in the app directory.");
        }

        if ((runtimeImage ?? buildImage) is { } explicitImage)
        {
            return explicitImage;
        }

        var (defaultImage, defaultTemplate) = !isWeb
            ? (PhpImages.DefaultCliImage, PhpImages.CliImageTemplate)
            : PhpWebServers.Resolve(resource) switch
            {
                PhpWebServer.Apache => (PhpImages.DefaultApacheImage, PhpImages.ApacheImageTemplate),
                PhpWebServer.FpmNginx => (PhpImages.DefaultFpmNginxImage, PhpImages.FpmNginxImageTemplate),
                _ => (PhpImages.DefaultWebImage, PhpImages.WebImageTemplate)
            };

        var version = resource.TryGetLastAnnotation<PhpEnvironmentAnnotation>(out var environment)
            ? environment.Version
            : null;

        return version is null
            ? defaultImage
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, defaultTemplate, version);
    }

    /// <summary>
    /// Rewrites a host-shaped relative path into the forward-slash form a Linux container needs.
    /// </summary>
    /// <remarks>
    /// An AppHost on Windows may well express a script as <c>bin\worker.php</c>. A backslash is a legal
    /// filename character on Linux, so this cannot be done blindly at runtime; it is only correct here,
    /// where the destination is known to be a Linux container.
    /// </remarks>
    private static string? NormalizeScriptPath(string? scriptPath)
        => scriptPath is null || !OperatingSystem.IsWindows()
            ? scriptPath
            : scriptPath.Replace('\\', '/');
}

#pragma warning restore ASPIREDOCKERFILEBUILDER001
