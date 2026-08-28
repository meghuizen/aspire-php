#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Applies database migrations before the application starts.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="args">Overrides the command. Defaults to the framework's own migration command.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Adds a child resource that runs once and exits; the application waits for it to succeed. Any queue
    /// worker or scheduler on the same application waits for it too, so nothing touches a schema that is not
    /// yet current.
    /// </para>
    /// <para>
    /// Defaults per framework: Laravel <c>artisan migrate --force</c>, Symfony
    /// <c>doctrine:migrations:migrate</c>, Drupal <c>drush updatedb</c>. WordPress and Joomla have no
    /// migration concept, so pass <paramref name="args"/> explicitly there.
    /// </para>
    /// <para>
    /// Remember to <c>WaitFor</c> the database, or migrations will race it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var db = builder.AddMySql("mysql").AddDatabase("shopdb");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithDatabaseReference(db)
    ///        .WaitFor(db)
    ///        .WithMigrations();
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithMigrations<T>(this IResourceBuilder<T> builder, params string[] args)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resolved = ResolveFrameworkCommand(builder.Resource, args, PhpFrameworkCommands.Migrate, "migrations");

        return builder.WithPhpConsoleCommand("migrate", PhpConsoleCommandKind.OneShot, resolved);
    }

    /// <summary>
    /// Runs a queue worker alongside the application.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">A suffix for the resource name, so several queues can be worked separately.</param>
    /// <param name="args">Overrides the command. Defaults to the framework's own queue command.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Runs until stopped, so the application does not wait for it. It does wait for migrations when
    /// <c>WithMigrations</c> is also configured.
    /// </para>
    /// <para>
    /// Concurrency belongs to the worker, not to Aspire: Aspire's replica support is limited to project
    /// resources, so use the tool's own options — Laravel Horizon, or <c>queue:work</c> arguments — rather than
    /// expecting this to scale out.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithMigrations()
    ///        .WithQueueWorker()
    ///        .WithQueueWorker("emails", "artisan", "queue:work", "--queue=emails");
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithQueueWorker<T>(
        this IResourceBuilder<T> builder,
        string? name = null,
        params string[] args)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resolved = ResolveFrameworkCommand(builder.Resource, args, PhpFrameworkCommands.QueueWorker, "a queue worker");

        return builder.WithPhpConsoleCommand(name ?? "queue", PhpConsoleCommandKind.LongRunning, resolved);
    }

    /// <summary>
    /// Runs the framework's scheduler alongside the application.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="cron">
    /// The schedule a deployment target should invoke the command on. Defaults to every minute. Ignored
    /// during <c>aspire run</c>, where the framework's own scheduler keeps time.
    /// </param>
    /// <param name="args">Overrides the command. Defaults to the framework's own scheduler command.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Laravel's <c>schedule:work</c> ticks every minute itself, so no cron daemon is involved during
    /// <c>aspire run</c>. Symfony's scheduler runs as a Messenger transport, which is what is started here.
    /// </para>
    /// <para>
    /// A deployment target with a scheduler of its own uses <paramref name="cron"/> instead, and runs the
    /// command once per tick rather than leaving a process alive to count minutes. That is both cheaper and
    /// more reliable: a process counting minutes stops counting when the container is recycled.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithScheduler<T>(
        this IResourceBuilder<T> builder,
        string? cron = null,
        params string[] args)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resolved = ResolveFrameworkCommand(builder.Resource, args, PhpFrameworkCommands.Scheduler, "a scheduler");

        // Every minute, because that is the cadence both Laravel's and Symfony's schedulers assume: they do
        // their own dispatch decisions and expect to be asked often enough not to miss one.
        return builder.WithPhpConsoleCommand(
            "scheduler",
            PhpConsoleCommandKind.Scheduled,
            cron ?? DefaultSchedulerCron,
            resolved);
    }

    /// <summary>The cadence Laravel's and Symfony's schedulers are designed to be invoked at.</summary>
    private const string DefaultSchedulerCron = "* * * * *";

    /// <summary>
    /// Runs any PHP console command alongside the application.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">A suffix for the resource name.</param>
    /// <param name="kind">Whether it runs once or until stopped.</param>
    /// <param name="args">The arguments passed to <c>php</c>, for example <c>artisan</c>, <c>cache:clear</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// The command runs in the same environment as the application — the same image, the same extensions, the
    /// same database and cache variables — so it sees exactly what the application sees.
    /// </remarks>
    public static IResourceBuilder<T> WithPhpConsoleCommand<T>(
        this IResourceBuilder<T> builder,
        string name,
        PhpConsoleCommandKind kind,
        params string[] args)
        where T : IPhpResource
        => builder.WithPhpConsoleCommand(name, kind, cron: null, args);

    private static IResourceBuilder<T> WithPhpConsoleCommand<T>(
        this IResourceBuilder<T> builder,
        string name,
        PhpConsoleCommandKind kind,
        string? cron,
        params string[] args)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new ArgumentException("A console command needs at least one argument.", nameof(args));
        }

        var applicationBuilder = builder.ApplicationBuilder;
        var resource = builder.Resource;
        var commandName = $"{resource.Name}-{name}";

        if (applicationBuilder.TryCreateResourceBuilder<IResource>(commandName, out _))
        {
            throw new DistributedApplicationException(
                $"The PHP app '{resource.Name}' already has a console command called '{name}'. " +
                "Give one of them a different name.");
        }

        var commandBuilder = CreateConsoleResource(applicationBuilder, commandName, resource, args);

        commandBuilder
            .WithParentRelationship(resource)
            .WithAnnotation(new PhpConsoleKindAnnotation(kind, cron), ResourceAnnotationMutationBehavior.Replace)
            .WithIconName(kind == PhpConsoleCommandKind.OneShot ? "DatabaseArrowUp" : "Whiteboard");

        // The command sees exactly what the application sees. Replaying the application's own callbacks at
        // resolution time rather than copying them now means references and WaitFor calls made after this one
        // are still picked up, and it works identically in run and publish mode — an OnBeforeStart hook would
        // not, because publishing never starts anything.
        commandBuilder.WithEnvironment(async context =>
        {
            if (!resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var callbacks))
            {
                return;
            }

            foreach (var callback in callbacks)
            {
                await callback.Callback(context).ConfigureAwait(false);
            }
        });

        if (applicationBuilder.ExecutionContext.IsRunMode)
        {
            // Deferred to start so that WaitFor calls made after this one are still picked up. Publishing has
            // no such event, so it copies the waits directly below and accepts the ordering constraint.
            applicationBuilder.OnBeforeStart((_, _) =>
            {
                CopyBackingServiceWaits(resource, commandBuilder);
                return Task.CompletedTask;
            });

            // Nothing outside the AppHost needs to know about a command that only exists while the dashboard
            // is up. Publishing is the opposite case: the manifest is the only way the deployment learns that
            // migrations and workers exist at all.
            ConfigureRunModeConsoleWaits(builder, commandBuilder, applicationBuilder, resource, kind);
        }
        else
        {
            ConfigurePublishedConsoleCommand(commandBuilder, resource);
            CopyBackingServiceWaits(resource, commandBuilder);

            if (kind == PhpConsoleCommandKind.OneShot)
            {
                // Compose renders this as depends_on/service_completed_successfully. Container Apps has no
                // equivalent, so B1 sequences jobs explicitly; the relationship is still worth stating.
                builder.WaitForCompletion(commandBuilder);
            }
        }

        return builder;
    }

    private static void ConfigureRunModeConsoleWaits<T>(
        IResourceBuilder<T> builder,
        IResourceBuilder<IPhpConsoleResource> commandBuilder,
        IDistributedApplicationBuilder applicationBuilder,
        IPhpResource resource,
        PhpConsoleCommandKind kind)
        where T : IPhpResource
    {
        commandBuilder.ExcludeFromManifest();

        if (kind == PhpConsoleCommandKind.OneShot)
        {
            builder.WaitForCompletion(commandBuilder);
        }
        else if (applicationBuilder.TryCreateResourceBuilder<IResource>($"{resource.Name}-migrate", out var migrations)
            && migrations is not null)
        {
            // A worker started against a stale schema fails in ways that look like application bugs.
            commandBuilder.WaitForCompletion(migrations);
        }
    }

    /// <summary>
    /// Gives a published console command an image of its own.
    /// </summary>
    /// <remarks>
    /// The command needs the same image as the application: the same extensions, the same ini settings, the
    /// same source. That is the same Dockerfile against the same build context, so the layers are identical
    /// and the second build is a cache hit rather than a second compile of every extension.
    /// </remarks>
    private static void ConfigurePublishedConsoleCommand(
        IResourceBuilder<IPhpConsoleResource> commandBuilder,
        IPhpResource resource)
    {
        if (commandBuilder is not IResourceBuilder<PhpConsoleResource> executableBuilder)
        {
            return;
        }

        var appDirectory = resource.AppDirectory;

        executableBuilder.PublishAsDockerFile(container =>
        {
            if (File.Exists(Path.Combine(appDirectory, "Dockerfile")))
            {
                return;
            }

            container.WithDockerfileBuilder(
                appDirectory,
                context => PhpDockerfileGenerator.WritePublishDockerfile(resource, context));

            if (!File.Exists(Path.Combine(appDirectory, ".dockerignore"))
                && container.Resource.TryGetLastAnnotation<DockerfileBuildAnnotation>(out var dockerfile))
            {
                dockerfile.BuildContextIgnoreContent ??= PhpDockerfileGenerator.DefaultBuildContextIgnoreContent;
            }
        });
    }

    private static IResourceBuilder<IPhpConsoleResource> CreateConsoleResource(
        IDistributedApplicationBuilder applicationBuilder,
        string commandName,
        IPhpResource resource,
        string[] args)
    {
        if (resource is PhpContainerAppResource containerResource)
        {
            var container = new PhpConsoleContainerResource(commandName);
            container.Annotations.Add(NameValidationPolicyAnnotation.None);

            return applicationBuilder.AddResource(container)
                .WithBindMount(containerResource.AppDirectory, PhpImages.AppBaseDirectory)
                .WithImage("placeholder")
                .WithDockerfileBuilder(
                    CreateEmptyBuildContext(commandName, containerResource.AppDirectory),
                    context => PhpDockerfileGenerator.WriteDevDockerfile(containerResource, context))
                .WithArgs(["php", .. args])
                .WithEnvironment("SHOW_WELCOME_MESSAGE", "false");
        }

        var executablePath = resource.TryGetLastAnnotation<PhpEnvironmentAnnotation>(out var environment)
            && environment.PhpExecutablePath is { } path
            ? path
            : "php";

        var executable = new PhpConsoleResource(commandName, executablePath, resource.AppDirectory);
        executable.Annotations.Add(NameValidationPolicyAnnotation.None);

        return applicationBuilder.AddResource(executable)
            .WithArgs(args)
            .WithRequiredCommand("php", PhpInstallHelpLink);
    }

    // The command needs the same database, cache and telemetry variables the application has. Rather than
    // duplicating the configuration, the application's own environment callbacks are replayed onto it.
    private static void CopyEnvironmentCallbacks(IPhpResource resource, IResourceBuilder<IPhpConsoleResource> commandBuilder)
    {
        if (!resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var callbacks))
        {
            return;
        }

        foreach (var callback in callbacks)
        {
            commandBuilder.WithAnnotation(callback);
        }
    }

    /// <summary>
    /// Gives the console command the same waits the application has on its backing services.
    /// </summary>
    /// <remarks>
    /// Without this, <c>WaitFor(db)</c> on the application would leave migrations racing the database — and
    /// they run first, so they would lose. Waits on the application's own console commands are skipped, since
    /// copying those would make a command wait on itself or on a sibling for no reason.
    /// </remarks>
    internal static void CopyBackingServiceWaits(IPhpResource resource, IResourceBuilder<IPhpConsoleResource> commandBuilder)
    {
        if (!resource.TryGetAnnotationsOfType<WaitAnnotation>(out var waits))
        {
            return;
        }

        foreach (var wait in waits)
        {
            if (wait.Resource is IPhpConsoleResource || ReferenceEquals(wait.Resource, commandBuilder.Resource))
            {
                continue;
            }

            commandBuilder.WithAnnotation(wait);
        }
    }

    private static string[] ResolveFrameworkCommand(
        IPhpResource resource,
        string[] args,
        Func<PhpConnectionConvention, string[]?> lookup,
        string description)
    {
        if (args.Length > 0)
        {
            return args;
        }

        var convention = resource.TryGetLastAnnotation<PhpConnectionConventionAnnotation>(out var annotation)
            ? annotation.Convention
            : PhpConnectionConvention.Generic;

        return lookup(convention)
            ?? throw new DistributedApplicationException(
                $"The PHP app '{resource.Name}' is a {PhpFrameworkCommands.DisplayName(convention)} application, " +
                $"which has no built-in command for {description}. Pass the command explicitly, for example " +
                "WithMigrations(\"bin/migrate.php\").");
    }
}

#pragma warning restore ASPIREDOCKERFILEBUILDER001
