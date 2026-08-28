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
public interface IPhpResource : IResourceWithServiceDiscovery, IResourceWithEnvironment, IResourceWithArgs, IResourceWithWaitSupport
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
public interface IPhpWebResource : IPhpResource
{
    /// <summary>
    /// Gets the document root, relative to <see cref="IPhpResource.AppDirectory"/>. Usually <c>public</c>.
    /// </summary>
    string DocumentRoot { get; }
}
