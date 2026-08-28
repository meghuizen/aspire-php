using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Applies the performance settings that matter for PHP: OPcache, igbinary, APCu and the realpath cache.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="configure">Adjusts the defaults.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// The defaults differ between running and publishing, because the two want opposite things. Publishing
    /// turns OPcache on and stops it checking the filesystem, since the source in an image cannot change.
    /// Running leaves OPcache off so an edit takes effect immediately.
    /// </para>
    /// <para>
    /// Settings are applied as php.ini values, so they work the same whether the resource runs as a local
    /// process or in a container.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithPhpOptimizations(options =>
    ///        {
    ///            options.OpcachePreloadScript = "vendor/autoload.php";
    ///            options.IgbinaryForRedis = true;
    ///        });
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithPhpOptimizations<T>(
        this IResourceBuilder<T> builder,
        Action<PhpOptimizationOptions>? configure = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new PhpOptimizationOptions();
        configure?.Invoke(options);

        // Read once, here: run and publish are separate processes, so the mode cannot change afterwards.
        var isPublish = builder.ApplicationBuilder.ExecutionContext.IsPublishMode;

        builder.WithAnnotation(new PhpOptimizationAnnotation(options), ResourceAnnotationMutationBehavior.Replace);

        ApplyOpcache(builder, options, isPublish);
        ApplySerializers(builder, options);
        ApplyRealpathCache(builder, options);

        return builder;
    }

    private static void ApplyOpcache<T>(IResourceBuilder<T> builder, PhpOptimizationOptions options, bool isPublish)
        where T : IPhpResource
    {
        var enabled = options.Opcache ?? isPublish;

        builder.WithPhpIniSetting("opcache.enable", enabled ? "1" : "0");

        // The CLI has its own switch, and a worker or console command benefits from bytecode caching just as
        // much as a web request does.
        builder.WithPhpIniSetting("opcache.enable_cli", enabled ? "1" : "0");

        if (!enabled)
        {
            return;
        }

        // Off when publishing: the source inside an image cannot change, so the check is a wasted stat on
        // every included file of every request.
        var validateTimestamps = options.OpcacheValidateTimestamps ?? !isPublish;
        builder.WithPhpIniSetting("opcache.validate_timestamps", validateTimestamps ? "1" : "0");

        builder
            .WithPhpIniSetting("opcache.memory_consumption", Number(options.OpcacheMemoryMegabytes))
            .WithPhpIniSetting("opcache.max_accelerated_files", Number(options.OpcacheMaxAcceleratedFiles))
            .WithPhpIniSetting("opcache.interned_strings_buffer", Number(options.OpcacheInternedStringsMegabytes));

        if (options.OpcacheJit)
        {
            // tracing is the general-purpose mode; the alternative, function mode, only helps a narrow set of
            // numeric workloads.
            builder
                .WithPhpIniSetting("opcache.jit", "tracing")
                .WithPhpIniSetting("opcache.jit_buffer_size", $"{Number(options.OpcacheJitBufferMegabytes)}M");
        }

        // Publishing only. A preloaded file cannot be changed without restarting PHP, which would make an
        // edit-and-refresh loop behave in a way nobody would guess from the symptom.
        if (isPublish && options.OpcachePreloadScript is { } preloadScript)
        {
            builder.WithPhpIniSetting(
                "opcache.preload",
                $"{PhpImages.AppBaseDirectory}/{preloadScript.Replace('\\', '/').TrimStart('/')}");

            // Preloading runs as this user; without it PHP refuses to preload when running as root.
            builder.WithPhpIniSetting("opcache.preload_user", PhpImages.ContainerUser);
        }
    }

    private static void ApplySerializers<T>(IResourceBuilder<T> builder, PhpOptimizationOptions options)
        where T : IPhpResource
    {
        if (options.Igbinary)
        {
            builder
                .WithPhpExtension("igbinary")
                .WithPhpIniSetting("session.serialize_handler", "igbinary");
        }

        if (options.Apcu)
        {
            builder.WithPhpExtension("apcu");

            if (options.Igbinary)
            {
                // Halves what every cache store and fetch has to serialize.
                builder.WithPhpIniSetting("apc.serializer", "igbinary");
            }
        }

        if (options.IgbinaryForRedis)
        {
            // The rebuild itself happens in the generated Dockerfile, which reads the annotation. Requesting
            // the extension here keeps the two consistent when only this option is set.
            builder.WithPhpExtension("redis");
        }
    }

    private static void ApplyRealpathCache<T>(IResourceBuilder<T> builder, PhpOptimizationOptions options)
        where T : IPhpResource
        => builder
            .WithPhpIniSetting("realpath_cache_size", $"{Number(options.RealpathCacheSizeKilobytes)}K")
            .WithPhpIniSetting("realpath_cache_ttl", Number(options.RealpathCacheTtlSeconds));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
