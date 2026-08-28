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
       .WithExternalHttpEndpoints();

builder.Build().Run();
