using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// Turns an Aspire resource reference into the environment variables a PHP application actually reads.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="IResourceWithConnectionString.GetConnectionProperties"/>, which every first-party Aspire
/// database and cache resource implements. That yields named parts — <c>Host</c>, <c>Port</c>, <c>Username</c>,
/// <c>Password</c>, <c>DatabaseName</c>, <c>Uri</c> — as expressions rather than resolved values.
/// </para>
/// <para>
/// Working from expressions rather than parsing the connection string matters for publishing: the values stay
/// unresolved placeholders in the generated compose file, so passwords are never baked into it.
/// </para>
/// </remarks>
internal static class PhpConnectionMapper
{
    // Property names as yielded by GetConnectionProperties.
    private const string HostProperty = "Host";
    private const string PortProperty = "Port";
    private const string UsernameProperty = "Username";
    private const string PasswordProperty = "Password";
    private const string DatabaseNameProperty = "DatabaseName";
    private const string UriProperty = "Uri";

    /// <summary>
    /// Reads the named connection parts off a resource.
    /// </summary>
    public static IReadOnlyDictionary<string, ReferenceExpression> ReadProperties(IResourceWithConnectionString resource)
    {
        var properties = new Dictionary<string, ReferenceExpression>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in resource.GetConnectionProperties())
        {
            // Last wins: a database resource combines its parent's properties with its own overrides.
            properties[property.Key] = property.Value;
        }

