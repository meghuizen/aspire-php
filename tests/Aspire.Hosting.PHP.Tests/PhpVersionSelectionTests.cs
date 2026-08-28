#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

/// <summary>
/// Switching PHP version. Both 8.5 and 8.4 have to work, from any of the three sources: an explicit
/// WithPhpVersion call, a .php-version file, or a composer.json constraint.
/// </summary>
public class PhpVersionSelectionTests
{
    [Theory]
    [InlineData("8.5")]
    [InlineData("8.4")]
    public void WithPhpVersion_PinsTheWorkerImageTag(string version)
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php").WithPhpVersion(version);

        Assert.Contains(
            $"FROM docker.io/serversideup/php:{version}-cli-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8.5")]
    [InlineData("8.4")]
    public void WithPhpVersion_PinsTheWebImageTag(string version)
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("shop", directory.Path).WithPhpVersion(version);

        Assert.Contains(
            $"FROM docker.io/serversideup/php:{version}-frankenphp-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8.4", "8.4")]
    [InlineData("8.4.24", "8.4")]
    [InlineData("8.5", "8.5")]
    public void PhpVersionFile_SelectsTheImageTag(string fileContents, string expectedTag)
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", fileContents);
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        Assert.Contains(
            $"serversideup/php:{expectedTag}-cli-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("^8.4", "8.4")]
    [InlineData(">=8.4", "8.4")]
    [InlineData("8.4.*", "8.4")]
    [InlineData("^8.5", "8.5")]
    public void ComposerConstraint_SelectsTheImageTag(string constraint, string expectedTag)
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", $$"""{ "require": { "php": "{{constraint}}" } }""");
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpWebApp("shop", directory.Path);

        Assert.Contains(
            $"serversideup/php:{expectedTag}-frankenphp-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WithPhpVersion_BeatsTheDetectedVersion()
    {
        // An explicit call is a deliberate override of whatever the application files happen to say.
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", "8.5");
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php").WithPhpVersion("8.4");

        Assert.Contains(
            "serversideup/php:8.4-cli-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WithPhpVersion_AlsoAppliesToTheRunModeContainerImage()
    {
        // Otherwise a pinned application would develop against one version and deploy another.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container)
            .WithPhpVersion("8.4");

        Assert.Contains(
            "FROM docker.io/serversideup/php:8.4-cli-alpine",
            PhpTestBuilder.RenderDevDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void WithPhpVersion_AcceptsAFullVersionAndKeepsMajorMinor()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php").WithPhpVersion("8.4.24");

        Assert.Contains(
            "serversideup/php:8.4-cli-alpine",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("8")]
    [InlineData("latest")]
    public void WithPhpVersion_RejectsSomethingThatIsNotAVersion(string version)
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        Assert.Throws<ArgumentException>(() => php.WithPhpVersion(version));
    }

    [Fact]
    public void WithDockerfileBaseImage_StillWinsOverAPinnedVersion()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php")
            .WithPhpVersion("8.4")
            .WithDockerfileBaseImage(runtimeImage: "docker.io/library/php:8.4-cli-alpine");

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.Contains("FROM docker.io/library/php:8.4-cli-alpine", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("serversideup", dockerfile, StringComparison.Ordinal);
    }
}
