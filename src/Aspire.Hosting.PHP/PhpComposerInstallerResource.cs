using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// Runs <c>composer install</c> for a PHP application before the application starts.
/// </summary>
internal sealed class PhpComposerInstallerResource(string name, string workingDirectory)
    : ExecutableResource(name, "composer", workingDirectory);

/// <summary>
/// Runs <c>composer install</c> in a container for a PHP application that itself runs in a container.
/// </summary>
internal sealed class PhpComposerInstallerContainerResource(string name) : ContainerResource(name);
