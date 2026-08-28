using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// A PHP application in the app model.
/// </summary>
/// <remarks>
/// A PHP resource runs either as a local <c>php</c> process or as a container, depending on whether PHP is
/// installed on the machine running the AppHost. Both shapes implement this interface, so the <c>With*</c>
/// methods work the same either way. See <see cref="PhpRunMode"/>.
/// </remarks>
public interface IPhpResource
    : IResourceWithServiceDiscovery, IResourceWithEnvironment, IResourceWithArgs, IResourceWithWaitSupport, IComputeResource
{
    /// <summary>
    /// Gets the full path to the directory holding the PHP application.
    /// </summary>
    string AppDirectory { get; }

    /// <summary>
    /// Gets how this resource runs during <c>aspire run</c>.
    /// </summary>
    PhpRunMode RunMode { get; }
}

/// <summary>
/// A PHP application served over HTTP by FrankenPHP.
/// </summary>
/// <remarks>
/// Carries probes as well as endpoints. Probes are what a deployment target reads to decide whether a
/// replica has started, may take traffic, and is still alive; a health check registered only with the
/// dashboard tells it nothing. Worker resources are deliberately excluded, having no endpoint to probe.
/// </remarks>
#pragma warning disable ASPIREPROBES001
public interface IPhpWebResource : IPhpResource, IResourceWithProbes
#pragma warning restore ASPIREPROBES001
{
    /// <summary>
    /// Gets the document root, relative to <see cref="IPhpResource.AppDirectory"/>. Usually <c>public</c>.
    /// </summary>
    string DocumentRoot { get; }
}
