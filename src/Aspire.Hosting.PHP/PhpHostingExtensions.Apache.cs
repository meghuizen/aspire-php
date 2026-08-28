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
    /// <para>
    /// Choose this when the application genuinely needs <c>.htaccess</c> — a legacy application, or a WordPress
    /// plugin that writes its own rewrite rules. Apache is the only web server here that reads those files;
    /// nginx has no equivalent feature at all, so its rules have to be translated into server configuration
    /// by hand.
    /// </para>
    /// <para>
    /// It costs size. No Alpine variant of the Apache image exists, so this is Debian-based and roughly four
    /// times the compressed size of the default images. Use <c>AddPhpWebApp</c> unless you need the feature.
    /// </para>
    /// <para>
    /// Always runs as a container, in every mode: Apache and PHP-FPM are not something a local <c>php</c>
    /// process can stand in for, and PHP's built-in server ignores <c>.htaccess</c> entirely, so falling back
    /// to it would quietly change the application's behaviour.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// builder.AddPhpApacheApp("legacy", "../legacy-app")
    ///        .WithComposer()
    ///        .WithHealthCheck()
    ///        .WithExternalHttpEndpoints();
    /// </code>
    /// </example>
    public static IResourceBuilder<IPhpWebResource> AddPhpApacheApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string documentRoot = DefaultDocumentRoot)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentRoot);

        // Container in every mode. The annotation has to be attached before anything reads it, so the web
        // server choice is applied to the resource as soon as it is built.
        var app = builder.AddPhpWebApp(name, appDirectory, documentRoot, PhpRunMode.Container);

        return app.WithAnnotation(
            new PhpWebServerAnnotation(PhpWebServer.Apache),
            ResourceAnnotationMutationBehavior.Replace);
    }
}
