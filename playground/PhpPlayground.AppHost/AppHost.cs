var builder = DistributedApplication.CreateBuilder(args);

// Gives `aspire publish` somewhere to publish to. Without a compute environment it emits nothing.
builder.AddDockerComposeEnvironment("compose");

// A long-running worker. Runs `php worker.php` locally when PHP is installed, otherwise in a container.
builder.AddPhpApp("worker", "../php-worker", "worker.php")
       .WithComposer();

// A web application. Publishes as a single FrankenPHP container.
builder.AddPhpWebApp("web", "../php-web")
       .WithComposer()
       .WithOpenTelemetry()
       .WithPhpIniSetting("memory_limit", "512M")
       .WithPhpOptimizations(options =>
       {
           // Keeps the framework's classes resident instead of linking them on every request.
           options.OpcachePreloadScript = "vendor/autoload.php";
           options.IgbinaryForRedis = true;
       })
       .WithExternalHttpEndpoints();

// Backing services, and a PHP app that actually connects to them. This is what proves the reference
// translation works: Aspire hands over an ADO.NET connection string, and PHP reads DB_* and REDIS_*.
var db = builder.AddMySql("mysql")
                .AddDatabase("appdb");

var cache = builder.AddRedis("cache");

builder.AddPhpWebApp("data", "../php-db")
       .WithComposer()
       .WithDatabaseReference(db)
       .WithCacheReference(cache)
       .WaitFor(db)
       .WaitFor(cache)
       .WithExternalHttpEndpoints();

// The same sample against PostgreSQL, to prove the driver mapping rather than only the MySQL path.
var pgDb = builder.AddPostgres("pg")
                  .AddDatabase("pgappdb");

builder.AddPhpWebApp("data-pg", "../php-db")
       .WithComposer()
       .WithDatabaseReference(pgDb)
       .WaitFor(pgDb)
       .WithExternalHttpEndpoints();

builder.Build().Run();
