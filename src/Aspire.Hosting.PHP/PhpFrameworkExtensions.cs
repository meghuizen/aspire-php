using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

/// <summary>
/// Adds PHP frameworks and content management systems to an <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
/// <remarks>
/// Each of these is <c>AddPhpWebApp</c> with the document root, PHP extensions and connection naming that
/// application expects already set. Everything on a plain PHP resource still applies.
/// </remarks>
public static class PhpFrameworkExtensions
{
    // Extensions every one of these needs beyond what the base images already ship. mbstring and xml are
    // present in the serversideup images; gd, intl and zip are not, and every one of these applications
    // either requires them outright or degrades badly without them.
    private static readonly string[] s_commonWebExtensions = ["gd", "intl", "zip"];

    /// <summary>
    /// Adds a Laravel application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The Laravel project directory, the one holding <c>artisan</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Serves from <c>public/</c>, installs Composer dependencies, and translates database and cache references
    /// into Laravel's <c>DB_*</c> and <c>REDIS_*</c> names.
    /// <para>
    /// Laravel needs an <c>APP_KEY</c>. Generate one with <c>php artisan key:generate</c> and keep it in the
    /// application's <c>.env</c>, or set it with <c>WithEnvironment("APP_KEY", ...)</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var db = builder.AddMySql("mysql").AddDatabase("shopdb");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithDatabaseReference(db)
    ///        .WaitFor(db)
    ///        .WithExternalHttpEndpoints();
    /// </code>
    /// </example>
    public static IResourceBuilder<IPhpWebResource> AddLaravelApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        PhpRunMode runMode = PhpRunMode.Auto)
        => builder.AddPhpWebApp(name, appDirectory, "public", runMode: runMode)
            .WithConnectionConvention(PhpConnectionConvention.Laravel)
            .WithComposer()
            .WithPhpExtension(s_commonWebExtensions)
            // Laravel reads APP_ENV and APP_DEBUG to decide error display and caching. Local while running,
            // production when published, which is what each context wants.
            .WithEnvironment(context =>
            {
                var isPublish = context.ExecutionContext.IsPublishMode;
                context.EnvironmentVariables["APP_ENV"] = isPublish ? "production" : "local";
                context.EnvironmentVariables["APP_DEBUG"] = isPublish ? "false" : "true";
            });

    /// <summary>
    /// Adds a Symfony application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The Symfony project directory, the one holding <c>composer.json</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Serves from <c>public/</c>, installs Composer dependencies, and translates references into Symfony's
    /// single-DSN <c>DATABASE_URL</c> and <c>REDIS_URL</c>.
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddSymfonyApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        PhpRunMode runMode = PhpRunMode.Auto)
        => builder.AddPhpWebApp(name, appDirectory, "public", runMode: runMode)
            .WithConnectionConvention(PhpConnectionConvention.Symfony)
            .WithComposer()
            .WithPhpExtension(s_commonWebExtensions)
            .WithEnvironment(context =>
            {
                var isPublish = context.ExecutionContext.IsPublishMode;
                context.EnvironmentVariables["APP_ENV"] = isPublish ? "prod" : "dev";
                context.EnvironmentVariables["APP_DEBUG"] = isPublish ? "0" : "1";
            });

    /// <summary>
    /// Adds a WordPress site.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The WordPress directory, the one holding <c>wp-load.php</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// WordPress serves from its own root rather than a <c>public/</c> subdirectory, so the document root is
    /// the application directory itself.
    /// </para>
    /// <para>
    /// WordPress is not a Composer application, so no Composer install is configured. Call <c>WithComposer</c>
    /// yourself for a Bedrock-style site.
    /// </para>
    /// <para>
    /// WordPress keeps uploads, plugins and themes on disk under <c>wp-content</c>. Running locally that is
    /// your working copy, but a published container starts empty each time — see <c>WithDataVolume</c>.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddWordPressApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        PhpRunMode runMode = PhpRunMode.Auto)
        => builder.AddPhpWebApp(name, appDirectory, ".", runMode: runMode)
            .WithConnectionConvention(PhpConnectionConvention.WordPress)
            // mysqli rather than only pdo_mysql: WordPress core uses the mysqli API directly.
            .WithPhpExtension([.. s_commonWebExtensions, "mysqli", "exif"]);

    /// <summary>
    /// Adds a Drupal site.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The Drupal project directory, the one holding <c>composer.json</c>.</param>
    /// <param name="documentRoot">The document root. Drupal 8 and later default to <c>web</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Drupal reads its database configuration from <c>settings.php</c> rather than the environment, so the
    /// site has to read the <c>DRUPAL_DATABASE_*</c> variables itself. The common pattern is a block in
    /// <c>settings.php</c> that calls <c>getenv()</c>; see the README.
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddDrupalApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        string documentRoot = "web",
        PhpRunMode runMode = PhpRunMode.Auto)
        => builder.AddPhpWebApp(name, appDirectory, documentRoot, runMode: runMode)
            .WithConnectionConvention(PhpConnectionConvention.Drupal)
            .WithComposer()
            .WithPhpExtension([.. s_commonWebExtensions, "opcache"]);

    /// <summary>
    /// Adds a Joomla site.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="appDirectory">The Joomla directory, the one holding <c>configuration.php</c>.</param>
    /// <param name="runMode">How the resource runs during <c>aspire run</c>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Joomla serves from its own root, so the document root is the application directory itself.
    /// </para>
    /// <para>
    /// Joomla stores its database settings in <c>configuration.php</c>, which is PHP source rather than
    /// configuration the environment can override. The <c>JOOMLA_DB_*</c> variables are set for the installer
    /// and for images that read them, but an already-installed site keeps using its own file.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<IPhpWebResource> AddJoomlaApp(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string appDirectory,
        PhpRunMode runMode = PhpRunMode.Auto)
        => builder.AddPhpWebApp(name, appDirectory, ".", runMode: runMode)
            .WithConnectionConvention(PhpConnectionConvention.Joomla)
            .WithPhpExtension([.. s_commonWebExtensions, "mysqli"]);

    /// <summary>
    /// Keeps a directory of user-uploaded content across container restarts.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="path">The directory to persist, relative to the application directory.</param>
    /// <param name="name">The volume name. Defaults to one derived from the resource name.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Only applies when the resource runs as a container. Running against a local PHP the files are already on
    /// disk in your working copy, so there is nothing to persist and this does nothing.
    /// </para>
    /// <para>
    /// Typical paths: <c>wp-content/uploads</c> for WordPress, <c>sites/default/files</c> for Drupal,
    /// <c>images</c> for Joomla, <c>storage</c> for Laravel.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithDataVolume<T>(
        this IResourceBuilder<T> builder,
        string path,
        string? name = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (builder.Resource is not PhpContainerAppResource containerResource)
        {
            // Running as a local process, the path is a real directory in the working copy already.
            return builder;
        }

        var target = $"{PhpImages.AppBaseDirectory}/{path.Replace('\\', '/').TrimStart('/')}";

        builder.ApplicationBuilder
            .CreateResourceBuilder(containerResource)
            .WithVolume(name ?? $"{builder.Resource.Name}-data", target);

        return builder;
    }
}
