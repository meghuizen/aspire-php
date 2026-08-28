#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpOptimizationTests
{
    [Fact]
    public void Publish_TurnsOpcacheOnAndStopsItCheckingTheFilesystem()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations());

        Assert.Contains("opcache.enable=1", dockerfile, StringComparison.Ordinal);

        // The source inside an image cannot change, so the stat on every include is pure waste.
        Assert.Contains("opcache.validate_timestamps=0", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_LeavesOpcacheOffSoEditsTakeEffect()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container)
            .WithPhpOptimizations();

        var dockerfile = PhpTestBuilder.RenderDevDockerfile(php.Resource);

        Assert.Contains("opcache.enable=0", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Opcache_SizingIsWrittenOut()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations(o =>
        {
            o.OpcacheMemoryMegabytes = 256;
            o.OpcacheMaxAcceleratedFiles = 30000;
            o.OpcacheInternedStringsMegabytes = 32;
        }));

        Assert.Contains("opcache.memory_consumption=256", dockerfile, StringComparison.Ordinal);
        Assert.Contains("opcache.max_accelerated_files=30000", dockerfile, StringComparison.Ordinal);
        Assert.Contains("opcache.interned_strings_buffer=32", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Jit_TurnsOpcacheOnBecauseItIsPartOfOpcache()
    {
        // The JIT does nothing without OPcache. Requesting it while OPcache is off would produce ini that
        // reads correctly and silently never engages -- verified: opcache_get_status reports jit disabled.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container)
            .WithPhpOptimizations(o => o.OpcacheJit = true);

        var dockerfile = PhpTestBuilder.RenderDevDockerfile(php.Resource);

        Assert.Contains("opcache.enable=1", dockerfile, StringComparison.Ordinal);
        Assert.Contains("opcache.jit=tracing", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Jit_IsOffUnlessAskedFor()
    {
        // It helps numeric work and does close to nothing for ordinary request handling, so it is not a default.
        Assert.DoesNotContain("opcache.jit", RenderPublish(php => php.WithPhpOptimizations()), StringComparison.Ordinal);

        var withJit = RenderPublish(php => php.WithPhpOptimizations(o => o.OpcacheJit = true));
        Assert.Contains("opcache.jit=tracing", withJit, StringComparison.Ordinal);
        Assert.Contains("opcache.jit_buffer_size=64M", withJit, StringComparison.Ordinal);
    }

    [Fact]
    public void Igbinary_IsInstalledAndUsedForApcuAndSessions()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations());

        Assert.Contains("igbinary", dockerfile, StringComparison.Ordinal);
        Assert.Contains("apcu", dockerfile, StringComparison.Ordinal);
        Assert.Contains("apc.serializer=igbinary", dockerfile, StringComparison.Ordinal);
        Assert.Contains("session.serialize_handler=igbinary", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Igbinary_CanBeTurnedOff()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations(o =>
        {
            o.Igbinary = false;
            o.Apcu = false;
        }));

        Assert.DoesNotContain("igbinary", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("apc.serializer", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void IgbinaryForRedis_RebuildsTheExtensionAgainstIgbinaryFirst()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations(o => o.IgbinaryForRedis = true));

        // The base image ships Redis built without igbinary and the installer will not replace an extension
        // that is already present, so it has to be removed before it can be rebuilt.
        var igbinaryFirst = dockerfile.IndexOf("install-php-extensions igbinary", StringComparison.Ordinal);
        var rebuild = dockerfile.IndexOf("pecl uninstall -r redis", StringComparison.Ordinal);

        Assert.True(igbinaryFirst >= 0, "igbinary must be installed on its own first.");
        Assert.True(rebuild > igbinaryFirst, "Redis must be rebuilt after igbinary is present.");
        Assert.Contains("install-php-extensions redis", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void IgbinaryForRedis_IsOffByDefaultBecauseItCostsBuildTime()
    {
        Assert.DoesNotContain(
            "pecl uninstall",
            RenderPublish(php => php.WithPhpOptimizations()),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RealpathCache_IsEnlarged()
    {
        // The PHP default of 256K is far too small for a framework resolving thousands of include paths.
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations());

        Assert.Contains("realpath_cache_size=4096K", dockerfile, StringComparison.Ordinal);
        Assert.Contains("realpath_cache_ttl=600", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_UsesAnAuthoritativeClassmapWhenPublishing()
    {
        var dockerfile = RenderPublish(php => php.WithPhpOptimizations());

        Assert.Contains("composer dump-autoload --classmap-authoritative", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_KeepsTheFilesystemFallbackWhenNotOptimized()
    {
        var dockerfile = RenderPublish(php => php);

        Assert.Contains("composer dump-autoload --optimize", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Preload_IsAppliedOnlyWhenPublishing()
    {
        var published = RenderPublish(php => php.WithPhpOptimizations(o => o.OpcachePreloadScript = "vendor/autoload.php"));
        Assert.Contains("opcache.preload=/var/www/html/vendor/autoload.php", published, StringComparison.Ordinal);
        Assert.Contains("opcache.preload_user=www-data", published, StringComparison.Ordinal);

        // A preloaded file cannot change without restarting PHP, which would be baffling mid-edit.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
        var running = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container)
            .WithPhpOptimizations(o => o.OpcachePreloadScript = "vendor/autoload.php");

        Assert.DoesNotContain("opcache.preload", PhpTestBuilder.RenderDevDockerfile(running.Resource), StringComparison.Ordinal);
    }

    private static string RenderPublish(Func<IResourceBuilder<IPhpResource>, IResourceBuilder<IPhpResource>> configure)
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", """{ "require": { "php": "^8.5" } }""");

        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var php = configure(builder.AddPhpApp("worker", directory.Path, "worker.php"));

        return PhpTestBuilder.RenderPublishDockerfile(php.Resource);
    }
}
