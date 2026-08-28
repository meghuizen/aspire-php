namespace Aspire.Hosting.PHP.Tests;

/// <summary>
/// A throwaway directory standing in for a PHP application on disk.
/// </summary>
/// <remarks>
/// Built from <see cref="System.IO.Path.GetTempPath"/> rather than a literal path so the suite runs on Windows,
/// Linux and macOS alike.
/// </remarks>
internal sealed class TempAppDirectory : IDisposable
{
    public TempAppDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aspire-php-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void WriteFile(string relativePath, string contents)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, contents);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
