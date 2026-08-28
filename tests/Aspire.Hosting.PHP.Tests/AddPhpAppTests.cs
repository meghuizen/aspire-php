using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class AddPhpAppTests
{
    [Fact]
    public void AddPhpApp_AddsAnExecutableResourceInPublishMode()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        var resource = Assert.IsType<PhpAppResource>(php.Resource);
        Assert.Equal("worker", resource.Name);
        Assert.Equal(directory.Path, resource.AppDirectory);
        Assert.Equal(PhpRunMode.Executable, resource.RunMode);
    }

    [Fact]
    public void AddPhpApp_ResolvesTheAppDirectoryRelativeToTheAppHost()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("api/worker.php", "<?php");
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", "api", "worker.php");

        Assert.Equal(Path.Combine(directory.Path, "api"), php.Resource.AppDirectory);
    }

    [Fact]
    public void AddPhpApp_UsesTheDetectedVersionForTheImageTag()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", "8.4");
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        Assert.Contains("serversideup/php:8.4-cli-alpine", PhpTestBuilder.RenderPublishDockerfile(php.Resource), StringComparison.Ordinal);
    }

    [Fact]
    public void AddPhpApp_RunsTheScript()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        Assert.Contains("""ENTRYPOINT ["php","worker.php"]""", PhpTestBuilder.RenderPublishDockerfile(php.Resource), StringComparison.Ordinal);
    }

    [Fact]
    public void AddPhpApp_ThrowsWhenRequiredArgumentsAreMissing()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        // Held in a variable rather than written inline: a literal empty name is rejected by the ResourceName
        // analyzer at compile time, which is the behaviour we want but is not what this test is checking.
        var empty = string.Empty;

        Assert.Throws<ArgumentException>(() => builder.AddPhpApp(empty, directory.Path, "worker.php"));
        Assert.Throws<ArgumentException>(() => builder.AddPhpApp("worker", empty, "worker.php"));
        Assert.Throws<ArgumentException>(() => builder.AddPhpApp("worker", directory.Path, empty));
    }

    [Fact]
    public void AddPhpWebApp_DefaultsToThePublicDocumentRoot()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("shop", directory.Path);

        Assert.Equal("public", php.Resource.DocumentRoot);
    }

    [Fact]
    public void AddPhpWebApp_AddsAnHttpEndpoint()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("shop", directory.Path);

        var endpoint = Assert.Single(php.Resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", endpoint.Name);
    }

    [Fact]
    public void AddPhpWebApp_UsesTheFrankenPhpImageAndNoEntrypoint()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("shop", directory.Path);
        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        // The base image starts FrankenPHP itself; overriding the entrypoint would stop it serving.
        Assert.Contains("serversideup/php:8.5-frankenphp-alpine", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ENTRYPOINT", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPhpApp_IsExecutableInPublishModeEvenWhenPhpIsNotInstalled()
    {
        // Publishing never runs anything locally and PublishAsDockerFile only works on an executable resource,
        // so the machine's PHP must not influence the shape of a published resource.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container);

        Assert.IsType<PhpAppResource>(php.Resource);
    }
}
