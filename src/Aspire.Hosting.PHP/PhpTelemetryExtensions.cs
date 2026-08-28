using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.PHP;

namespace Aspire.Hosting;

/// <summary>
/// Adds an OpenTelemetry Collector for PHP applications to export to.
/// </summary>
public static class PhpTelemetryExtensions
{
    private const string CollectorImage = "docker.io/otel/opentelemetry-collector-contrib";
    private const string CollectorTag = "0.159.0";
    private const string ConfigPathInContainer = "/etc/otelcol-contrib/config.yaml";

    /// <summary>
    /// Adds an OpenTelemetry Collector that PHP applications can export to.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// Worth adding for two reasons. PHP has no background thread, so outside FrankenPHP worker mode every
    /// request pays the span export inline. Exporting to a collector turns that into a local write, and the
    /// collector batches and forwards on its own schedule.
    /// </para>
    /// <para>
    /// It is also the only supported path to Application Insights — see <c>WithApplicationInsights</c>.
    /// </para>
    /// <para>
    /// Everything it receives is forwarded to the Aspire dashboard as well, so traces still appear there.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var collector = builder.AddOpenTelemetryCollector("otel");
    ///
    /// builder.AddLaravelApp("shop", "../shop")
    ///        .WithOpenTelemetry(collector);
    /// </code>
    /// </example>
    public static IResourceBuilder<PhpTelemetryCollectorResource> AddOpenTelemetryCollector(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new PhpTelemetryCollectorResource(name);

        var resourceBuilder = builder.AddResource(resource)
            .WithImage(CollectorImage, CollectorTag)
            .WithIconName("DataUsage")
            .WithHttpEndpoint(
                targetPort: PhpTelemetryCollectorConfig.HttpPort,
                name: PhpTelemetryCollectorResource.HttpEndpointName)
            .WithEndpoint(
                targetPort: PhpTelemetryCollectorConfig.GrpcPort,
                scheme: "http",
                name: PhpTelemetryCollectorResource.GrpcEndpointName)
            // Gives the collector the dashboard's own OTLP endpoint, which the generated configuration then
            // forwards to. Aspire supplies it the same way it does for any other resource.
            .WithOtlpExporter();

        // The configuration depends on whether Application Insights was added, which can happen after this
        // call returns, so it is written at the last moment before start.
        builder.OnBeforeStart((_, _) =>
        {
            WriteConfiguration(builder, resourceBuilder);
            return Task.CompletedTask;
        });

        return resourceBuilder;
    }

    /// <summary>
    /// Forwards everything the collector receives to Application Insights.
    /// </summary>
    /// <param name="builder">The collector resource builder.</param>
    /// <param name="applicationInsights">The Application Insights resource, or any connection string resource.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// <para>
    /// This is the only supported route from PHP to Application Insights. Azure Monitor's OpenTelemetry Distro
    /// covers .NET, Java, Node.js and Python; there is no Application Insights exporter for PHP, so the
    /// translation has to happen in the collector.
    /// </para>
    /// <para>
    /// The connection string is passed as an environment variable and referenced from the generated
    /// configuration, so it is never written into the configuration file itself.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var insights = builder.AddAzureApplicationInsights("insights");
    ///
    /// var collector = builder.AddOpenTelemetryCollector("otel")
    ///                        .WithApplicationInsights(insights);
    /// </code>
    /// </example>
    public static IResourceBuilder<PhpTelemetryCollectorResource> WithApplicationInsights(
        this IResourceBuilder<PhpTelemetryCollectorResource> builder,
        IResourceBuilder<IResourceWithConnectionString> applicationInsights)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(applicationInsights);

        builder.WithAnnotation(new PhpApplicationInsightsAnnotation(), ResourceAnnotationMutationBehavior.Replace);

        return builder.WithEnvironment(
            PhpTelemetryCollectorConfig.ApplicationInsightsVariable,
            applicationInsights);
    }

    /// <summary>
    /// Points a PHP application's telemetry at a collector rather than straight at the dashboard.
    /// </summary>
    /// <typeparam name="T">The PHP resource type.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="collector">The collector to export to.</param>
    /// <returns>A reference to the resource builder.</returns>
    /// <remarks>
    /// Does everything <c>WithOpenTelemetry()</c> does, and additionally overrides the OTLP endpoint. The
    /// application still needs the SDK:
    /// <c>composer require open-telemetry/sdk open-telemetry/exporter-otlp</c>.
    /// </remarks>
    public static IResourceBuilder<T> WithOpenTelemetry<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<PhpTelemetryCollectorResource> collector)
        where T : IPhpResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(collector);

        return builder
            .WithOpenTelemetry()
            .WaitFor(collector)
            .WithEnvironment(context =>
            {
                // Overwrites the dashboard endpoint Aspire injected. http/protobuf is already set by
                // WithOpenTelemetry, and is what the collector's HTTP receiver expects.
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] =
                    collector.Resource.HttpEndpoint;

                // The dashboard's API key is meaningless to the collector, and sending it would be rejected.
                context.EnvironmentVariables.Remove("OTEL_EXPORTER_OTLP_HEADERS");
            });
    }

    private static void WriteConfiguration(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<PhpTelemetryCollectorResource> collector)
    {
        var forwardToApplicationInsights =
            collector.Resource.TryGetLastAnnotation<PhpApplicationInsightsAnnotation>(out _);

        // The dashboard requires this on every OTLP request and answers 401 without it, which surfaces as the
        // collector accepting spans and then dropping them on export.
        var apiKey = builder.Configuration["AppHost:OtlpApiKey"];

        var configuration = PhpTelemetryCollectorConfig.Build(
            forwardToDashboard: true,
            forwardToApplicationInsights,
            // The dashboard presents a development certificate while running; in publish the endpoint is
            // whatever the deployment supplies, and should be verified.
            skipTlsVerification: builder.ExecutionContext.IsRunMode,
            dashboardNeedsApiKey: !string.IsNullOrEmpty(apiKey));

        var directory = Path.Combine(
            Path.GetTempPath(),
            "aspire-php-otel",
            SanitizeForPath(collector.Resource.Name));

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "config.yaml");

        // LF endings whatever the host does: the collector reads this inside a Linux container.
        File.WriteAllText(path, configuration.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));

        collector.WithBindMount(path, ConfigPathInContainer, isReadOnly: true);

        // Aspire injects its dashboard endpoint under the standard name; the generated configuration reads it
        // under a name of its own, so the collector's own exporter cannot be confused with what it receives.
        collector.WithEnvironment(context =>
        {
            if (context.EnvironmentVariables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var endpoint))
            {
                context.EnvironmentVariables[PhpTelemetryCollectorConfig.DashboardEndpointVariable] = endpoint;
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                context.EnvironmentVariables[PhpTelemetryCollectorConfig.DashboardApiKeyVariable] = apiKey;
            }
        });
    }

    private static string SanitizeForPath(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
