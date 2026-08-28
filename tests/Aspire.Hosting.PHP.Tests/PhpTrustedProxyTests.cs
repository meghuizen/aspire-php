using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpTrustedProxyTests
{
    [Fact]
    public void Laravel_TrustsProxiesWhenPublished()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path);

        Assert.Equal("*", GetEnvironment(php.Resource)["TRUSTED_PROXIES"]);
    }

    [Fact]
    public void Symfony_NamesTheHeadersAsWellAsTheProxies()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddSymfonyApp("shop", directory.Path);
        var environment = GetEnvironment(php.Resource);

        // REMOTE_ADDR is Symfony's idiom for "whatever is directly in front of me", which is the ingress.
        Assert.Equal("REMOTE_ADDR", environment["TRUSTED_PROXIES"]);
        Assert.Contains("x-forwarded-proto", (string)environment["TRUSTED_HEADERS"], StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitProxyListIsUsedVerbatim()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path).WithTrustedProxies("10.0.0.0/8");

        Assert.Equal("10.0.0.0/8", GetEnvironment(php.Resource)["TRUSTED_PROXIES"]);
    }

    [Fact]
    public void AnEmptyProxyListOptsOut()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path).WithTrustedProxies("");

        Assert.DoesNotContain("TRUSTED_PROXIES", GetEnvironment(php.Resource).Keys);
    }

    [Fact]
    public void NothingIsTrustedWhenRunningLocally()
    {
        // There is no proxy in front of a local process, and claiming otherwise would let a forged
        // X-Forwarded-Proto header change how the application builds its URLs.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container);

        Assert.DoesNotContain("TRUSTED_PROXIES", GetEnvironment(php.Resource).Keys);
    }

    [Fact]
    public void WordPress_GetsTheServerShimBecauseItHasNoEnvironmentVariable()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddWordPressApp("blog", directory.Path);
        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("base64 -d", dockerfile, StringComparison.Ordinal);
        Assert.Contains("auto_prepend_file", dockerfile, StringComparison.Ordinal);

        // WordPress reads $_SERVER['HTTPS'] directly, so there is no variable to set instead.
        Assert.DoesNotContain("TRUSTED_PROXIES", GetEnvironment(php.Resource).Keys);
    }

    [Fact]
    public void Laravel_DoesNotGetTheShim()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path);

        Assert.DoesNotContain("auto_prepend_file", PhpTestBuilder.RenderPublishDockerfile(php.Resource), StringComparison.Ordinal);
    }

    [Fact]
    public void TheShimDecodesToValidPhpThatOnlyEverUpgradesToHttps()
    {
        var shim = PhpHostingExtensions.ForwardedHeaderShimContent;

        Assert.StartsWith("<?php", shim, StringComparison.Ordinal);
        Assert.Contains("HTTP_X_FORWARDED_PROTO", shim, StringComparison.Ordinal);

        // Never demotes: the headers can only be set by the proxy in front, and if something else set them,
        // believing the request was HTTPS is the safe direction to be wrong in.
        Assert.DoesNotContain("unset(", shim, StringComparison.Ordinal);
        Assert.DoesNotContain("'off'", shim, StringComparison.Ordinal);
    }

    [Fact]
    public void AnApplicationsOwnPrependFileIsNotReplaced()
    {
        // PHP allows one auto_prepend_file. Overwriting the application's would break it outright, which is
        // worse than leaving the shim unapplied.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddWordPressApp("blog", directory.Path)
            .WithPhpIniSetting("auto_prepend_file", "/var/www/html/bootstrap.php");

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("/var/www/html/bootstrap.php", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain(PhpHostingExtensions.ForwardedHeaderShimPath, dockerfile, StringComparison.Ordinal);
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
