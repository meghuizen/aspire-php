#pragma warning disable ASPIREDOCKERFILEBUILDER001

using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.ApplicationModel.Docker;

namespace Aspire.Hosting.PHP.Tests;

/// <summary>
/// Helpers for building an app model in tests without a real AppHost project.
/// </summary>
internal static class PhpTestBuilder
{
    /// <summary>
    /// Creates a builder in publish mode.
    /// </summary>
    /// <remarks>
    /// Publish mode is what most tests want: it always produces the executable resource shape and never probes
    /// for a local PHP, so the same assertions hold on a machine with PHP installed and one without.
    /// </remarks>
    public static IDistributedApplicationBuilder CreatePublishBuilder(string appHostDirectory)
        => Create(appHostDirectory, ["--operation", "publish", "--output-path", Path.Combine(appHostDirectory, "out")]);

    /// <summary>
    /// Creates a builder in run mode.
    /// </summary>
    public static IDistributedApplicationBuilder CreateRunBuilder(string appHostDirectory)
        => Create(appHostDirectory, []);

    // DistributedApplicationTestingBuilder is deliberately not used here. It resolves the DCP orchestrator
    // from an assembly the Aspire.AppHost.Sdk marks, because it is built for tests that actually launch the
    // application. These tests only build the app model and never start anything, so the plain builder is both
    // sufficient and far quicker. The playground end-to-end test, which does start an AppHost, uses the
    // testing builder instead.
    private static IDistributedApplicationBuilder Create(string appHostDirectory, string[] args)
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = args,
            DisableDashboard = true,
            AssemblyName = typeof(PhpTestBuilder).Assembly.FullName,
            ProjectDirectory = appHostDirectory
        });

    /// <summary>
    /// Renders the Dockerfile that publishing would generate for a resource.
    /// </summary>
    /// <remarks>
    /// The generator is called directly rather than through a full publish. It reads only the resource and the
    /// builder it is handed, so this exercises the real code path while keeping the test fast and free of any
    /// dependency on a container runtime.
    /// </remarks>
    public static string RenderPublishDockerfile(IPhpResource resource)
        => Render(builder => PhpDockerfileGenerator.WritePublishDockerfile(resource, Context(resource, builder)));

    /// <summary>
    /// Renders the Dockerfile used for the run-mode container image.
    /// </summary>
    public static string RenderDevDockerfile(IPhpResource resource)
        => Render(builder => PhpDockerfileGenerator.WriteDevDockerfile(resource, Context(resource, builder)));

    private static DockerfileBuilderCallbackContext Context(IPhpResource resource, DockerfileBuilder builder)
        => new(resource, builder, EmptyServiceProvider.Instance, CancellationToken.None);

    private static string Render(Action<DockerfileBuilder> write)
    {
        var builder = new DockerfileBuilder();
        write(builder);

        using var stream = new MemoryStream();
        // LF and no BOM, matching what the integration writes, so assertions are the same on every platform.
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        builder.WriteAsync(writer).GetAwaiter().GetResult();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}

#pragma warning restore ASPIREDOCKERFILEBUILDER001
