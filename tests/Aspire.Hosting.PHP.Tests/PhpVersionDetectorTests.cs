using Aspire.Hosting.PHP;

namespace Aspire.Hosting.PHP.Tests;

public class PhpVersionDetectorTests
{
    [Fact]
    public void DetectVersion_ReadsPhpVersionFile()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", "8.5.9\n");

        Assert.Equal("8.5", PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_TolueratesWindowsLineEndingsInPhpVersionFile()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", "8.4\r\n");

        Assert.Equal("8.4", PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_PrefersPhpVersionFileOverComposerJson()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile(".php-version", "8.5");
        directory.WriteFile("composer.json", """{ "require": { "php": "^8.2" } }""");

        Assert.Equal("8.5", PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_ReadsComposerRequire()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", """{ "require": { "php": "^8.5" } }""");

        Assert.Equal("8.5", PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_PrefersComposerPlatformOverRequire()
    {
        // config.platform.php is what Composer resolved the vendor directory against, so it beats the
        // looser constraint in require.
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", """
            {
              "require": { "php": ">=8.1" },
              "config": { "platform": { "php": "8.5.9" } }
            }
            """);

        Assert.Equal("8.5", PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_ReturnsNullWhenNothingDeclaresAVersion()
    {
        using var directory = new TempAppDirectory();

        Assert.Null(PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_ReturnsNullForMalformedComposerJson()
    {
        // Composer itself reports the syntax error far better than the AppHost could, so this must not throw.
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", "{ this is not json");

        Assert.Null(PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Fact]
    public void DetectVersion_ReturnsNullWhenComposerJsonHasNoPhpConstraint()
    {
        using var directory = new TempAppDirectory();
        directory.WriteFile("composer.json", """{ "require": { "monolog/monolog": "^3.0" } }""");

        Assert.Null(PhpVersionDetector.DetectVersion(directory.Path));
    }

    [Theory]
    [InlineData("8.5", "8.5")]
    [InlineData("^8.5", "8.5")]
    [InlineData(">=8.4", "8.4")]
    [InlineData("8.5.*", "8.5")]
    [InlineData("~8.5.0", "8.5")]
    [InlineData("8.5.9", "8.5")]
    [InlineData(">=8.4 <8.6", "8.4")]
    [InlineData("PHP 8.5.9 (cli)", "8.5")]
    public void TryParseMajorMinor_ExtractsTheFirstPair(string input, string expected)
    {
        Assert.True(PhpVersionDetector.TryParseMajorMinor(input, out var version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("8")]
    public void TryParseMajorMinor_RejectsInputWithoutAMajorMinorPair(string? input)
    {
        Assert.False(PhpVersionDetector.TryParseMajorMinor(input, out _));
    }

    [Theory]
    [InlineData("8.5", "8.5", true)]
    [InlineData("8.6", "8.5", true)]
    [InlineData("9.0", "8.5", true)]
    [InlineData("8.4", "8.5", false)]
    [InlineData("7.4", "8.0", false)]
    public void SatisfiesVersion_ComparesMajorThenMinor(string actual, string required, bool expected)
    {
        Assert.Equal(expected, PhpVersionDetector.SatisfiesVersion(actual, required));
    }

    [Fact]
    public void SatisfiesVersion_ReturnsFalseForUnparseableInput()
    {
        Assert.False(PhpVersionDetector.SatisfiesVersion("nightly", "8.5"));
        Assert.False(PhpVersionDetector.SatisfiesVersion("8.5", "nightly"));
    }
}
