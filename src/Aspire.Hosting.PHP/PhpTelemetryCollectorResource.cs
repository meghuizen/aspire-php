using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// An OpenTelemetry Collector that PHP applications export to.
/// </summary>
/// <remarks>
/// <para>
/// Two reasons this exists rather than PHP exporting straight to a backend.
/// </para>
/// <para>
/// PHP has no background thread, so outside FrankenPHP worker mode a batching exporter has no "later" in which
/// to flush — every request pays the export cost inline, in the request's own critical path. Exporting to a
/// collector on the same host makes that a local write; the collector then batches and forwards on its own
/// schedule.
/// </para>
/// <para>
/// It is also the only supported route to Application Insights. Azure Monitor's OpenTelemetry Distro covers
/// .NET, Java, Node.js and Python, and there is no Application Insights exporter for PHP, so the collector has
/// to do that translation.
/// </para>
/// </remarks>
public sealed class PhpTelemetryCollectorResource(string name) : ContainerResource(name), IResourceWithEnvironment
{
    /// <summary>The endpoint name for OTLP over HTTP.</summary>
    public const string HttpEndpointName = "otlp-http";

    /// <summary>The endpoint name for OTLP over gRPC.</summary>
    public const string GrpcEndpointName = "otlp-grpc";

    /// <summary>
    /// Gets the OTLP HTTP endpoint PHP applications export to.
    /// </summary>
    /// <remarks>
    /// HTTP rather than gRPC is the default for PHP: gRPC needs a separate PHP extension, while OTLP over HTTP
    /// only needs a PSR-18 client the SDK already pulls in.
    /// </remarks>
    public EndpointReference HttpEndpoint => field ??= new(this, HttpEndpointName);
}
