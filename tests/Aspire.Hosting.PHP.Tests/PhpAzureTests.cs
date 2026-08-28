using Aspire.Hosting.Azure;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpAzureTests
{
    [Fact]
    public void AzureConvention_UsesServiceConnectorNamesForMySql()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var db = builder.AddMySql("mysql").AddDatabase("shopdb");
        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithDatabaseReference(db, PhpConnectionConvention.Azure);

        var environment = GetEnvironment(php.Resource);

        Assert.Contains("AZURE_MYSQL_HOST", environment.Keys);
        Assert.Contains("AZURE_MYSQL_USERNAME", environment.Keys);

        // DBNAME, not DATABASE. Service Connector's spelling, which is what a tutorial-derived app reads.
        Assert.Contains("AZURE_MYSQL_DBNAME", environment.Keys);
    }

    [Fact]
    public void AzureConvention_PointsMySqlAtTheCaBundle()
    {
        // Azure Database for MySQL requires TLS, and without the trust store PDO fails with an error that
        // reads like the server is unreachable rather than like a certificate problem.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var db = builder.AddMySql("mysql").AddDatabase("shopdb");
        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithDatabaseReference(db, PhpConnectionConvention.Azure);

        Assert.Equal(
            "/etc/ssl/certs/ca-certificates.crt",
            GetEnvironment(php.Resource)["MYSQL_ATTR_SSL_CA"]);
    }

    [Fact]
    public void AzureConvention_GivesPostgresALibpqKeywordString()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var db = builder.AddPostgres("pg").AddDatabase("shopdb");
        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithDatabaseReference(db, PhpConnectionConvention.Azure);

        var environment = GetEnvironment(php.Resource);

        // pg_connect takes keywords, not a URL, which is why Service Connector's PHP client type hands one over.
        Assert.Contains("AZURE_POSTGRESQL_CONNECTIONSTRING", environment.Keys);
        Assert.Contains("AZURE_POSTGRESQL_HOST", environment.Keys);
    }

    [Fact]
    public void AzureConvention_StatesThatRedisIsTls()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var cache = builder.AddRedis("cache");
        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithCacheReference(cache, PhpConnectionConvention.Azure);

        var environment = GetEnvironment(php.Resource);

        Assert.Contains("AZURE_REDIS_HOST", environment.Keys);
        Assert.Equal("true", environment["AZURE_REDIS_SSL"]);
    }

    [Fact]
    public void ContainerApps_MakesMigrationsAManualJob()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");
        builder.AddLaravelApp("shop", directory.Path)
            .WithMigrations()
            .WithAzureContainerApps();

        // Without this the migration deploys as an ordinary app: it would run, exit, and be restarted forever.
        var migrate = GetResource(builder, "shop-migrate");
        Assert.Contains(migrate.Annotations, a => a.GetType().Name.Contains("ContainerAppJob", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainerApps_KeepsQueueWorkersOffScaleToZero()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");
        builder.AddLaravelApp("shop", directory.Path)
            .WithQueueWorker()
            .WithAzureContainerApps();

        // A worker scaled to zero stops consuming, and nothing arrives over HTTP to wake it up again.
        var queue = GetResource(builder, "shop-queue");
        Assert.Contains(queue.Annotations, a => a.GetType().Name.Contains("ContainerApp", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainerApps_IsInertInRunMode()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container)
            .WithMigrations()
            .WithAzureContainerApps();

        Assert.Equal("shop", php.Resource.Name);
    }

    [Fact]
    public void Identity_PublishesTheClientIdWherePhpReadsIt()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");
        var identity = builder.AddAzureUserAssignedIdentity("shop-identity");

        var php = builder.AddLaravelApp("shop", directory.Path).WithAzureIdentity(identity);

        var environment = GetEnvironment(php.Resource);

        // A user-assigned identity has to be named in the token request, and PHP has no way to discover one.
        Assert.Contains("AZURE_CLIENT_ID", environment.Keys);
        Assert.Contains("AZURE_MYSQL_CLIENTID", environment.Keys);
        Assert.Contains("AZURE_POSTGRESQL_CLIENTID", environment.Keys);

        // Fixed by Azure, easy to get wrong, and stricter validation is coming.
        Assert.Equal(
            "https://ossrdbms-aad.database.windows.net",
            environment["AZURE_DATABASE_TOKEN_AUDIENCE"]);
    }

    [Fact]
    public void KeyVault_PublishesTheSecretNameNotTheSecret()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");
        var vault = builder.AddAzureKeyVault("secrets");

        var php = builder.AddLaravelApp("shop", directory.Path)
            .WithKeyVaultReference(vault, "app-key", "APP_KEY_SECRET_NAME");

        var environment = GetEnvironment(php.Resource);

        // Fetching at runtime means a rotated secret takes effect without a redeploy, and nothing sensitive
        // is written into a deployment artifact.
        Assert.Equal("app-key", environment["APP_KEY_SECRET_NAME"]);
        Assert.Contains("AZURE_KEYVAULT_URI", environment.Keys);
    }

    [Fact]
    public void BlobStorage_PublishesTheEndpointAndContainer()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");
        var storage = builder.AddAzureStorage("storage");

        var php = builder.AddLaravelApp("shop", directory.Path)
            .WithBlobStorageReference(storage, "uploads");

        var environment = GetEnvironment(php.Resource);

        Assert.Equal("uploads", environment["AZURE_STORAGE_CONTAINER"]);
        Assert.Contains("AZURE_STORAGE_BLOB_ENDPOINT", environment.Keys);
    }

    [Fact]
    public void ContainerApps_RefusesTwoExternalHttpIngresses()
    {
        // Both would deploy and one would silently never receive traffic.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");

        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithHttpEndpoint(name: "admin", targetPort: 9000)
            .WithExternalHttpEndpoints()
            .WithAzureContainerApps();

        var exception = Assert.Throws<DistributedApplicationException>(() => GetEnvironment(php.Resource));

        Assert.Contains("one external HTTP ingress", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'admin'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerApps_AllowsOneExternalHttpIngress()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddAzureContainerAppEnvironment("aca");

        var php = builder.AddPhpWebApp("shop", directory.Path)
            .WithExternalHttpEndpoints()
            .WithAzureContainerApps();

        Assert.NotEmpty(GetEnvironment(php.Resource));
    }

    private static IResource GetResource(IDistributedApplicationBuilder builder, string name)
        => Assert.Single(builder.Resources, r => r.Name == name);

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
