namespace Aspire.Hosting.PHP;

/// <summary>
/// Performance settings applied by <c>WithPhpOptimizations</c>.
/// </summary>
/// <remarks>
/// The defaults are the ones worth having on almost any application. Each maps to a specific, measurable cost:
/// OPcache removes recompiling every file on every request, igbinary halves what a cache round-trip serializes,
/// and the realpath cache removes repeated filesystem lookups for included files.
/// </remarks>
public sealed class PhpOptimizationOptions
{
    /// <summary>
    /// Caches compiled bytecode so PHP does not recompile every file on every request.
    /// </summary>
    /// <remarks>
    /// Defaults to on when publishing and off while running, so an edit takes effect immediately during
    /// development. Set explicitly to override.
    /// </remarks>
    public bool? Opcache { get; set; }

    /// <summary>
    /// Whether OPcache checks the filesystem for changed files.
    /// </summary>
    /// <remarks>
    /// Defaults to off when publishing, because the source in a container image cannot change and the check is
    /// a wasted stat on every included file for every request. On while running, so edits are picked up.
    /// <para>
    /// Turning this off in a container where you also bind-mount the source means your edits will not appear.
    /// </para>
    /// </remarks>
    public bool? OpcacheValidateTimestamps { get; set; }

    /// <summary>Memory for compiled bytecode, in megabytes. Defaults to 128.</summary>
    public int OpcacheMemoryMegabytes { get; set; } = 128;

    /// <summary>
    /// How many files OPcache will cache. Defaults to 20000.
    /// </summary>
    /// <remarks>
    /// A framework application easily exceeds the PHP default of 10000, and files beyond the limit are silently
    /// recompiled every request, which looks like OPcache simply not working.
    /// </remarks>
    public int OpcacheMaxAcceleratedFiles { get; set; } = 20_000;

    /// <summary>Memory for the shared string table, in megabytes. Defaults to 16.</summary>
    public int OpcacheInternedStringsMegabytes { get; set; } = 16;

    /// <summary>
    /// Compiles hot code to machine code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and measured rather than assumed. On a tight numeric loop the JIT cut runtime by
    /// roughly 40-50%. On a benchmark shaped like request handling — building arrays, encoding JSON, string
    /// work — the difference was inside run-to-run noise, and that benchmark performs no I/O at all, so a real
    /// request would show less again.
    /// </para>
    /// <para>
    /// It is not a security concern: PHP maps the JIT buffer twice, writable in one view and executable in the
    /// other, so no page is ever both. A process with the JIT active has zero writable-and-executable mappings.
    /// </para>
    /// <para>
    /// What it does cost is the buffer, reserved per process, for no gain on a typical web workload. It is also
    /// silently disabled whenever Xdebug is loaded, because Xdebug overrides the execution handler the JIT
    /// replaces — so it and debugging are mutually exclusive.
    /// </para>
    /// <para>
    /// Turning this on turns OPcache on as well, since the JIT is part of OPcache and does nothing without it.
    /// </para>
    /// </remarks>
    public bool OpcacheJit { get; set; }

    /// <summary>Memory for JIT-compiled code, in megabytes. Defaults to 64. Only used when <see cref="OpcacheJit"/> is on.</summary>
    public int OpcacheJitBufferMegabytes { get; set; } = 64;

    /// <summary>
    /// A script loaded once at startup whose classes stay in memory for every request, relative to the
    /// application directory.
    /// </summary>
    /// <remarks>
    /// Worthwhile for framework applications, where it removes the cost of linking the same few thousand
    /// classes on every request. Only applied when publishing: a preloaded file cannot be changed without
    /// restarting PHP, which would make development confusing.
    /// </remarks>
    public string? OpcachePreloadScript { get; set; }

    /// <summary>
    /// Installs APCu, a local in-memory key/value cache. Defaults to on.
    /// </summary>
    /// <remarks>
    /// APCu is per-process and not shared between containers, so it suits computed values that are cheap to
    /// rebuild, not shared application state. Use Redis for anything that has to be consistent across replicas.
    /// </remarks>
    public bool Apcu { get; set; } = true;

    /// <summary>
    /// Installs igbinary and makes it the serializer for APCu and sessions. Defaults to on.
    /// </summary>
    /// <remarks>
    /// igbinary is a drop-in replacement for PHP's <c>serialize()</c> that produces roughly half the bytes and
    /// parses faster. It only affects things that are serialized — caching and sessions — and does nothing for
    /// ordinary request handling.
    /// <para>
    /// The format is binary and PHP-specific. Anything outside PHP that has to read your cached data will not
    /// be able to.
    /// </para>
    /// </remarks>
    public bool Igbinary { get; set; } = true;

    /// <summary>
    /// Rebuilds the Redis extension so it can use igbinary as its serializer. Off by default.
    /// </summary>
    /// <remarks>
    /// The base images ship a Redis extension built without igbinary support, and the extension installer will
    /// not replace an extension that is already present. Turning this on uninstalls and rebuilds it, which adds
    /// roughly a minute to the image build.
    /// <para>
    /// It makes <c>Redis::SERIALIZER_IGBINARY</c> available; the application still has to select it with
    /// <c>$redis-&gt;setOption(Redis::OPT_SERIALIZER, Redis::SERIALIZER_IGBINARY)</c>.
    /// </para>
    /// </remarks>
    public bool IgbinaryForRedis { get; set; }

    /// <summary>Size of the resolved-path cache, in kilobytes. Defaults to 4096.</summary>
    /// <remarks>
    /// The PHP default of 256 KB is far too small for a framework application, which resolves thousands of
    /// include paths; the misses show up as repeated filesystem calls on every request.
    /// </remarks>
    public int RealpathCacheSizeKilobytes { get; set; } = 4096;

    /// <summary>How long resolved paths stay cached, in seconds. Defaults to 600.</summary>
    public int RealpathCacheTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Builds a Composer autoloader that never falls back to searching the filesystem. Defaults to on.
    /// </summary>
    /// <remarks>
    /// Applied when publishing only. A class that is not in the classmap will not be found, which is correct
    /// for a fixed image but wrong while developing, where new files appear all the time.
    /// </remarks>
    public bool ComposerClassmapAuthoritative { get; set; } = true;
}
