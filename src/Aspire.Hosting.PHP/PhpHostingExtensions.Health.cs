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

        var resolvedPath = path ?? DefaultHealthCheckPath;

        // A dashboard health check on its own stops at the dashboard. Probes are the separate API that
        // deployment targets read, and are translated into Container Apps probes, Kubernetes probes and
        // Compose health checks. Registering only the check leaves every target knowing nothing.
        //
        // Suppressed at the call site rather than project-wide, so that when the probe API changes this stops
        // compiling instead of quietly doing nothing.
#pragma warning disable ASPIREPROBES001
        // Readiness goes through WithHttpProbe because that also registers the dashboard health check, which
        // is what WaitFor keys off. Readiness is the right one to tie it to: it is the probe that answers
        // "can this take traffic", which is exactly what a dependent is waiting to know.
        builder.WithHttpProbe(
            ProbeType.Readiness,
            resolvedPath,
            periodSeconds: 5,
            timeoutSeconds: 3,
            endpointName: "http");

        // The other two are added as annotations directly. WithHttpProbe would register a second health check
        // under the same key -- endpoint, path and status code -- and Aspire rejects the duplicate.
        var endpoint = builder.Resource.GetEndpoint("http");

        builder
            // Slack on purpose: an image with OPcache preloading and a large autoloader can take tens of
            // seconds to answer its first request, and a tight startup probe kills the container before it
            // ever finishes booting.
            .WithAnnotation(new EndpointProbeAnnotation
            {
                Type = ProbeType.Startup,
                EndpointReference = endpoint,
                Path = resolvedPath,
                InitialDelaySeconds = 5,
                PeriodSeconds = 3,
                FailureThreshold = 20
            })
            // Slack for a different reason: PHP-FPM with every worker busy is slow to answer without being
            // dead, and restarting it under load makes the overload worse rather than better.
            .WithAnnotation(new EndpointProbeAnnotation
            {
                Type = ProbeType.Liveness,
                EndpointReference = endpoint,
                Path = resolvedPath,
                PeriodSeconds = 30,
                FailureThreshold = 3
            });
#pragma warning restore ASPIREPROBES001

        return builder;
    }
}
