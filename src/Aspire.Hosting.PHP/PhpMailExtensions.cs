using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

/// <summary>
/// Gives PHP applications SMTP mail delivery.
/// </summary>
/// <remarks>
/// The SMTP server itself is not this package's concern. Point these at any resource that exposes an SMTP
/// endpoint — <c>CommunityToolkit.Aspire.Hosting.MailPit</c> for development, or a real server in production
/// through <c>WithSmtp</c>.
/// </remarks>
public static class PhpMailExtensions
{
    /// <summary>The endpoint name SMTP resources conventionally use, including MailPit's.</summary>
    public const string DefaultSmtpEndpointName = "smtp";

    // Names the generated sendmail_path reads. They are deliberately the Laravel ones, because that is the
    // most widely recognised spelling and the framework variables have to be set anyway.
    private const string HostVariable = "MAIL_HOST";
    private const string PortVariable = "MAIL_PORT";
    private const string UsernameVariable = "MAIL_USERNAME";
    private const string PasswordVariable = "MAIL_PASSWORD";
    private const string FromVariable = "MAIL_FROM_ADDRESS";

    /// <summary>
    /// Points a PHP application's mail at an SMTP server in the app model.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <typeparam name="TSmtp">The SMTP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="smtp">Any resource exposing an SMTP endpoint.</param>
    /// <param name="from">The address messages are sent from when the application does not set one.</param>
    /// <param name="fromName">The display name paired with <paramref name="from"/>.</param>
    /// <param name="endpointName">The endpoint to use. Defaults to <c>smtp</c>, which is the usual name.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Deliberately generic: the SMTP server is not this package's concern. MailPit is the obvious choice for
    /// development, through <c>CommunityToolkit.Aspire.Hosting.MailPit</c>, but anything with an SMTP endpoint
    /// works the same way.
    /// </para>
    /// <para>
    /// Sets the framework's own mail variables, and separately makes PHP's <c>mail()</c> work — see
    /// <see cref="WithSmtp{T}"/> for why the second part is needed.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var mail = builder.AddMailPit("mail");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithMailReference(mail, from: "shop@example.test");
    /// </code>
    /// </example>
    public static IResourceBuilder<T> WithMailReference<T, TSmtp>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<TSmtp> smtp,
        string? from = null,
        string? fromName = null,
        string endpointName = DefaultSmtpEndpointName)
        where T : IPhpResource
        where TSmtp : class, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

        var endpoint = smtp.Resource.GetEndpoint(endpointName);

        ConfigureMail(builder, from, fromName, usesAuthentication: false, encryption: null);

        return builder
            .WaitFor(smtp)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[HostVariable] = endpoint.Property(EndpointProperty.Host);
                context.EnvironmentVariables[PortVariable] = endpoint.Property(EndpointProperty.Port);
            });
    }

    /// <summary>
    /// Points a PHP application's mail at an SMTP server outside the app model.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="host">The SMTP host.</param>
    /// <param name="port">The SMTP port.</param>
    /// <param name="username">The username, when the server requires authentication.</param>
    /// <param name="password">The password. Use a parameter rather than a literal.</param>
    /// <param name="encryption">The encryption to request: <c>tls</c>, <c>ssl</c>, or null for none.</param>
    /// <param name="from">The address messages are sent from when the application does not set one.</param>
    /// <param name="fromName">The display name paired with <paramref name="from"/>.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Two separate things are configured, because PHP applications send mail in two different ways.
    /// </para>
    /// <para>
    /// Frameworks with their own mailer — Laravel, Symfony — speak SMTP themselves and read configuration from
    /// environment variables, so those are set in the convention each expects.
    /// </para>
    /// <para>
    /// Everything else calls PHP's <c>mail()</c>, which on Linux does not speak SMTP at all: it pipes the
    /// message to whatever <c>sendmail_path</c> names. WordPress and Joomla both work this way. So
    /// <c>msmtp</c> is installed and <c>sendmail_path</c> points at it, which makes <c>mail()</c> deliver over
    /// SMTP without the application changing.
    /// </para>
    /// </remarks>
    public static IResourceBuilder<T> WithSmtp<T>(
        this IResourceBuilder<T> builder,
        string host,
        int port,
        string? username = null,
        IResourceBuilder<ParameterResource>? password = null,
        string? encryption = null,
        string? from = null,
        string? fromName = null)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        ConfigureMail(builder, from, fromName, usesAuthentication: username is not null, encryption);

        return builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables[HostVariable] = host;
            context.EnvironmentVariables[PortVariable] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (username is not null)
            {
                context.EnvironmentVariables[UsernameVariable] = username;
            }

            if (password is not null)
            {
                context.EnvironmentVariables[PasswordVariable] = password.Resource;
            }
        });
    }

    private static void ConfigureMail<T>(
        IResourceBuilder<T> builder,
        string? from,
        string? fromName,
        bool usesAuthentication,
        string? encryption)
        where T : IPhpResource
    {
        var annotation = new PhpMailAnnotation
        {
            FromAddress = from,
            FromName = fromName,
            UsesAuthentication = usesAuthentication,
            Encryption = encryption
        };

        builder.WithAnnotation(annotation, ResourceAnnotationMutationBehavior.Replace);

        // mail() pipes to sendmail_path, so something has to be there to pipe to. msmtp is a small SMTP
        // client that speaks the sendmail command line, which is exactly what this needs.
        if (!builder.Resource.TryGetLastAnnotation<PhpSystemPackageAnnotation>(out var packages))
        {
            packages = new PhpSystemPackageAnnotation();
            builder.WithAnnotation(packages);
        }

        packages.Add("msmtp");

        // PHP expands ${VAR} in ini values from the environment when it parses them, so the host and port can
        // be baked into the image while their values stay runtime configuration.
        builder.WithPhpIniSetting("sendmail_path", BuildSendmailPath(annotation));

        builder.WithEnvironment(context =>
        {
            var convention = builder.Resource.TryGetLastAnnotation<PhpConnectionConventionAnnotation>(out var c)
                ? c.Convention
                : PhpConnectionConvention.Generic;

            ApplyMailConvention(context.EnvironmentVariables, convention, annotation);
        });
    }

    /// <summary>
    /// Builds the command <c>mail()</c> pipes messages to.
    /// </summary>
    /// <remarks>
    /// <c>-t</c> tells msmtp to read the recipients out of the message headers, which is what <c>mail()</c>
    /// produces. The host and port are left as ini variable references for PHP to expand.
    /// </remarks>
    private static string BuildSendmailPath(PhpMailAnnotation mail)
    {
        var parts = new List<string>
        {
            "/usr/bin/msmtp",
            $"--host=${{{HostVariable}}}",
            $"--port=${{{PortVariable}}}"
        };

        if (mail.FromAddress is not null)
        {
            parts.Add($"--from=${{{FromVariable}}}");
        }

        if (mail.UsesAuthentication)
        {
            parts.Add("--auth=on");
            parts.Add($"--user=${{{UsernameVariable}}}");

            // Reading the password through a command keeps it out of the process list, where a --password
            // argument would be visible to anything that can run ps.
            parts.Add($"--passwordeval=echo \"${PasswordVariable}\"");
        }
        else
        {
            parts.Add("--auth=off");
        }

        parts.Add(mail.Encryption is null ? "--tls=off" : "--tls=on");
        parts.Add("-t");

        return string.Join(" ", parts);
    }

    private static void ApplyMailConvention(
        IDictionary<string, object> environment,
        PhpConnectionConvention convention,
        PhpMailAnnotation mail)
    {
        switch (convention)
        {
            case PhpConnectionConvention.Symfony:
                // Symfony reads one DSN. The host and port are already in the environment, and Symfony
                // expands them itself when the DSN is resolved from .env.
                environment["MAILER_DSN"] = mail.UsesAuthentication
                    ? $"smtp://%env(MAIL_USERNAME)%:%env(MAIL_PASSWORD)%@%env(MAIL_HOST)%:%env(MAIL_PORT)%"
                    : "smtp://%env(MAIL_HOST)%:%env(MAIL_PORT)%";
                break;

            case PhpConnectionConvention.Laravel:
                environment["MAIL_MAILER"] = "smtp";

                // Laravel treats an unset encryption as "no encryption"; the literal string "null" is what
                // its own .env.example uses for that.
                environment["MAIL_ENCRYPTION"] = mail.Encryption ?? "null";
                break;

            case PhpConnectionConvention.WordPress:
            case PhpConnectionConvention.Joomla:
            case PhpConnectionConvention.Drupal:
            case PhpConnectionConvention.Generic:
            default:
                // These send through mail(), which the sendmail_path shim already covers. The variables are
                // still set because SMTP plugins and settings.php commonly read them.
                break;
        }

        if (mail.FromAddress is { } fromAddress)
        {
            environment[FromVariable] = fromAddress;
        }

        if (mail.FromName is { } fromName)
        {
            environment["MAIL_FROM_NAME"] = fromName;
        }
    }
}
