namespace Aspire.Hosting.PHP;

/// <summary>
/// Default container images and the in-container paths they use.
/// </summary>
/// <remarks>
/// These are the serversideup PHP images. They were chosen because they run as an unprivileged user out of the
/// box, pin real 8.5.x releases, and ship both <c>composer</c> and <c>install-php-extensions</c>, which is what
/// makes <c>WithPhpExtension</c> and PHP-level OpenTelemetry one-liners.
/// Override either image with <c>WithDockerfileBaseImage</c>.
/// </remarks>
internal static class PhpImages
{
    /// <summary>Default image for worker and CLI applications.</summary>
    public const string DefaultCliImage = "docker.io/serversideup/php:8.5-cli-alpine";

    /// <summary>Default image for web applications. FrankenPHP is Caddy with PHP compiled in.</summary>
    public const string DefaultWebImage = "docker.io/serversideup/php:8.5-frankenphp-alpine";

    /// <summary>The image tag family, used when a specific PHP version is pinned.</summary>
    public const string CliImageTemplate = "docker.io/serversideup/php:{0}-cli-alpine";

    /// <summary>The image tag family for web applications, used when a specific PHP version is pinned.</summary>
    public const string WebImageTemplate = "docker.io/serversideup/php:{0}-frankenphp-alpine";

    /// <summary>Where the serversideup images expect the application to live.</summary>
    public const string AppBaseDirectory = "/var/www/html";

    /// <summary>Where PHP scans for additional ini files.</summary>
    public const string PhpConfDirectory = "/usr/local/etc/php/conf.d";

    /// <summary>
    /// Name of the ini file this integration generates. The four z prefix sorts after the images' own
    /// <c>zzz-serversideup-docker-php-debug.ini</c>, so settings set here win.
    /// </summary>
    public const string GeneratedIniFileName = "zzzz-aspire.ini";

    /// <summary>The unprivileged user the serversideup images run as.</summary>
    public const string ContainerUser = "www-data";

    /// <summary>Default image for nginx web applications.</summary>
    public const string DefaultFpmNginxImage = "docker.io/serversideup/php:8.5-fpm-nginx-alpine";

    /// <summary>The nginx image tag family, used when a specific PHP version is pinned.</summary>
    public const string FpmNginxImageTemplate = "docker.io/serversideup/php:{0}-fpm-nginx-alpine";

    /// <summary>Default image for Apache web applications. Debian: no Alpine variant of this image exists.</summary>
    public const string DefaultApacheImage = "docker.io/serversideup/php:8.5-fpm-apache";

    /// <summary>The Apache image tag family, used when a specific PHP version is pinned.</summary>
    public const string ApacheImageTemplate = "docker.io/serversideup/php:{0}-fpm-apache";

    /// <summary>The port FrankenPHP listens on inside the container by default.</summary>
    public const int DefaultWebContainerPort = 8080;
}
