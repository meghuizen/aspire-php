namespace Aspire.Hosting.PHP;

/// <summary>
/// How a PHP resource runs during <c>aspire run</c>.
/// </summary>
/// <remarks>
/// This is chosen when the resource is created, not afterwards, because it decides whether the resource is an
/// executable or a container. Pass it to <c>AddPhpApp</c> or <c>AddPhpWebApp</c> to override the default.
/// Publishing always builds a container regardless of this value.
/// </remarks>
public enum PhpRunMode
{
    /// <summary>
    /// Use a local <c>php</c> if one is on the PATH, otherwise run in a container.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always use the local <c>php</c>. Fails at startup if PHP is not installed.
    /// </summary>
    Executable = 1,

    /// <summary>
    /// Always run in a container. Requires a running container runtime, but no local PHP.
    /// </summary>
    Container = 2
}
