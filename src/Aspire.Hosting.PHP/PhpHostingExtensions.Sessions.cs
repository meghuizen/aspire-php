using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Keeps sessions in a shared cache instead of on the container's local disk.
    /// </summary>
    /// <typeparam name="T">The PHP web resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="cache">The cache resource, for example one returned by <c>AddRedis</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// PHP writes sessions to local files by default. That is correct for exactly one replica and wrong for
    /// any other number: a request routed to a different replica finds no session and the user is logged out,
    /// seemingly at random. Deployment targets scale by default, so this becomes wrong as soon as the
    /// application is deployed.
    /// </para>
    /// <para>
    /// Not applied automatically, even though the failure is near certain. Where sessions live is the
    /// application's decision — one already using a database session driver would be broken by having it
    /// changed underneath. Publishing a web application without either this or an explicit opt-out logs a
    /// warning instead.
    /// </para>
    /// <para>
    /// Installs the <c>redis</c> extension, which is the handler this configures.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var cache = builder.AddRedis("cache");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithCacheReference(cache)
    ///        .WithSessionStore(cache)
    ///        .WaitFor(cache);
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithSessionStore<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> cache)
        where T : IPhpWebResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cache);

        builder.WithPhpExtension(PhpExtensions.Redis);
        builder.WithAnnotation(new PhpSessionStoreAnnotation(), ResourceAnnotationMutationBehavior.Replace);

        // The path is written as a ${VAR} reference that PHP expands when it parses the ini file, and the
        // value arrives in the environment. The alternative -- writing the resolved path into the generated
        // ini file -- would bake the cache password into the image, which is the one place it must not be.
        builder.WithPhpIniSetting("session.save_handler", "redis");
        builder.WithPhpIniSetting("session.save_path", $"${{{SessionSavePathVariable}}}");

        return builder.WithEnvironment(context =>
        {
            var properties = PhpConnectionMapper.ReadProperties(cache.Resource);

            var savePath = PhpConnectionMapper.BuildSessionSavePath(properties)
                ?? throw new DistributedApplicationException(
                    $"The PHP app '{builder.Resource.Name}' stores sessions in '{cache.Resource.Name}', which does " +
                    "not expose a host or a URI. Reference a resource created by an Aspire Redis integration, or " +
                    "set session.save_handler and session.save_path yourself with WithPhpIniSetting.");

            context.EnvironmentVariables[SessionSavePathVariable] = savePath;
        });
    }

    /// <summary>The environment variable the generated <c>session.save_path</c> reads.</summary>
    internal const string SessionSavePathVariable = "PHP_SESSION_SAVE_PATH";

    /// <summary>
    /// Says the application handles its own sessions, silencing the scale-out warning.
    /// </summary>
    /// <typeparam name="T">The PHP web resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// For an application that keeps sessions in its own database, issues stateless tokens, or genuinely runs
    /// as a single replica. The warning exists because the failure it describes looks like an application bug
    /// rather than a deployment one; it is worth being able to turn off honestly.
    /// </remarks>
    public static IResourceBuilder<T> WithoutSharedSessions<T>(this IResourceBuilder<T> builder)
        where T : IPhpWebResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnnotation(
            new PhpSessionStoreAnnotation(),
            ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>
    /// Warns when publishing a web application whose sessions are local files.
    /// </summary>
    internal static void WarnAboutLocalSessions(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<PhpAppResource> resourceBuilder,
        bool isWeb)
    {
        if (!isWeb || !builder.ExecutionContext.IsPublishMode)
        {
            return;
        }

        // Checked from an environment callback rather than a start event. Publishing never starts anything,
        // so a BeforeStart hook would make the warning fire only in the mode where it does not matter.
        resourceBuilder.WithEnvironment(context =>
        {
            var resource = resourceBuilder.Resource;

            if (resource.TryGetLastAnnotation<PhpSessionStoreAnnotation>(out _))
            {
                return;
            }

            context.Logger?.LogWarning(
                "PHP app '{Name}' is being published with sessions on the container's local disk. A deployment " +
                "with more than one replica will log users out at random, because a request routed to another " +
                "replica finds no session. Call WithSessionStore(cache) to share them, or WithoutSharedSessions() " +
                "if the application handles this itself.",
                resource.Name);
        });
    }
}
