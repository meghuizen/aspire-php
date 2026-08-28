using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// A PHP console command running alongside its application.
/// </summary>
/// <remarks>
/// Implemented by both the executable and container shapes, so callers can wait on one without caring which
/// way the application happens to be running.
/// </remarks>
public interface IPhpConsoleResource : IResourceWithWaitSupport, IResourceWithEnvironment, IResourceWithArgs;

/// <summary>
/// A PHP console command run as a local process.
/// </summary>
internal sealed class PhpConsoleResource(string name, string phpExecutablePath, string workingDirectory)
    : ExecutableResource(name, phpExecutablePath, workingDirectory), IPhpConsoleResource;

/// <summary>
/// A PHP console command run in a container.
/// </summary>
internal sealed class PhpConsoleContainerResource(string name) : ContainerResource(name), IPhpConsoleResource;

/// <summary>
/// How a console command behaves once started.
/// </summary>
public enum PhpConsoleCommandKind
{
    /// <summary>
    /// Runs once and exits. The application waits for it to succeed before starting.
    /// </summary>
    /// <remarks>Migrations are the usual case: the schema has to be current before anything serves traffic.</remarks>
    OneShot = 0,

    /// <summary>
    /// Runs until stopped. The application does not wait for it.
    /// </summary>
    /// <remarks>Queue workers and schedulers.</remarks>
    LongRunning = 1
}
