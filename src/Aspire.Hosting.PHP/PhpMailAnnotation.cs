using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP;

/// <summary>
/// How a PHP application reaches an SMTP server.
/// </summary>
internal sealed class PhpMailAnnotation : IResourceAnnotation
{
    /// <summary>The address messages are sent from when the application does not set one.</summary>
    public string? FromAddress { get; set; }

    /// <summary>The display name paired with <see cref="FromAddress"/>.</summary>
    public string? FromName { get; set; }

    /// <summary>Whether the server expects a username and password.</summary>
    public bool UsesAuthentication { get; set; }

    /// <summary>Whether the connection is encrypted, and how.</summary>
    public string? Encryption { get; set; }
}
