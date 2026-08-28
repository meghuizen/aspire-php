using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.PHP;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Azure.Provisioning.Storage;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

/// <summary>
/// Azure-specific configuration for PHP applications.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in. Nothing here runs unless it is called, and .NET does not load an assembly until a method that
/// touches it is first executed, so an AppHost deploying to Docker Compose or Kubernetes never loads the
/// Azure assemblies these methods use.
/// </para>
/// <para>
/// What is here is only what Aspire cannot already work out. Aspire turns a published PHP container into a
/// Container App on its own, provisions a registry, and translates volumes into Azure Files mounts. It
/// cannot know that one console command is a migration and another is a queue worker, and it has no way to
/// give PHP an Entra token.
/// </para>
/// </remarks>
public static class PhpAzureExtensions
{
    /// <summary>
    /// The audience an access token for Azure Database for MySQL or PostgreSQL must be requested for.
    /// </summary>
    /// <remarks>
    /// Microsoft's documentation warns that stricter audience validation is coming and that tokens issued for
    /// any other audience will stop being accepted, so this is not a value to vary.
    /// </remarks>
    public const string DatabaseTokenAudience = "https://ossrdbms-aad.database.windows.net";

    /// <summary>
    /// Shapes an application's console commands for Azure Container Apps.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// A one-shot command becomes a manually triggered Container App Job, a scheduled one becomes a job on
    /// its cron, and a long-running worker becomes a Container App that is not allowed to scale to zero.
    /// Without this they would all deploy as ordinary apps: a migration would run, exit, and be restarted
    /// forever, and a scheduler would be a process counting minutes until its container was recycled.
    /// </para>
    /// <para>
    /// The kinds come from the annotations <c>WithMigrations</c>, <c>WithQueueWorker</c> and
    /// <c>WithScheduler</c> already record, so nothing has to be repeated per command.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.AddAzureContainerAppEnvironment("aca");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithMigrations()
    ///        .WithQueueWorker()
    ///        .WithScheduler()
    ///        .WithAzureContainerApps()
    ///        .WithExternalHttpEndpoints();
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithAzureContainerApps<T>(this IResourceBuilder<T> builder)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            // Nothing to shape: run mode has no Container Apps.
            return builder;
        }

        var applicationBuilder = builder.ApplicationBuilder;
        var parentName = builder.Resource.Name;

        foreach (var resource in applicationBuilder.Resources.ToList())
        {
            if (!resource.TryGetLastAnnotation<PhpConsoleKindAnnotation>(out var kind)
                || !IsChildOf(resource, parentName))
            {
                continue;
            }

            ShapeConsoleResource(applicationBuilder, resource, kind);
        }

        return builder.WithEnvironment(context =>
        {
            ValidateIngress(builder.Resource);
            WarnAboutAzureFiles(builder.Resource, context);
        });
    }

    /// <summary>
    /// Rejects an endpoint arrangement Container Apps cannot express.
    /// </summary>
    /// <remarks>
    /// Container Apps allows exactly one external HTTP ingress per app. Two would deploy, and one of them
    /// would simply never receive traffic — a failure that shows up as an unreachable URL long after the fact.
    /// Better to refuse it here, naming both endpoints.
    /// </remarks>
    private static void ValidateIngress(IPhpResource resource)
    {
        if (!resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints))
        {
            return;
        }

        var externalHttp = endpoints
            .Where(endpoint => endpoint.IsExternal
                && endpoint.UriScheme is "http" or "https")
            // Grouped by target port, because that is how Container Apps groups them: two endpoints on one
            // port are one ingress, not two.
            .GroupBy(endpoint => endpoint.TargetPort)
            .ToList();

        if (externalHttp.Count <= 1)
        {
            return;
        }

        var names = string.Join(", ", externalHttp.SelectMany(group => group).Select(endpoint => $"'{endpoint.Name}'"));

        throw new DistributedApplicationException(
            $"The PHP app '{resource.Name}' has external HTTP endpoints on more than one target port ({names}), " +
            "but an Azure Container App has exactly one external HTTP ingress. Make all but one internal, or " +
            "put them on the same target port.");
    }

    /// <summary>
    /// Warns that a data volume becomes an Azure Files mount, with the consequences that carries.
    /// </summary>
    /// <remarks>
    /// Aspire translates volumes into Azure Files shares. That works, but Azure Files is SMB: the mount has
    /// one fixed owner for the whole share and Container Apps exposes no way to set it, because the ARM type
    /// behind the mount carries no mount options at all. For uploads a blob container is the better answer
    /// and has adapters in every framework here, so the warning names it rather than only complaining.
    /// </remarks>
    private static void WarnAboutAzureFiles(IPhpResource resource, EnvironmentCallbackContext context)
    {
        if (!resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mounts) || !mounts.Any())
        {
            return;
        }

        context.Logger?.LogWarning(
            "PHP app '{Name}' has a volume, which Azure Container Apps mounts as an Azure Files share. The " +
            "share has a single fixed owner and Container Apps offers no way to set it, so an application " +
            "expecting to own its upload directory may not be able to write there. For uploads, " +
            "WithBlobStorageReference is the better fit.",
            resource.Name);
    }

    /// <summary>
    /// Gives the application a managed identity and publishes its client ID where the PHP side reads it.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="identity">The user-assigned identity, from <c>AddAzureUserAssignedIdentity</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Assigns the identity to the container and sets <c>AZURE_CLIENT_ID</c>, plus the
    /// <c>AZURE_MYSQL_CLIENTID</c> and <c>AZURE_POSTGRESQL_CLIENTID</c> names Service Connector uses. A
    /// user-assigned identity has to be named in the token request, so the PHP side needs the client ID and
    /// there is no way for it to discover one.
    /// </para>
    /// <para>
    /// Getting a token is the application's job, because there is nothing to delegate it to: Microsoft
    /// documents calling the REST API directly for PHP, and the Azure SDK for PHP was retired in 2021. The
    /// companion composer package <c>meghuizen/aspire-azure-identity</c> does it, and reads exactly these
    /// variables.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var identity = builder.AddAzureUserAssignedIdentity("shop-identity");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithAzureIdentity(identity);
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithAzureIdentity<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AzureUserAssignedIdentityResource> identity)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);

        builder.WithAzureUserAssignedIdentity(identity);

        return builder.WithEnvironment(context =>
        {
            var clientId = identity.Resource.ClientId;

            context.EnvironmentVariables["AZURE_CLIENT_ID"] = clientId;

            // Service Connector's per-service spelling. Set both because an application following Microsoft's
            // documentation reads whichever matches its database, and neither is derivable from the other.
            context.EnvironmentVariables["AZURE_MYSQL_CLIENTID"] = clientId;
            context.EnvironmentVariables["AZURE_POSTGRESQL_CLIENTID"] = clientId;

            // The audience is fixed by Azure and easy to get wrong. Publishing it means the PHP side reads a
            // value rather than carrying a constant that has to be kept in step with this package.
            context.EnvironmentVariables["AZURE_DATABASE_TOKEN_AUDIENCE"] = DatabaseTokenAudience;
        });
    }

    /// <summary>
    /// Lets the application read a secret from Key Vault with its managed identity.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="vault">The Key Vault resource.</param>
    /// <param name="secretName">The name of the secret to read.</param>
    /// <param name="environmentVariable">
    /// The variable the secret's name is published under. The application fetches the value itself; the
    /// secret is deliberately not resolved into the environment.
    /// </param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Needed even where everything else is passwordless, because plenty of secrets have no Entra path at
    /// all: third-party API keys, SMTP credentials, a Laravel <c>APP_KEY</c>. Microsoft's own flagship PHP
    /// tutorial uses Key Vault rather than a token for its database password.
    /// </para>
    /// <para>
    /// The vault URI and the secret name are published, not the secret. Fetching at runtime means a rotated
    /// secret takes effect without redeploying, and nothing sensitive is written into a deployment artifact.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithKeyVaultReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AzureKeyVaultResource> vault,
        string secretName,
        string environmentVariable)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariable);

        builder.WithRoleAssignments(vault, KeyVaultBuiltInRole.KeyVaultSecretsUser);

        return builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables["AZURE_KEYVAULT_URI"] = vault.Resource.VaultUri;
            context.EnvironmentVariables[environmentVariable] = secretName;
        });
    }

    /// <summary>
    /// Lets the application read and write a blob container with its managed identity.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="storage">The storage account.</param>
    /// <param name="containerName">The blob container uploads live in.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// The better answer for uploads than a mounted volume. Azure Files, which is what a volume becomes,
    /// is SMB: one fixed owner for the whole mount, no POSIX ownership, and latency that makes it a poor
    /// place for anything read on every request. Laravel, Drupal and WordPress all have well-trodden blob
    /// adapters, and a blob container survives the container being replaced without any of that.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithBlobStorageReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<AzureStorageResource> storage,
        string containerName)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        builder.WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataContributor);

        return builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables["AZURE_STORAGE_BLOB_ENDPOINT"] = storage.Resource.BlobEndpoint;
            context.EnvironmentVariables["AZURE_STORAGE_CONTAINER"] = containerName;
        });
    }

    /// <summary>
    /// Applies the Container Apps shape a console command's kind implies.
    /// </summary>
    private static void ShapeConsoleResource(
        IDistributedApplicationBuilder applicationBuilder,
        IResource resource,
        PhpConsoleKindAnnotation kind)
    {
        // PublishAsDockerFile has already substituted a container for the executable by now, so the resource
        // is a compute resource either way. The job APIs want IComputeResource; the app API wants the
        // concrete container, because its overloads are split by resource shape.
        if (resource is not IComputeResource compute)
        {
            return;
        }

        var jobBuilder = applicationBuilder.CreateResourceBuilder(compute);

        switch (kind.Kind)
        {
            case PhpConsoleCommandKind.OneShot:
                jobBuilder.PublishAsAzureContainerAppJob((_, job) =>
                {
                    job.Configuration.TriggerType = ContainerAppJobTriggerType.Manual;

                    // Migrations are retried because the usual failure is the database not being reachable
                    // yet, which fixes itself. Thirty minutes is long enough for a real migration on a large
                    // table and short enough that a hung one is noticed.
                    job.Configuration.ReplicaRetryLimit = 3;
                    job.Configuration.ReplicaTimeout = 1800;
                });
                break;

            case PhpConsoleCommandKind.Scheduled:
                jobBuilder.PublishAsScheduledAzureContainerAppJob(
                    kind.CronExpression ?? "* * * * *",
                    (_, job) =>
                    {
                        // One at a time. A scheduler that overlaps with itself runs the same due tasks twice,
                        // and PHP schedulers do not expect to be running concurrently.
                        job.Configuration.ScheduleTriggerConfig.Parallelism = 1;
                        job.Configuration.ScheduleTriggerConfig.ReplicaCompletionCount = 1;

                        // Must finish inside its own tick, or executions pile up.
                        job.Configuration.ReplicaTimeout = 600;
                    });
                break;

            case PhpConsoleCommandKind.LongRunning:
                if (resource is ContainerResource container)
                {
                    applicationBuilder.CreateResourceBuilder(container).PublishAsAzureContainerApp((_, app) =>
                    {
                        // A queue worker that scales to zero stops consuming the queue, and nothing arrives
                        // over HTTP to wake it up again, so it never scales back. It has no ingress by
                        // construction: a console command declares no endpoint.
                        app.Template.Scale.MinReplicas = 1;
                    });
                }

                break;
        }
    }

    private static bool IsChildOf(IResource resource, string parentName)
        => resource.Annotations
            .OfType<ResourceRelationshipAnnotation>()
            .Any(relationship => relationship.Resource.Name == parentName);
}
