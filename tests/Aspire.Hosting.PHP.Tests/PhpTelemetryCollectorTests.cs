using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpTelemetryCollectorTests
{
    [Fact]
    public void Config_AlwaysHasAnExporter()
    {
        // A pipeline with no exporters is a configuration error the collector refuses to start with.
        var config = PhpTelemetryCollectorConfig.Build(
            forwardToDashboard: false,
            forwardToApplicationInsights: false,
            skipTlsVerification: false,
            dashboardNeedsApiKey: false);

        Assert.Contains("debug:", config, StringComparison.Ordinal);
        Assert.Contains("exporters: [debug]", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_DisablesCompressionForTheDashboard()
    {
        // The collector gzips by default and the dashboard parses the body as protobuf without decompressing,
        // which surfaces as a 500 and an InvalidProtocolBufferException that points nowhere near compression.
        var config = PhpTelemetryCollectorConfig.Build(
            forwardToDashboard: true,
            forwardToApplicationInsights: false,
            skipTlsVerification: true,
            dashboardNeedsApiKey: true);

        Assert.Contains("compression: none", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_SendsTheDashboardApiKeyWhenThereIsOne()
    {
        // Without it the dashboard answers 401 and the collector drops everything it forwards.
        var withKey = PhpTelemetryCollectorConfig.Build(true, false, true, dashboardNeedsApiKey: true);
        var withoutKey = PhpTelemetryCollectorConfig.Build(true, false, true, dashboardNeedsApiKey: false);

        Assert.Contains("x-otlp-api-key: ${env:ASPIRE_OTLP_API_KEY}", withKey, StringComparison.Ordinal);
        Assert.DoesNotContain("x-otlp-api-key", withoutKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_ReferencesSecretsByEnvironmentRatherThanInlining()
    {
        var config = PhpTelemetryCollectorConfig.Build(
            forwardToDashboard: true,
            forwardToApplicationInsights: true,
            skipTlsVerification: false,
            dashboardNeedsApiKey: true);

        // The connection string is a secret; it must not be written into a file that gets copied around.
        Assert.Contains(
            "connection_string: ${env:APPLICATIONINSIGHTS_CONNECTION_STRING}",
            config,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Config_FansOutToBothDestinations()
    {
        var config = PhpTelemetryCollectorConfig.Build(true, true, true, true);

        Assert.Contains("exporters: [otlphttp/dashboard, azuremonitor]", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_CoversAllThreeSignals()
    {
        var config = PhpTelemetryCollectorConfig.Build(true, false, true, true);

        Assert.Contains("    traces:", config, StringComparison.Ordinal);
        Assert.Contains("    metrics:", config, StringComparison.Ordinal);
        Assert.Contains("    logs:", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_SkipsTlsVerificationOnlyWhenRunningLocally()
    {
        Assert.Contains("insecure_skip_verify: true",
            PhpTelemetryCollectorConfig.Build(true, false, skipTlsVerification: true, true),
            StringComparison.Ordinal);

        Assert.DoesNotContain("insecure_skip_verify",
            PhpTelemetryCollectorConfig.Build(true, false, skipTlsVerification: false, true),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_ExposesBothOtlpEndpoints()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);

        var collector = builder.AddOpenTelemetryCollector("otel");

        var endpoints = collector.Resource.Annotations.OfType<EndpointAnnotation>().ToList();

        Assert.Contains(endpoints, e => e.Name == PhpTelemetryCollectorResource.HttpEndpointName && e.TargetPort == 4318);
        Assert.Contains(endpoints, e => e.Name == PhpTelemetryCollectorResource.GrpcEndpointName && e.TargetPort == 4317);
    }

    [Fact]
    public void PointingAnAppAtTheCollector_InstallsTheExtensionAndWaits()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreateRunBuilder(directory.Path);
        var collector = builder.AddOpenTelemetryCollector("otel");

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php", PhpRunMode.Container)
            .WithOpenTelemetry(collector);

        Assert.Contains(php.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == collector.Resource);
        Assert.Contains(
            "opentelemetry",
            PhpTestBuilder.RenderDevDockerfile(php.Resource),
            StringComparison.Ordinal);
    }
}
