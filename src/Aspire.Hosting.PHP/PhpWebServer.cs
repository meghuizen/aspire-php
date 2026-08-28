namespace Aspire.Hosting.PHP;

/// <summary>
/// The web server a PHP web application is served by.
/// </summary>
/// <remarks>
/// Pass one to <c>AddPhpWebApp</c>. They differ in more than name: whether <c>.htaccess</c> is read, whether
/// PHP is built thread-safe, how large the image is, and whether worker mode is available.
/// </remarks>
public enum PhpWebServer
{
    /// <summary>
    /// FrankenPHP: Caddy with PHP compiled into the same binary, as a single process.
    /// </summary>
    /// <remarks>
    /// The default, and the only one here that is genuinely one process — the others run a web server and
    /// PHP-FPM side by side under a supervisor. Supports worker mode. Runs thread-safe (ZTS) PHP, which a few
    /// extensions do not tolerate. Does not read <c>.htaccess</c>.
    /// </remarks>
    FrankenPhp = 0,

    /// <summary>
    /// nginx with PHP-FPM, in one container.
    /// </summary>
    /// <remarks>
    /// The traditional pairing, and the most widely deployed. Runs non-thread-safe PHP, which suits extensions
    /// that dislike ZTS. nginx has no <c>.htaccess</c> equivalent at all, so rewrite rules have to live in
    /// server configuration. No worker mode.
    /// </remarks>
    FpmNginx = 1,

    /// <summary>
    /// Apache with PHP-FPM, which is the only option here that reads <c>.htaccess</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Choose it when the application genuinely needs those files — a legacy application, or a WordPress plugin
    /// that writes its own rewrite rules.
    /// </para>
    /// <para>
    /// It costs size: no Alpine variant of the Apache image exists, so it is Debian-based at roughly four times
    /// the compressed size of the others. Runs non-thread-safe PHP. No worker mode.
    /// </para>
    /// </remarks>
    Apache = 2
}
