using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

/// <summary>
/// Translating an Aspire resource reference into the environment variables a PHP application reads.
/// </summary>
/// <remarks>
/// Asserted against real MySQL, PostgreSQL and Redis resources rather than fakes, because the whole point is
/// the shape of what those resources expose.
/// </remarks>
public class PhpConnectionReferenceTests
{
    [Fact]
    public async Task Laravel_GetsLaravelDatabaseNames()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("shopdb");

        var php = builder.AddLaravelApp("shop", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("mysql", env["DB_CONNECTION"]);
        Assert.Equal("shopdb", env["DB_DATABASE"]);
        Assert.Equal("root", env["DB_USERNAME"]);
        Assert.True(env.ContainsKey("DB_HOST"));
        Assert.True(env.ContainsKey("DB_PORT"));
        Assert.False(string.IsNullOrEmpty(env["DB_PASSWORD"]));

        // Laravel reads the parts; a DSN as well would leave it ambiguous which one won.
        Assert.False(env.ContainsKey("DATABASE_URL"));
    }

    [Fact]
    public async Task Laravel_UsesPgsqlForPostgres()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddPostgres("pg").AddDatabase("shopdb");

        var php = builder.AddLaravelApp("shop", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        // Laravel spells PostgreSQL "pgsql", not "postgres" or "postgresql".
        Assert.Equal("pgsql", env["DB_CONNECTION"]);
        Assert.Equal("shopdb", env["DB_DATABASE"]);
        Assert.Equal("postgres", env["DB_USERNAME"]);
    }

    [Fact]
    public async Task Symfony_GetsASingleDsn()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddPostgres("pg").AddDatabase("shopdb");

        var php = builder.AddSymfonyApp("shop", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.StartsWith("postgresql://", env["DATABASE_URL"], StringComparison.Ordinal);
        Assert.EndsWith("/shopdb", env["DATABASE_URL"], StringComparison.Ordinal);

        // Symfony reads the DSN only.
        Assert.False(env.ContainsKey("DB_HOST"));
    }

    [Fact]
    public async Task WordPress_CombinesHostAndPortIntoOneValue()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("wpdb");

        var php = builder.AddWordPressApp("blog", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        // WordPress takes "host:port" in one variable, not two.
        Assert.Contains(':', env["WORDPRESS_DB_HOST"]);
        Assert.Equal("wpdb", env["WORDPRESS_DB_NAME"]);
        Assert.Equal("root", env["WORDPRESS_DB_USER"]);
        Assert.False(string.IsNullOrEmpty(env["WORDPRESS_DB_PASSWORD"]));
    }

    [Fact]
    public async Task Joomla_UsesMysqliRatherThanMysql()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("joomladb");

        var php = builder.AddJoomlaApp("site", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        // Joomla's database type is spelled mysqli.
        Assert.Equal("mysqli", env["JOOMLA_DB_TYPE"]);
        Assert.Equal("joomladb", env["JOOMLA_DB_NAME"]);
        Assert.Contains(':', env["JOOMLA_DB_HOST"]);
    }

    [Fact]
    public async Task Drupal_GetsBothPartsAndADsn()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddPostgres("pg").AddDatabase("drupaldb");

