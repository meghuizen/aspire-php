using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// A PHP application that runs in a container because no local PHP was found.
/// </summary>
/// <remarks>
/// The application directory is bind-mounted into the container, so edits on disk take effect immediately and
/// <c>vendor/</c> is written back to the host.
/// </remarks>
/// <param name="name">The name of the resource in the app model.</param>
/// <param name="appDirectory">The directory holding the PHP application, bind-mounted into the container.</param>
public class PhpContainerAppResource(string name, string appDirectory)
    : ContainerResource(name), IPhpResource
{
    /// <inheritdoc />
    public string AppDirectory { get; } = appDirectory;

    /// <inheritdoc />
    public PhpRunMode RunMode => PhpRunMode.Container;
}

/// <summary>
/// A PHP web application served by FrankenPHP in a container.
/// </summary>
/// <param name="name">The name of the resource in the app model.</param>
/// <param name="appDirectory">The directory holding the PHP application, bind-mounted into the container.</param>
/// <param name="documentRoot">The document root relative to <paramref name="appDirectory"/>.</param>
public class PhpWebContainerAppResource(string name, string appDirectory, string documentRoot)
    : PhpContainerAppResource(name, appDirectory), IPhpWebResource
{
    /// <inheritdoc />
    public string DocumentRoot { get; } = documentRoot;
}
