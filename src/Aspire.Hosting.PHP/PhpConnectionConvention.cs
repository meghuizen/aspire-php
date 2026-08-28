namespace Aspire.Hosting.PHP;

/// <summary>
/// Which environment variable names a PHP application expects its connection details in.
/// </summary>
/// <remarks>
/// Aspire hands a resource reference over as an ADO.NET connection string, which no PHP application reads. Each
/// framework and CMS reads something different instead, so a reference has to be translated rather than passed
/// through. This selects the target shape.
/// </remarks>
public enum PhpConnectionConvention
{
    /// <summary>
    /// Use the convention the application was created with — <c>AddLaravelApp</c> gives
    /// <see cref="Laravel"/>, and so on. Falls back to <see cref="Generic"/> for a plain PHP application.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// <c>DB_HOST</c>, <c>DB_PORT</c>, <c>DB_DATABASE</c>, <c>DB_USERNAME</c>, <c>DB_PASSWORD</c>, plus
    /// <c>DATABASE_URL</c>. A reasonable default for an application that reads <c>getenv()</c> directly.
    /// </summary>
    Generic = 1,

    /// <summary>
    /// Laravel's <c>config/database.php</c> names: <c>DB_CONNECTION</c>, <c>DB_HOST</c>, <c>DB_PORT</c>,
    /// <c>DB_DATABASE</c>, <c>DB_USERNAME</c>, <c>DB_PASSWORD</c>, and <c>REDIS_*</c> for cache.
    /// </summary>
    Laravel = 2,

    /// <summary>
    /// Symfony's single-DSN style: <c>DATABASE_URL</c> and <c>REDIS_URL</c>.
    /// </summary>
    Symfony = 3,

    /// <summary>
    /// WordPress's <c>WORDPRESS_DB_*</c> names. Host and port are combined into one <c>host:port</c> value,
    /// which is what WordPress expects.
    /// </summary>
    WordPress = 4,

    /// <summary>
    /// Drupal's <c>DRUPAL_DATABASE_*</c> names, as used by the common Drupal container images, plus
    /// <c>DATABASE_URL</c> for sites whose <c>settings.php</c> reads a DSN.
    /// </summary>
    Drupal = 5,

    /// <summary>
    /// Joomla's <c>JOOMLA_DB_*</c> names, as used by the official Joomla container image.
    /// </summary>
    Joomla = 6
}

/// <summary>
/// The PHP database driver an application should use.
/// </summary>
public enum PhpDatabaseDriver
{
    /// <summary>Work it out from the referenced resource.</summary>
    Auto = 0,

    /// <summary>MySQL or MariaDB.</summary>
    MySql = 1,

    /// <summary>PostgreSQL.</summary>
    PostgreSql = 2,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer = 3,

    /// <summary>SQLite.</summary>
    Sqlite = 4
}
