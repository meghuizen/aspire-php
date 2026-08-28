using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

public static partial class PhpHostingExtensions
{
    /// <summary>
    /// Adds a PHP web application served by Apache with PHP-FPM, honouring <c>.htaccess</c>.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The directory holding the application.</param>
    /// <param name="documentRoot">The document root relative to <paramref name="appDirectory"/>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Shorthand for <c>AddPhpWebApp(..., PhpWebServer.Apache)</c>. See <see cref="PhpWebServer.Apache"/> for
    /// what it costs and when it is worth it.
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddPhpApacheApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string documentRoot = DefaultDocumentRoot)
        => builder.AddPhpWebApp(name, appDirectory, documentRoot, PhpWebServer.Apache);

    /// <summary>
    /// Adds a PHP web application served by nginx with PHP-FPM.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The directory holding the application.</param>
    /// <param name="documentRoot">The document root relative to <paramref name="appDirectory"/>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Shorthand for <c>AddPhpWebApp(..., PhpWebServer.FpmNginx)</c>. The traditional pairing, on
    /// non-thread-safe PHP. See <see cref="PhpWebServer.FpmNginx"/>.
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddPhpNginxApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string documentRoot = DefaultDocumentRoot)
        => builder.AddPhpWebApp(name, appDirectory, documentRoot, PhpWebServer.FpmNginx);
}
