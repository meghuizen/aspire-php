using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// Reads the web server choice off a resource, and knows what each one is configured with.
/// </summary>
internal static class PhpWebServers
{
    /// <summary>
    /// The web server a resource is served by. Defaults to FrankenPHP.
    /// </summary>
    public static PhpWebServer Resolve(IResource resource)
        => resource.TryGetLastAnnotation<PhpWebServerAnnotation>(out var annotation)
            ? annotation.WebServer
            : PhpWebServer.FrankenPhp;

    /// <summary>
    /// The variable naming the port the server listens on inside the container.
    /// </summary>
    /// <remarks>
    /// Each server reads only its own name, and none of them fall back to a shared one, so getting this wrong
    /// produces a container that starts cleanly and then serves nothing.
    /// <para>
    /// <c>NGINX_HTTP_PORT</c> is absent from serversideup's published variable table, but the shipped nginx
    /// configuration does interpolate it, and it is verified to move the listener.
    /// </para>
    /// </remarks>
    public static string PortVariable(PhpWebServer webServer) => webServer switch
    {
        PhpWebServer.Apache => "APACHE_HTTP_PORT",
        PhpWebServer.FpmNginx => "NGINX_HTTP_PORT",
        _ => "CADDY_HTTP_PORT"
    };

    /// <summary>
    /// The variable naming the document root inside the container.
    /// </summary>
    public static string DocumentRootVariable(PhpWebServer webServer) => webServer switch
    {
        PhpWebServer.Apache => "APACHE_DOCUMENT_ROOT",
        PhpWebServer.FpmNginx => "NGINX_WEBROOT",
        _ => "CADDY_SERVER_ROOT"
    };

    /// <summary>
    /// Whether the server can be stood in for by PHP's built-in development server when running locally.
    /// </summary>
    /// <remarks>
    /// Only FrankenPHP can. The built-in server ignores <c>.htaccess</c> and has no FastCGI layer, so
    /// substituting it for Apache or nginx would quietly change how the application behaves.
    /// </remarks>
    public static bool SupportsLocalPhp(PhpWebServer webServer) => webServer == PhpWebServer.FrankenPhp;

    /// <summary>A human-readable name, for error messages.</summary>
    public static string DisplayName(PhpWebServer webServer) => webServer switch
    {
        PhpWebServer.Apache => "Apache",
        PhpWebServer.FpmNginx => "nginx",
        _ => "FrankenPHP"
    };
}
