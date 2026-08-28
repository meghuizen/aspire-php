using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpConsoleCommandTests
{
    [Fact]
    public void Migrations_UseLaravelsCommandAndTheAppWaitsForThem()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container).WithMigrations();

        var migrate = GetResource(builder, "shop-migrate");
        Assert.Equal(["php", "artisan", "migrate", "--force"], GetArgs(migrate));

        // The schema has to be current before anything serves traffic.
        Assert.Contains(php.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == migrate);
    }

    [Fact]
    public void Migrations_UseSymfonysCommand()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddSymfonyApp("shop", directory.Path, PhpRunMode.Container).WithMigrations();

        Assert.Equal(
            ["php", "bin/console", "doctrine:migrations:migrate", "--no-interaction"],
            GetArgs(GetResource(builder, "shop-migrate")));
    }

    [Fact]
    public void Migrations_UseDrushForDrupal()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddDrupalApp("site", directory.Path, runMode: PhpRunMode.Container).WithMigrations();

        Assert.Equal(
            ["php", "vendor/bin/drush", "updatedb", "--yes"],
            GetArgs(GetResource(builder, "site-migrate")));
    }

    [Fact]
    public void Migrations_ExplainThemselvesWhenTheFrameworkHasNoConcept()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
        var php = builder.AddWordPressApp("blog", directory.Path, PhpRunMode.Container);

        var exception = Assert.Throws<DistributedApplicationException>(() => php.WithMigrations());

        Assert.Contains("WordPress", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no built-in command", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueWorker_RunsLongAndTheAppDoesNotWaitForIt()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container).WithQueueWorker();

        var worker = GetResource(builder, "shop-queue");
        Assert.Equal(["php", "artisan", "queue:work"], GetArgs(worker));

        // A worker never completes, so waiting on it would deadlock startup.
        Assert.DoesNotContain(php.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == worker);
    }

    [Fact]
    public void QueueWorker_WaitsForMigrations()
    {
        // Started against a stale schema, a worker fails in ways that look like application bugs.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container)
            .WithMigrations()
            .WithQueueWorker();

        var migrate = GetResource(builder, "shop-migrate");
        var worker = GetResource(builder, "shop-queue");

        Assert.Contains(worker.Annotations.OfType<WaitAnnotation>(), w => w.Resource == migrate);
    }

    [Fact]
    public void QueueWorker_CanBeAddedMoreThanOnceWithDifferentNames()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container)
            .WithQueueWorker()
            .WithQueueWorker("emails", "artisan", "queue:work", "--queue=emails");

        Assert.Equal(["php", "artisan", "queue:work"], GetArgs(GetResource(builder, "shop-queue")));
        Assert.Equal(
            ["php", "artisan", "queue:work", "--queue=emails"],
            GetArgs(GetResource(builder, "shop-emails")));
    }

    [Fact]
    public void TwoCommandsWithTheSameNameAreRejected()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container).WithQueueWorker();

        var exception = Assert.Throws<DistributedApplicationException>(() => php.WithQueueWorker());

        Assert.Contains("already has a console command", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scheduler_UsesLaravelsWorkerRatherThanCron()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container).WithScheduler();

        // schedule:work ticks every minute itself, so no cron daemon is involved.
        Assert.Equal(["php", "artisan", "schedule:work"], GetArgs(GetResource(builder, "shop-scheduler")));
    }

    [Fact]
    public void ConsoleCommands_ArePublished()
    {
        // The command is never executed at publish time -- that would run on the machine doing the publish.
        // It does have to reach the manifest, because that is the only way the deployment learns that
        // migrations and a queue worker exist at all.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path).WithMigrations().WithQueueWorker();

        // Args are asserted in run mode instead: publishing hands them to Aspire's own container substitution,
        // which resolves them during the publish rather than while the model is being built.
        Assert.Equal("shop-migrate", GetResource(builder, "shop-migrate").Name);
        Assert.Equal("shop-queue", GetResource(builder, "shop-queue").Name);
    }

    [Fact]
    public void PublishedConsoleCommands_CarryTheirKind()
    {
        // A one-shot migration and a long-running worker are the same shape in the app model. A deployment
        // target cannot tell them apart without being told, and it must: one is a job, the other must not
        // scale to zero.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path)
            .WithMigrations()
            .WithQueueWorker()
            .WithScheduler();

        Assert.Equal(PhpConsoleCommandKind.OneShot, GetKind(builder, "shop-migrate").Kind);
        Assert.Equal(PhpConsoleCommandKind.LongRunning, GetKind(builder, "shop-queue").Kind);

        var scheduler = GetKind(builder, "shop-scheduler");
        Assert.Equal(PhpConsoleCommandKind.Scheduled, scheduler.Kind);
        Assert.Equal("* * * * *", scheduler.CronExpression);
    }

    [Fact]
    public void Scheduler_TakesAnExplicitCron()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path).WithScheduler("*/5 * * * *");

        Assert.Equal("*/5 * * * *", GetKind(builder, "shop-scheduler").CronExpression);
    }

    [Fact]
    public void PublishedConsoleCommands_InheritTheApplicationsEnvironment()
    {
        // Publishing never raises BeforeStart, so a command whose environment is copied at start would reach
        // the deployment with no database configured at all.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var db = builder.AddMySql("mysql").AddDatabase("shopdb");

        builder.AddLaravelApp("shop", directory.Path)
            .WithDatabaseReference(db)
            .WithMigrations();

        var environment = GetEnvironment(GetResource(builder, "shop-migrate"));

        Assert.Contains("DB_HOST", environment.Keys);
        Assert.Contains("DB_DATABASE", environment.Keys);
    }

    [Fact]
    public void PublishedConsoleCommands_BuildTheSameImageAsTheApplication()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path).WithMigrations();

        // Same Dockerfile, same context: identical layers, so the second build is a cache hit rather than a
        // second compile of every extension.
        var migrate = GetResource(builder, "shop-migrate");
        Assert.Contains(migrate.Annotations, a => a is DockerfileBuildAnnotation);
    }

    [Fact]
    public void ConsoleCommand_RunsInTheSameImageAsTheApplication()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container)
            .WithPhpExtension("pdo_pgsql")
            .WithMigrations();

        var migrate = GetResource(builder, "shop-migrate");

        // Bind-mounted at the same place, so relative paths such as artisan resolve identically.
        var mount = Assert.Single(migrate.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/var/www/html", mount.Target);
    }

    [Fact]
    public void ConsoleCommand_InheritsTheApplicationsWaitOnItsDatabase()
    {
        // Migrations run before the app, so without this they would race the database and lose.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
        var db = builder.AddMySql("mysql").AddDatabase("shopdb");

        var php = builder.AddLaravelApp("shop", directory.Path, PhpRunMode.Container)
            .WithMigrations()
            .WaitFor(db);

        // The copy runs at BeforeStart so a WaitFor made after WithMigrations still counts. Raising that
        // event needs the DCP orchestrator, so the logic is exercised directly instead.
        var migrate = (IPhpConsoleResource)GetResource(builder, "shop-migrate");
        PhpHostingExtensions.CopyBackingServiceWaits(php.Resource, builder.CreateResourceBuilder(migrate));

        Assert.Contains(migrate.Annotations.OfType<WaitAnnotation>(), w => w.Resource == db.Resource);
    }

    private static IResource GetResource(IDistributedApplicationBuilder builder, string name)
        => Assert.Single(builder.Resources, r => r.Name == name);

    private static PhpConsoleKindAnnotation GetKind(IDistributedApplicationBuilder builder, string name)
        => Assert.Single(GetResource(builder, name).Annotations.OfType<PhpConsoleKindAnnotation>());

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

    private static string[] GetArgs(IResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args);

        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            annotation.Callback(context).GetAwaiter().GetResult();
        }

        return [.. args.Select(a => a.ToString()!)];
    }
}
