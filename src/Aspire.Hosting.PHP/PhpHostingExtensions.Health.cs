using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>The path the base images answer health checks on.</summary>
    private const string DefaultHealthCheckPath = "/healthcheck";

    /// <summary>
    /// Marks a PHP web application unhealthy until it answers over HTTP.
    /// </summary>
    /// <typeparam name="T">The PHP web resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="path">
    /// The path to request. Defaults to <c>/healthcheck</c>, which the base images answer with 200 without
    /// involving PHP at all.
    /// </param>
    /// <param name="statusCode">The status code that counts as healthy. Defaults to 200.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Without this a resource reports running as soon as its process starts, which for a web application says
    /// almost nothing — the server can be up while the application fails on every request. It also makes
    /// <c>WaitFor</c> meaningful: dependents wait for the application to actually answer.
    /// </para>
    /// <para>
    /// The default path is answered by the web server itself, so it stays green even if PHP is broken. Point it
    /// at a route of your own to check the application instead: Laravel's <c>/up</c>, for example.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var shop = builder.AddLaravelApp("shop", "../shop")
    ///                   .WithHealthCheck("/up");
    ///
    /// builder.AddProject&lt;Projects.Api&gt;("api").WaitFor(shop);
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithHealthCheck<T>(
        this IResourceBuilder<T> builder,
        string? path = null,
        int? statusCode = null)
        where T : IPhpWebResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithHttpHealthCheck(path ?? DefaultHealthCheckPath, statusCode, endpointName: "http");
    }
}
