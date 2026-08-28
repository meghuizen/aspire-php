using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Tells the application it is served through a reverse proxy that terminates TLS.
    /// </summary>
    /// <typeparam name="T">The PHP web resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="proxies">
    /// Which proxies to trust. Defaults to all of them, because a deployed container is not reachable except
    /// through the platform's own ingress. Pass an empty string to opt out entirely.
    /// </param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Applied automatically when publishing a web application, because every deployment target this package
    /// supports puts one in front: Container Apps, Kubernetes ingress and a Compose reverse proxy all
    /// terminate TLS at the edge and forward plain HTTP.
    /// </para>
    /// <para>
    /// Without it PHP sees an unset <c>HTTPS</c> and builds <c>http://</c> URLs. That produces mixed-content
    /// warnings on every asset, and a redirect loop wherever the framework redirects to its canonical HTTP
    /// URL and the platform redirects that straight back to HTTPS.
    /// </para>
    /// <para>
    /// Laravel and Symfony read an environment variable for this. WordPress, Joomla and Drupal do not — they
    /// read <c>$_SERVER['HTTPS']</c> directly — so for those the generated image also gets a small
    /// <c>auto_prepend_file</c> that populates the <c>$_SERVER</c> keys from the forwarded headers before any
    /// application code runs.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// // Trust only the platform's own ingress range rather than everything.
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithTrustedProxies("10.0.0.0/8");
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithTrustedProxies<T>(
        this IResourceBuilder<T> builder,
        string? proxies = null)
        where T : IPhpWebResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnnotation(
            new PhpTrustedProxyAnnotation(proxies ?? DefaultTrustedProxies),
            ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>
    /// Trust every proxy. Correct here because the container has no route in except the platform's ingress.
    /// </summary>
    internal const string DefaultTrustedProxies = "*";

    /// <summary>The file the generated image writes the <c>$_SERVER</c> shim to.</summary>
    internal const string ForwardedHeaderShimPath = $"{PhpImages.PhpConfDirectory}/aspire-forwarded-headers.php";

    /// <summary>
    /// Applies proxy awareness when publishing, unless the caller opted out.
    /// </summary>
    internal static void ConfigureTrustedProxies(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<PhpAppResource> resourceBuilder,
        bool isWeb)
    {
        if (!isWeb || !builder.ExecutionContext.IsPublishMode)
        {
            return;
        }

        resourceBuilder.WithEnvironment(context =>
        {
            var resource = resourceBuilder.Resource;

            var proxies = resource.TryGetLastAnnotation<PhpTrustedProxyAnnotation>(out var annotation)
                ? annotation.Proxies
                : DefaultTrustedProxies;

            if (proxies.Length == 0)
            {
                return;
            }

            ApplyTrustedProxies(ResolveConvention(resource, PhpConnectionConvention.Auto), proxies, context.EnvironmentVariables);
        });
    }

    /// <summary>
    /// Writes the trusted proxy configuration in the names the framework reads.
    /// </summary>
    internal static void ApplyTrustedProxies(
        PhpConnectionConvention convention,
        string proxies,
        IDictionary<string, object> environment)
    {
        switch (convention)
        {
            case PhpConnectionConvention.Symfony:
                // Symfony wants the proxy list and the headers named separately, and REMOTE_ADDR is its own
                // idiom for "whatever is directly in front of me", which is exactly the platform's ingress.
                environment["TRUSTED_PROXIES"] = proxies == DefaultTrustedProxies ? "REMOTE_ADDR" : proxies;
                environment["TRUSTED_HEADERS"] = "x-forwarded-for,x-forwarded-proto,x-forwarded-port,x-forwarded-host";
                break;

            case PhpConnectionConvention.Laravel:
            case PhpConnectionConvention.Generic:
                environment["TRUSTED_PROXIES"] = proxies;
                break;

            case PhpConnectionConvention.WordPress:
            case PhpConnectionConvention.Joomla:
            case PhpConnectionConvention.Drupal:
                // No environment variable exists for these: they read $_SERVER directly. The prepend file
                // does the work; this only records that it should be written.
                break;
        }
    }

    /// <summary>
    /// Whether this convention needs the <c>$_SERVER</c> shim rather than an environment variable.
    /// </summary>
    internal static bool NeedsForwardedHeaderShim(PhpConnectionConvention convention)
        => convention is PhpConnectionConvention.WordPress
            or PhpConnectionConvention.Joomla
            or PhpConnectionConvention.Drupal;

    /// <summary>
    /// The prepend file contents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately tiny and dependency-free: it runs before every request, including ones that go on to
    /// fail, so it must not be able to throw.
    /// </para>
    /// <para>
    /// It only ever promotes a request to HTTPS, never demotes one, because the forwarded headers can only be
    /// set by the proxy in front — and if something else set them, believing "this was HTTPS" is the safe
    /// direction to be wrong in.
    /// </para>
    /// </remarks>
    internal const string ForwardedHeaderShimContent = """
        <?php
        // Generated by Aspire.Hosting.PHP. The platform terminates TLS at its ingress and forwards plain
        // HTTP, so PHP would otherwise report an unencrypted request and build http:// URLs.
        if (isset($_SERVER['HTTP_X_FORWARDED_PROTO'])
            && strtolower(explode(',', $_SERVER['HTTP_X_FORWARDED_PROTO'])[0]) === 'https') {
            $_SERVER['HTTPS'] = 'on';
            $_SERVER['SERVER_PORT'] = 443;
            $_SERVER['REQUEST_SCHEME'] = 'https';
        }
        if (isset($_SERVER['HTTP_X_FORWARDED_HOST'])) {
            $_SERVER['HTTP_HOST'] = explode(',', $_SERVER['HTTP_X_FORWARDED_HOST'])[0];
        }
        if (isset($_SERVER['HTTP_X_FORWARDED_FOR'])) {
            $_SERVER['REMOTE_ADDR'] = trim(explode(',', $_SERVER['HTTP_X_FORWARDED_FOR'])[0]);
        }
        """;
}
