using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// A PHP application that runs as a local <c>php</c> process.
/// </summary>
/// <param name="name">The name of the resource in the app model.</param>
/// <param name="phpExecutablePath">Full path to the <c>php</c> executable, or just <c>php</c> to resolve from PATH.</param>
/// <param name="appDirectory">The directory holding the PHP application. Also the Docker build context when publishing.</param>
public class PhpAppResource(string name, string phpExecutablePath, string appDirectory)
    : ExecutableResource(name, phpExecutablePath, appDirectory), IPhpResource, IContainerFilesDestinationResource
{
    /// <inheritdoc />
    public string AppDirectory { get; } = appDirectory;

    /// <inheritdoc />
    public PhpRunMode RunMode => PhpRunMode.Executable;
}

/// <summary>
/// A PHP web application that runs as a local <c>php</c> process using PHP's built-in development server.
/// </summary>
/// <remarks>
/// The built-in server is single-threaded and is for development only. Publishing always produces a FrankenPHP
/// container instead, so what you run locally and what you deploy differ here by design.
/// </remarks>
/// <param name="name">The name of the resource in the app model.</param>
/// <param name="phpExecutablePath">Full path to the <c>php</c> executable, or just <c>php</c> to resolve from PATH.</param>
/// <param name="appDirectory">The directory holding the PHP application.</param>
/// <param name="documentRoot">The document root relative to <paramref name="appDirectory"/>.</param>
public class PhpWebAppResource(string name, string phpExecutablePath, string appDirectory, string documentRoot)
    : PhpAppResource(name, phpExecutablePath, appDirectory), IPhpWebResource
{
    /// <inheritdoc />
    public string DocumentRoot { get; } = documentRoot;
}
