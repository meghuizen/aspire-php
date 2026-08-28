#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpDockerfileGeneratorTests
{
    [Fact]
    public void Publish_InstallsRequestedExtensionsAsRoot()
    {
        var dockerfile = RenderWorker(php => php.WithPhpExtension("pdo_pgsql").WithPhpExtension("redis"));

        Assert.Contains("USER root", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN install-php-extensions pdo_pgsql redis", dockerfile, StringComparison.Ordinal);

        // Dropping back to the unprivileged user is the whole point of choosing these images.
        Assert.Contains("USER www-data", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_DoesNotSwitchToRootWhenNothingNeedsIt()
    {
        var dockerfile = RenderWorker(php => php);

        Assert.DoesNotContain("USER root", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_DeduplicatesExtensionsButKeepsCallOrder()
    {
        var dockerfile = RenderWorker(php => php
            .WithPhpExtension("redis", "pdo_pgsql")
            .WithPhpExtension("redis", "opentelemetry"));

        Assert.Contains("RUN install-php-extensions redis pdo_pgsql opentelemetry", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_WritesIniSettingsToAFileThatLoadsLast()
    {
        var dockerfile = RenderWorker(php => php
            .WithPhpIniSetting("memory_limit", "512M")
            .WithPhpIniSetting("max_execution_time", "30"));

        // Sorted, so the generated Dockerfile is byte-identical between runs and Docker layer caching holds.
        Assert.Contains(
            @"RUN printf '%s\n%s\n' max_execution_time=30 memory_limit=512M > /usr/local/etc/php/conf.d/zzzz-aspire.ini",
            dockerfile,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_KeepsTheLastValueWhenAnIniSettingIsSetTwice()
    {
        var dockerfile = RenderWorker(php => php
            .WithPhpIniSetting("memory_limit", "256M")
            .WithPhpIniSetting("memory_limit", "512M"));

        Assert.Contains("memory_limit=512M", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("memory_limit=256M", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_QuotesIniValuesThatNeedIt()
    {
        var dockerfile = RenderWorker(php => php.WithPhpIniSetting("error_reporting", "E_ALL & ~E_DEPRECATED"));

        Assert.Contains("'error_reporting=E_ALL & ~E_DEPRECATED'", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_RejectsAnExtensionNameThatCouldInjectAShellCommand()
    {
        var exception = Assert.Throws<DistributedApplicationException>(
            () => RenderWorker(php => php.WithPhpExtension("redis; rm -rf /")));

        Assert.Contains("PHP extension name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_RejectsControlCharactersInIniValues()
    {
        var exception = Assert.Throws<DistributedApplicationException>(
            () => RenderWorker(php => php.WithPhpIniSetting("memory_limit", "512M\nUSER root")));

        Assert.Contains("control character", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_HonoursWithDockerfileBaseImage()
    {
        var dockerfile = RenderWorker(php => php.WithDockerfileBaseImage(runtimeImage: "cgr.dev/chainguard/php:latest"));

        Assert.Contains("FROM cgr.dev/chainguard/php:latest", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("serversideup", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_ExplainsThatBuildAndRuntimeImagesCannotDiffer()
    {
        // Generated PHP Dockerfiles are single stage, so silently picking one of the two would produce an
        // image the caller did not ask for.
        var exception = Assert.Throws<DistributedApplicationException>(
            () => RenderWorker(php => php.WithDockerfileBaseImage(
                buildImage: "example/build:1",
                runtimeImage: "example/runtime:1")));

        Assert.Contains("single stage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_SkipsComposerWhenTheApplicationHasNone()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var php = builder.AddPhpApp("worker", directory.Path, "worker.php");

        var dockerfile = PhpTestBuilder.RenderPublishDockerfile(php.Resource);

        Assert.DoesNotContain("composer", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_InstallsDependenciesBeforeCopyingSource()
    {
        var dockerfile = RenderWorker(php => php);

        var composerCopy = dockerfile.IndexOf("COPY --chown=www-data:www-data composer.json", StringComparison.Ordinal);
        var install = dockerfile.IndexOf("composer install", StringComparison.Ordinal);
        var sourceCopy = dockerfile.IndexOf("COPY --chown=www-data:www-data . .", StringComparison.Ordinal);

        // Editing a source file must not invalidate the dependency layer.
        Assert.True(composerCopy >= 0 && install > composerCopy && sourceCopy > install,
            $"Expected composer.json copy, then install, then source copy. Got {composerCopy}, {install}, {sourceCopy}.");
    }

    [Fact]
    public void Dev_BuildsTheEnvironmentButCopiesNoSource()
    {
        var directory = new TempAppDirectory();
        try
        {
            directory.WriteFile("composer.json", "{}");
            var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
            var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container);
            php.WithPhpExtension("redis");

            var dockerfile = PhpTestBuilder.RenderDevDockerfile(php.Resource);

            Assert.Contains("RUN install-php-extensions redis", dockerfile, StringComparison.Ordinal);

            // The source arrives through a bind mount, so copying it would shadow the mount and stale the image.
            Assert.DoesNotContain("COPY", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("composer install", dockerfile, StringComparison.Ordinal);
        }
        finally
        {
            directory.Dispose();
        }
    }

    private static string RenderWorker(Func<IResourceBuilder<IPhpResource>, IResourceBuilder<IPhpResource>> configure)
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", """{ "require": { "php": "^8.5" } }""");

        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var php = configure(builder.AddPhpApp("worker", directory.Path, "worker.php"));

        return PhpTestBuilder.RenderPublishDockerfile(php.Resource);
    }
}
