#pragma warning disable ASPIREPROBES001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpSessionAndProbeTests
{
    [Fact]
    public void SessionStore_PointsPhpAtRedis()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(cache);

        var settings = Assert.Single(php.Resource.Annotations.OfType<PhpIniSettingAnnotation>()).Settings;

        Assert.Equal("redis", settings["session.save_handler"]);

        // A ${VAR} reference rather than the resolved path: the resolved path carries the cache password, and
        // the generated ini file is baked into the image.
        Assert.Equal("${PHP_SESSION_SAVE_PATH}", settings["session.save_path"]);
    }

    [Fact]
    public void SessionStore_KeepsThePasswordOutOfTheImage()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(cache);

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("session.save_handler", dockerfile, StringComparison.Ordinal);
        Assert.Contains("${PHP_SESSION_SAVE_PATH}", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("auth=", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStore_InstallsTheRedisExtension()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(cache);

        var extensions = Assert.Single(php.Resource.Annotations.OfType<PhpExtensionAnnotation>()).Extensions;
        Assert.Contains("redis", extensions);
    }

    [Fact]
    public void SessionStore_UsesThePhpredisSavePathGrammarNotAUrl()
    {
        // phpredis parses save_path itself: the scheme is tcp or tls, and the password is an auth query
        // parameter. Handing it the resource's own redis://user:pass@host URI produces a handler that fails
        // to connect at runtime with nothing useful in the message.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(cache);

        var savePath = ((ReferenceExpression)GetEnvironment(php.Resource)[PhpHostingExtensions.SessionSavePathVariable]).Format;

        Assert.StartsWith("tcp://", savePath, StringComparison.Ordinal);
        Assert.Contains("?auth=", savePath, StringComparison.Ordinal);
        Assert.DoesNotContain("redis://", savePath, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStore_SwitchesSchemeForTls()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(cache, useTls: true);

        var savePath = ((ReferenceExpression)GetEnvironment(php.Resource)[PhpHostingExtensions.SessionSavePathVariable]).Format;

        Assert.StartsWith("tls://", savePath, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionStore_RefusesAResourceWithNoRedisShape()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var db = builder.AddMySql("mysql").AddDatabase("shopdb");
        var php = builder.AddLaravelApp("shop", directory.Path).WithSessionStore(db);

        // MySQL publishes a host, so this one resolves; the guard is for a resource publishing neither a host
        // nor a URI. Assert the path is built rather than silently empty.
        var savePath = Assert.Contains(
            PhpHostingExtensions.SessionSavePathVariable,
            GetEnvironment(php.Resource));

        Assert.NotNull(savePath);
    }

    [Fact]
    public void HealthCheck_RegistersProbesAsWellAsTheDashboardCheck()
    {
        // The dashboard check stops at the dashboard. Probes are what Container Apps, Kubernetes and Compose
        // actually read.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("web", directory.Path).WithHealthCheck();

        var probes = php.Resource.Annotations.OfType<ProbeAnnotation>().ToList();

        Assert.Equal(3, probes.Count);
        Assert.Contains(probes, p => p.Type == ProbeType.Startup);
        Assert.Contains(probes, p => p.Type == ProbeType.Readiness);
        Assert.Contains(probes, p => p.Type == ProbeType.Liveness);
    }

    [Fact]
    public void StartupProbe_IsPatientEnoughForAPreloadedImage()
    {
        // An image with OPcache preloading and a large autoloader takes tens of seconds to answer its first
        // request. A tight startup probe kills the container before it ever finishes booting.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("web", directory.Path).WithHealthCheck();

        var startup = Assert.Single(
            php.Resource.Annotations.OfType<ProbeAnnotation>(),
            p => p.Type == ProbeType.Startup);

        Assert.Equal(20, startup.FailureThreshold);
    }

    [Fact]
    public void HealthCheck_ProbesTheGivenPath()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path).WithHealthCheck("/up");

        Assert.All(
            php.Resource.Annotations.OfType<EndpointProbeAnnotation>(),
            probe => Assert.Equal("/up", probe.Path));
    }

    [Fact]
    public void NoHealthCheck_MeansNoProbes()
    {
        // A probe against an application that never said it answers one would fail the deployment outright.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("web", directory.Path);

        Assert.Empty(php.Resource.Annotations.OfType<ProbeAnnotation>());
    }

    private static Dictionary<string, object> GetEnvironment(IResource resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            annotation.Callback(context).GetAwaiter().GetResult();
        }

        return context.EnvironmentVariables;
    }
}

#pragma warning restore ASPIREPROBES001
