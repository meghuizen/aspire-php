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
    /// <param name="args">Overrides the command. Defaults to the framework's own scheduler command.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Laravel's <c>schedule:work</c> ticks every minute itself, so no cron daemon is involved. Symfony's
    /// scheduler runs as a Messenger transport, which is what is started here.
    /// </remarks>
    public static IResourceBuilder<T> WithScheduler<T>(this IResourceBuilder<T> builder, params string[] args)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        var resolved = ResolveFrameworkCommand(builder.Resource, args, PhpFrameworkCommands.Scheduler, "a scheduler");

        return builder.WithPhpConsoleCommand("scheduler", PhpConsoleCommandKind.LongRunning, resolved);
    }

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
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            throw new ArgumentException("A console command needs at least one argument.", nameof(args));
        }

        // Publishing does not run console commands: they would run on the machine doing the publish, which is
        // the wrong machine. Migrations belong to the deployment, not to building an image.
        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return builder;
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
            .ExcludeFromManifest()
            .WithIconName(kind == PhpConsoleCommandKind.OneShot ? "DatabaseArrowUp" : "Whiteboard");

        // Everything the application knows about its backing services applies to the command too, so both the
        // environment and the waits are copied rather than reconfigured. Done at start so that references and
        // WaitFor calls made after this one are still picked up.
        applicationBuilder.OnBeforeStart((_, _) =>
        {
            CopyEnvironmentCallbacks(resource, commandBuilder);
            CopyBackingServiceWaits(resource, commandBuilder);
            return Task.CompletedTask;
        });

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

        return builder;
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
