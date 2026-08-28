using Microsoft.Extensions.DependencyInjection;
#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpApacheTests
{
    [Fact]
    public void UsesTheApacheImage()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApacheApp("legacy", directory.Path);

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("serversideup/php:8.5-fpm-apache", dockerfile, StringComparison.Ordinal);

        // No Alpine variant of the Apache image exists, so it must not be requested.
        Assert.DoesNotContain("fpm-apache-alpine", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("frankenphp", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedVersionSelectsTheApacheTag()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApacheApp("legacy", directory.Path).WithPhpVersion("8.4");

        Assert.Contains(
            "serversideup/php:8.4-fpm-apache",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetsApacheVariablesRatherThanCaddyOnes()
    {
        // Neither server reads the other's variables, so getting this wrong is a container that starts and
        // then serves nothing.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApacheApp("legacy", directory.Path, "htdocs");

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("/var/www/html/htdocs", env["APACHE_DOCUMENT_ROOT"]);
        Assert.True(env.ContainsKey("APACHE_HTTP_PORT"));
        Assert.False(env.ContainsKey("CADDY_SERVER_ROOT"));
        Assert.False(env.ContainsKey("CADDY_HTTP_PORT"));
    }

    [Fact]
    public void AlwaysRunsAsAContainer()
    {
        // PHP's built-in server ignores .htaccess entirely, so falling back to it would quietly change
        // the application's behaviour.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddPhpApacheApp("legacy", directory.Path);

        Assert.IsType<PhpWebContainerAppResource>(php.Resource);
        Assert.Equal(PhpRunMode.Container, php.Resource.RunMode);
    }

    [Fact]
    public void StillSupportsTheRestOfTheApi()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApacheApp("legacy", directory.Path)
            .WithPhpExtension(PhpExtensions.Gd)
            .WithPhpIniSetting("memory_limit", "256M");

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("install-php-extensions gd", dockerfile, StringComparison.Ordinal);
        Assert.Contains("memory_limit=256M", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void FrankenPhpRemainsTheDefault()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("app", directory.Path);

        Assert.Contains(
            "frankenphp",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    private static async Task<Dictionary<string, string>> GetEnvironmentAsync(
        IDistributedApplicationBuilder builder,
        IResource resource)
    {
        using var app = builder.Build();
        var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        var configuration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext);

        if (configuration.Exception is { } exception)
        {
            throw exception;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in configuration.EnvironmentVariables)
        {
            values[variable.Key] = variable.Value;
        }

        return values;
    }
}
