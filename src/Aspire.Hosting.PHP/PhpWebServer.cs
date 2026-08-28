namespace Aspire.Hosting.PHP;

/// <summary>
/// The web server a PHP web application is served by.
/// </summary>
public enum PhpWebServer
{
    /// <summary>
    /// FrankenPHP: Caddy with PHP compiled in, as a single long-running process.
    /// </summary>
    /// <remarks>
    /// The default. One process that binds a port, so it maps onto one Aspire endpoint with no sidecar, and it
    /// supports worker mode. Runs thread-safe (ZTS) PHP, which a few extensions do not tolerate.
    /// </remarks>
    FrankenPhp = 0,

    /// <summary>
    /// Apache with PHP-FPM, which is the only option here that honours <c>.htaccess</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only worth choosing when the application genuinely needs <c>.htaccess</c> — a legacy application, or a
    /// WordPress plugin that writes rewrite rules. nginx has no equivalent feature at all, so this is not a
    /// general "not FrankenPHP" escape hatch, it is specifically the Apache one.
    /// </para>
    /// <para>
    /// It costs size: no Alpine variant of the Apache image exists, so it is Debian-based and roughly four
    /// times the compressed size of the Alpine images. It also runs non-thread-safe PHP, which is an advantage
    /// for extensions that dislike ZTS.
    /// </para>
    /// </remarks>
    Apache = 1
}
