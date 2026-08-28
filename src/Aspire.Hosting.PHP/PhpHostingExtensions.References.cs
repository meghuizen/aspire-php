using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Points a PHP application at a database, in the environment variables it actually reads.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="database">The database resource, for example one returned by <c>AddDatabase</c>.</param>
    /// <param name="convention">
    /// Which names to use. Defaults to the convention the application was created with —
    /// <c>AddLaravelApp</c> gives Laravel's names, and so on.
    /// </param>
    /// <param name="driver">The PHP driver. Worked out from the resource when left as <see cref="PhpDatabaseDriver.Auto"/>.</param>
    /// <param name="prefix">
    /// Overrides the environment variable prefix, for a second database or an application that renamed them.
    /// For example <c>"DB_REPORTING"</c> yields <c>DB_REPORTING_HOST</c>.
    /// </param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Aspire's own <c>WithReference</c> injects an ADO.NET connection string, which no PHP application reads.
    /// This translates the same reference into the names the target expects, and installs the matching PDO
    /// extension into the image.
    /// </para>
    /// <para>
    /// The application still waits for the database to be ready — call <c>WaitFor</c> as usual.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var db = builder.AddMySql("mysql").AddDatabase("shopdb");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithDatabaseReference(db)
    ///        .WaitFor(db);
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithDatabaseReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> database,
        PhpConnectionConvention convention = PhpConnectionConvention.Auto,
        PhpDatabaseDriver driver = PhpDatabaseDriver.Auto,
        string? prefix = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(database);

        var databaseResource = database.Resource;
        var resolvedDriver = driver == PhpDatabaseDriver.Auto
            ? PhpConnectionMapper.DetectDriver(databaseResource)
            : driver;

        // The application cannot talk to the database without the driver, and which one it needs is knowable
        // from the reference itself, so installing it is not a decision worth making the caller repeat.
        if (PhpConnectionMapper.ExtensionForDriver(resolvedDriver) is { } extension)
        {
            builder.WithPhpExtension(extension);
        }

        return builder.WithEnvironment(context =>
        {
            var properties = PhpConnectionMapper.ReadProperties(databaseResource);

            if (properties.Count == 0)
            {
                throw new DistributedApplicationException(
                    $"The PHP app '{builder.Resource.Name}' references '{databaseResource.Name}', which does not " +
                    "expose named connection properties. Reference a resource created by an Aspire database " +
                    "integration, or set the environment variables yourself with WithEnvironment.");
            }

            PhpConnectionMapper.ApplyDatabase(
                properties,
                ResolveConvention(builder.Resource, convention),
                resolvedDriver,
                prefix,
                context.EnvironmentVariables);
        });
    }

    /// <summary>
    /// Points a PHP application at a cache, in the environment variables it actually reads.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="cache">The cache resource, for example one returned by <c>AddRedis</c>.</param>
    /// <param name="convention">
    /// Which names to use. Defaults to the convention the application was created with.
    /// </param>
    /// <param name="prefix">
    /// Overrides the environment variable prefix, for a second cache or an application that renamed them.
    /// </param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Installs the <c>redis</c> PHP extension, which is the C client the frameworks are configured to use here.
    /// A password is only set when the cache has one, rather than being set to an empty string, because some
    /// clients treat an empty password as "authenticate with an empty password" and fail.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var cache = builder.AddRedis("cache");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithCacheReference(cache)
    ///        .WaitFor(cache);
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithCacheReference<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> cache,
        PhpConnectionConvention convention = PhpConnectionConvention.Auto,
        string? prefix = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cache);

        var cacheResource = cache.Resource;

        builder.WithPhpExtension("redis");

        return builder.WithEnvironment(context =>
        {
            var properties = PhpConnectionMapper.ReadProperties(cacheResource);

            if (properties.Count == 0)
            {
                throw new DistributedApplicationException(
                    $"The PHP app '{builder.Resource.Name}' references '{cacheResource.Name}', which does not " +
                    "expose named connection properties. Reference a resource created by an Aspire cache " +
                    "integration, or set the environment variables yourself with WithEnvironment.");
            }

            PhpConnectionMapper.ApplyCache(
                properties,
                ResolveConvention(builder.Resource, convention),
                prefix,
                context.EnvironmentVariables);
        });
    }

    /// <summary>
    /// Sets the naming convention a PHP application's references are translated into.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="convention">The convention.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Only needed for an application built with <c>AddPhpWebApp</c> that nonetheless follows a framework's
    /// naming. The <c>AddLaravelApp</c> family sets this already.
    /// </remarks>
    public static IResourceBuilder<T> WithConnectionConvention<T>(
        this IResourceBuilder<T> builder,
        PhpConnectionConvention convention)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnnotation(
            new PhpConnectionConventionAnnotation(convention),
            ResourceAnnotationMutationBehavior.Replace);
    }

    // Auto means "whatever this application was created as", which is carried on an annotation rather than
    // captured when the reference is added, so the order of the fluent calls does not matter.
    private static PhpConnectionConvention ResolveConvention(IPhpResource resource, PhpConnectionConvention requested)
    {
        if (requested != PhpConnectionConvention.Auto)
        {
            return requested;
        }

        return resource.TryGetLastAnnotation<PhpConnectionConventionAnnotation>(out var annotation)
            ? annotation.Convention
            : PhpConnectionConvention.Generic;
    }
}
