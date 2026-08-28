using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpMailTests
{
    [Fact]
    public void Smtp_InstallsMsmtpAndPointsSendmailPathAtIt()
    {
        // mail() does not speak SMTP on Linux; it pipes to sendmail_path, so something has to be there.
        var dockerfile = RenderWorker(php => php.WithSmtp("smtp.example.test", 25));

        Assert.Contains("apk add --no-cache msmtp", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/usr/bin/msmtp", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_QuotesSendmailPathSoItIsNotTruncated()
    {
        // PHP's ini parser stops an unquoted value at the next '=', which turned
        // "msmtp --host=${MAIL_HOST} ..." into "msmtp --host" and made mail() fail silently.
        var dockerfile = RenderWorker(php => php.WithSmtp("smtp.example.test", 25));

        Assert.Contains(@"sendmail_path=""/usr/bin/msmtp --host=${MAIL_HOST}", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-t\"'", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_InstallsPackagesOnDebianToo()
    {
        // The Apache image is Debian, and the base image is overridable, so neither package manager can be
        // assumed to exist.
        var dockerfile = RenderWorker(php => php.WithSmtp("smtp.example.test", 25));

        Assert.Contains("if command -v apk", dockerfile, StringComparison.Ordinal);
        Assert.Contains("apt-get install", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_LeavesAuthenticationOffWhenThereIsNoUsername()
    {
        var dockerfile = RenderWorker(php => php.WithSmtp("smtp.example.test", 25));

        Assert.Contains("--auth=off", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("--passwordeval", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_ReadsThePasswordThroughACommandRatherThanAnArgument()
    {
        // A --password argument would be visible to anything that can run ps.
        var dockerfile = RenderWorker(php => php.WithSmtp("smtp.example.test", 587, username: "postmaster"));

        Assert.Contains("--auth=on", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--user=${MAIL_USERNAME}", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--passwordeval=echo", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Laravel_GetsLaravelsMailVariables()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path)
            .WithSmtp("smtp.example.test", 25, from: "shop@example.test", fromName: "Shop");

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("smtp", env["MAIL_MAILER"]);
        Assert.Equal("smtp.example.test", env["MAIL_HOST"]);
        Assert.Equal("25", env["MAIL_PORT"]);
        Assert.Equal("shop@example.test", env["MAIL_FROM_ADDRESS"]);
        Assert.Equal("Shop", env["MAIL_FROM_NAME"]);

        // Laravel spells "no encryption" as the literal string null in its own .env.example.
        Assert.Equal("null", env["MAIL_ENCRYPTION"]);
    }

    [Fact]
    public async Task Symfony_GetsASingleMailerDsn()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddSymfonyApp("shop", directory.Path).WithSmtp("smtp.example.test", 25);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.StartsWith("smtp://", env["MAILER_DSN"], StringComparison.Ordinal);
        Assert.DoesNotContain("MAIL_MAILER", env.Keys);
    }

    [Fact]
    public async Task WordPress_ReliesOnTheSendmailShim()
    {
        // WordPress calls mail() directly, so the shim is the whole mechanism; no framework variables apply.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddWordPressApp("blog", directory.Path).WithSmtp("smtp.example.test", 25);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("smtp.example.test", env["MAIL_HOST"]);
        Assert.DoesNotContain("MAIL_MAILER", env.Keys);
        Assert.DoesNotContain("MAILER_DSN", env.Keys);
    }

    private static string RenderWorker(Func<IResourceBuilder<IPhpResource>, IResourceBuilder<IPhpResource>> configure)
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var php = configure(builder.AddPhpApp("worker", directory.Path, "worker.php"));

        return PhpTestBuilder.RenderPublishDockerfile(php.Resource);
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