        var php = builder.AddDrupalApp("site", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("drupaldb", env["DRUPAL_DATABASE_NAME"]);
        Assert.Equal("pgsql", env["DRUPAL_DATABASE_DRIVER"]);
        Assert.True(env.ContainsKey("DRUPAL_DATABASE_HOST"));
        Assert.True(env.ContainsKey("DRUPAL_DATABASE_PORT"));

        // settings.php variants read either shape, so both are supplied.
        Assert.StartsWith("postgresql://", env["DATABASE_URL"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generic_GetsPartsAndADsn()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("appdb");

        var php = builder.AddPhpWebApp("app", directory.Path).WithDatabaseReference(db);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("appdb", env["DB_DATABASE"]);
        Assert.StartsWith("mysql://", env["DATABASE_URL"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prefix_RenamesTheVariablesForASecondDatabase()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var primary = builder.AddMySql("mysql").AddDatabase("primarydb");
        var reporting = builder.AddMySql("reporting").AddDatabase("reportingdb");

        var php = builder.AddLaravelApp("shop", directory.Path)
            .WithDatabaseReference(primary)
            .WithDatabaseReference(reporting, prefix: "DB_REPORTING");

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.Equal("primarydb", env["DB_DATABASE"]);
        Assert.Equal("reportingdb", env["DB_REPORTING_DATABASE"]);
    }

    [Fact]
    public async Task Laravel_GetsLaravelRedisNames()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var cache = builder.AddRedis("cache");

        var php = builder.AddLaravelApp("shop", directory.Path).WithCacheReference(cache);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.True(env.ContainsKey("REDIS_HOST"));
        Assert.True(env.ContainsKey("REDIS_PORT"));

        // phpredis is the C extension WithCacheReference installs; Laravel would otherwise default to
        // predis, a Composer package that may not be present.
        Assert.Equal("phpredis", env["REDIS_CLIENT"]);
    }

    [Fact]
    public async Task Symfony_GetsARedisDsn()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var cache = builder.AddRedis("cache");

        var php = builder.AddSymfonyApp("shop", directory.Path).WithCacheReference(cache);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        // Redis builds its URI from the endpoint's scheme property rather than a literal, so in publish mode
        // the scheme is still a placeholder that the orchestrator resolves to "redis" at runtime. Asserting a
        // literal "redis://" here would be asserting against the wrong stage.
        var redisUrl = env["REDIS_URL"];
        Assert.Contains("cache.bindings.tcp.scheme", redisUrl, StringComparison.Ordinal);
        Assert.Contains("://", redisUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cache_OmitsThePasswordWhenThereIsNone()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        // An empty password is not the same as no password: some clients would try to authenticate with it.
        var cache = builder.AddRedis("cache").WithPassword(null!);

        var php = builder.AddLaravelApp("shop", directory.Path).WithCacheReference(cache);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.False(env.ContainsKey("REDIS_PASSWORD"));
    }

    [Fact]
    public void DatabaseReference_InstallsTheMatchingPdoExtension()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddPostgres("pg").AddDatabase("shopdb");

        var php = builder.AddLaravelApp("shop", directory.Path).WithDatabaseReference(db);

        Assert.Contains(
            "pdo_pgsql",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CacheReference_InstallsTheRedisExtension()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var cache = builder.AddRedis("cache");

        var php = builder.AddLaravelApp("shop", directory.Path).WithCacheReference(cache);

        Assert.Contains(
            "redis",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitConvention_BeatsTheApplicationDefault()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("shopdb");

        var php = builder.AddLaravelApp("shop", directory.Path)
            .WithDatabaseReference(db, PhpConnectionConvention.Symfony);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.True(env.ContainsKey("DATABASE_URL"));
        Assert.False(env.ContainsKey("DB_CONNECTION"));
    }

    [Fact]
    public async Task ConventionIsReadWhenTheReferenceIsResolved_NotWhenItIsAdded()
    {
        // So the order of the fluent calls does not silently change the result.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("shopdb");

        var php = builder.AddPhpWebApp("app", directory.Path)
            .WithDatabaseReference(db)
            .WithConnectionConvention(PhpConnectionConvention.WordPress);

        var env = await GetEnvironmentAsync(builder, php.Resource);

        Assert.True(env.ContainsKey("WORDPRESS_DB_NAME"));
    }

    /// <summary>
    /// Resolves a resource's environment variables the way the orchestrator does, so the assertions are made
    /// against the values the PHP process would actually receive rather than against unresolved expressions.
    /// </summary>
    private static async Task<Dictionary<string, string>> GetEnvironmentAsync(
        IDistributedApplicationBuilder builder,
        IResource resource)
    {
        // The execution context has to come from a built application: resolving a reference expression needs
        // the service provider, and a context constructed by hand does not have one.
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