        return properties;
    }

    /// <summary>
    /// Writes the database environment variables for a convention.
    /// </summary>
    public static void ApplyDatabase(
        IReadOnlyDictionary<string, ReferenceExpression> properties,
        PhpConnectionConvention convention,
        PhpDatabaseDriver driver,
        string? prefix,
        IDictionary<string, object> environment)
    {
        var host = properties.GetValueOrDefault(HostProperty);
        var port = properties.GetValueOrDefault(PortProperty);
        var username = properties.GetValueOrDefault(UsernameProperty);
        var password = properties.GetValueOrDefault(PasswordProperty);
        var databaseName = properties.GetValueOrDefault(DatabaseNameProperty);
        var uri = properties.GetValueOrDefault(UriProperty);

        switch (convention)
        {
            case PhpConnectionConvention.Symfony:
                // Symfony reads one DSN rather than parts, and the resource already builds a correct one.
                Set(environment, prefix ?? "DATABASE_URL", uri);
                break;

            case PhpConnectionConvention.WordPress:
                // WordPress takes host and port as a single value, not two.
                Set(environment, "WORDPRESS_DB_HOST", CombineHostAndPort(host, port));
                Set(environment, "WORDPRESS_DB_USER", username);
                Set(environment, "WORDPRESS_DB_PASSWORD", password);
                Set(environment, "WORDPRESS_DB_NAME", databaseName);
                break;

            case PhpConnectionConvention.Joomla:
                Set(environment, "JOOMLA_DB_HOST", CombineHostAndPort(host, port));
                Set(environment, "JOOMLA_DB_USER", username);
                Set(environment, "JOOMLA_DB_PASSWORD", password);
                Set(environment, "JOOMLA_DB_NAME", databaseName);
                Set(environment, "JOOMLA_DB_TYPE", DriverName(driver, PhpConnectionConvention.Joomla));
                break;

            case PhpConnectionConvention.Drupal:
                Set(environment, "DRUPAL_DATABASE_HOST", host);
                Set(environment, "DRUPAL_DATABASE_PORT", port);
                Set(environment, "DRUPAL_DATABASE_NAME", databaseName);
                Set(environment, "DRUPAL_DATABASE_USERNAME", username);
                Set(environment, "DRUPAL_DATABASE_PASSWORD", password);
                Set(environment, "DRUPAL_DATABASE_DRIVER", DriverName(driver, PhpConnectionConvention.Drupal));
                // Also supplied because many settings.php variants read a DSN instead of the parts.
                Set(environment, "DATABASE_URL", uri);
                break;

            case PhpConnectionConvention.Laravel:
            case PhpConnectionConvention.Generic:
            default:
                var databasePrefix = prefix ?? "DB";
                Set(environment, $"{databasePrefix}_CONNECTION", DriverName(driver, convention));
                Set(environment, $"{databasePrefix}_HOST", host);
                Set(environment, $"{databasePrefix}_PORT", port);
                Set(environment, $"{databasePrefix}_DATABASE", databaseName);
                Set(environment, $"{databasePrefix}_USERNAME", username);
                Set(environment, $"{databasePrefix}_PASSWORD", password);

                if (convention == PhpConnectionConvention.Generic)
                {
                    // Laravel ignores DATABASE_URL when the parts are present, and setting both invites
                    // confusion about which one won, so it is only added for the generic shape.
                    Set(environment, "DATABASE_URL", uri);
                }

                break;
        }
    }

    /// <summary>
    /// Writes the cache environment variables for a convention.
    /// </summary>
    public static void ApplyCache(
        IReadOnlyDictionary<string, ReferenceExpression> properties,
        PhpConnectionConvention convention,
        string? prefix,
        IDictionary<string, object> environment)
    {
        var host = properties.GetValueOrDefault(HostProperty);
        var port = properties.GetValueOrDefault(PortProperty);
        var password = properties.GetValueOrDefault(PasswordProperty);
        var uri = properties.GetValueOrDefault(UriProperty);

        // Aspire turns on Redis TLS by default while running, which makes the scheme rediss rather than
        // redis. A client given only a host and port connects in plaintext and fails with a read error that
        // says nothing about TLS, so the URI is always supplied: it is the only value carrying the scheme.
        // Whether TLS is on is decided at runtime, so it cannot be branched on while building the model.
        if (convention != PhpConnectionConvention.Symfony)
        {
            Set(environment, "REDIS_URL", uri);
        }

        switch (convention)
        {
            case PhpConnectionConvention.Symfony:
                Set(environment, prefix ?? "REDIS_URL", uri);
                break;

            case PhpConnectionConvention.Laravel:
                var cachePrefix = prefix ?? "REDIS";
                Set(environment, $"{cachePrefix}_HOST", host);
                Set(environment, $"{cachePrefix}_PORT", port);
                Set(environment, $"{cachePrefix}_PASSWORD", password);

                // Laravel defaults to the predis client, which is a Composer package. phpredis is the C
                // extension, which WithPhpExtension installs, so it is named explicitly.
                Set(environment, $"{cachePrefix}_CLIENT", "phpredis");
                break;

            case PhpConnectionConvention.WordPress:
                // The Redis Object Cache plugin's names.
                Set(environment, "WP_REDIS_HOST", host);
                Set(environment, "WP_REDIS_PORT", port);
                Set(environment, "WP_REDIS_PASSWORD", password);
                break;

            case PhpConnectionConvention.Drupal:
                Set(environment, "DRUPAL_REDIS_HOST", host);
                Set(environment, "DRUPAL_REDIS_PORT", port);
                Set(environment, "DRUPAL_REDIS_PASSWORD", password);
                break;

            case PhpConnectionConvention.Joomla:
            case PhpConnectionConvention.Generic:
            default:
                var genericPrefix = prefix ?? "REDIS";
                Set(environment, $"{genericPrefix}_HOST", host);
                Set(environment, $"{genericPrefix}_PORT", port);
                Set(environment, $"{genericPrefix}_PASSWORD", password);
                break;
        }
    }

    /// <summary>
    /// Builds the <c>session.save_path</c> the phpredis session handler expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler parses this itself rather than taking a URL, and its grammar is not a URL's: the password
    /// arrives as an <c>auth</c> query parameter, not as userinfo before the host.
    /// </para>
    /// <para>
    /// The connection's own URI carries the scheme, which is the only place TLS is visible, and Aspire turns
    /// Redis TLS on by default. So the URI is used when there is one and the host and port are only a
    /// fallback for a resource that does not publish it.
    /// </para>
    /// </remarks>
    public static ReferenceExpression? BuildSessionSavePath(
        IReadOnlyDictionary<string, ReferenceExpression> properties)
    {
        var password = properties.GetValueOrDefault(PasswordProperty);

        if (properties.GetValueOrDefault(UriProperty) is { } uri)
        {
            return password is null
                ? uri
                : ReferenceExpression.Create($"{uri}?auth={password}");
        }

        var host = properties.GetValueOrDefault(HostProperty);
        var port = properties.GetValueOrDefault(PortProperty);

        if (host is null)
        {
            return null;
        }

        var authority = port is null
            ? ReferenceExpression.Create($"tcp://{host}")
            : ReferenceExpression.Create($"tcp://{host}:{port}");

        return password is null
            ? authority
            : ReferenceExpression.Create($"{authority}?auth={password}");
    }

    /// <summary>
    /// Works out which driver a resource represents.
    /// </summary>
    /// <remarks>
    /// Decided from the resource type name rather than a package reference, so this integration does not have
    /// to depend on every database integration it can talk to — including ones that do not exist yet.
    /// </remarks>
    public static PhpDatabaseDriver DetectDriver(IResource resource)
    {
        for (var type = resource.GetType(); type is not null; type = type.BaseType)
        {
            var name = type.Name;

            if (name.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                || name.Contains("MariaDb", StringComparison.OrdinalIgnoreCase))
            {
                return PhpDatabaseDriver.MySql;
            }

            if (name.Contains("Postgres", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                return PhpDatabaseDriver.PostgreSql;
            }

            if (name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return PhpDatabaseDriver.SqlServer;
            }

            if (name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return PhpDatabaseDriver.Sqlite;
            }
        }

        return PhpDatabaseDriver.Auto;
    }

    /// <summary>
    /// The PHP extension a driver needs, so it can be installed into the image automatically.
    /// </summary>
    public static string? ExtensionForDriver(PhpDatabaseDriver driver) => driver switch
    {
        PhpDatabaseDriver.MySql => "pdo_mysql",
        PhpDatabaseDriver.PostgreSql => "pdo_pgsql",
        PhpDatabaseDriver.SqlServer => "pdo_sqlsrv",
        PhpDatabaseDriver.Sqlite => "pdo_sqlite",
        _ => null
    };

    /// <summary>
    /// The driver name as the target application spells it.
    /// </summary>
    /// <remarks>
    /// The spellings genuinely differ: Laravel calls PostgreSQL <c>pgsql</c>, Drupal calls it <c>pgsql</c> too
    /// but MySQL <c>mysql</c>, and Joomla uses <c>mysqli</c> rather than <c>mysql</c>.
    /// </remarks>
    private static string? DriverName(PhpDatabaseDriver driver, PhpConnectionConvention convention) => driver switch
    {
        PhpDatabaseDriver.MySql => convention == PhpConnectionConvention.Joomla ? "mysqli" : "mysql",
        PhpDatabaseDriver.PostgreSql => "pgsql",
        PhpDatabaseDriver.SqlServer => "sqlsrv",
        PhpDatabaseDriver.Sqlite => "sqlite",
        _ => null
    };

    // WordPress and Joomla both take a single host value and split the port off themselves.
    private static ReferenceExpression? CombineHostAndPort(ReferenceExpression? host, ReferenceExpression? port)
    {
        if (host is null)
        {
            return null;
        }

        return port is null
            ? host
            : ReferenceExpression.Create($"{host}:{port}");
    }

    private static void Set(IDictionary<string, object> environment, string key, ReferenceExpression? value)
    {
        // A resource that does not expose a part (Redis without a password, for example) should leave the
        // variable unset rather than set it to an empty string, which some applications treat as configured.
        if (value is not null)
        {
            environment[key] = value;
        }
    }

    private static void Set(IDictionary<string, object> environment, string key, string? value)
    {
        if (value is not null)
        {
            environment[key] = value;
        }
    }
}
