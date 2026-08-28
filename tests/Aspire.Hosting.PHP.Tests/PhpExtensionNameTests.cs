#pragma warning disable ASPIREDOCKERFILEBUILDER001

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.PHP.Tests;

public class PhpExtensionNameTests
{
    [Theory]
    [InlineData(PhpExtensions.DataStructures, "ds")]
    [InlineData(PhpExtensions.SimdJson, "simdjson")]
    [InlineData(PhpExtensions.PdoSqlServer, "pdo_sqlsrv")]
    [InlineData(PhpExtensions.MySqli, "mysqli")]
    [InlineData(PhpExtensions.Igbinary, "igbinary")]
    public void Constants_MatchTheNameTheInstallerExpects(string constant, string expected)
        => Assert.Equal(expected, constant);

    [Fact]
    public void Constants_ReachTheGeneratedDockerfile()
    {
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);

        var php = builder.AddPhpApp("worker", directory.Path, "worker.php")
            .WithPhpExtension(PhpExtensions.DataStructures, PhpExtensions.SimdJson, PhpExtensions.PdoSqlServer);

        Assert.Contains(
            "RUN install-php-extensions ds simdjson pdo_sqlsrv",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServerReference_InstallsThePdoDriver()
    {
        // PhpDatabaseDriver.SqlServer maps to pdo_sqlsrv, which does build on the default Alpine images.
        using var directory = new TempAppDirectory();
        var builder = PhpTestBuilder.CreatePublishBuilder(directory.Path);
        var db = builder.AddConnectionString("mssql");

        var php = builder.AddPhpWebApp("app", directory.Path)
            .WithDatabaseReference(db, driver: PhpDatabaseDriver.SqlServer);

        Assert.Contains(
            "pdo_sqlsrv",
            PhpTestBuilder.RenderPublishDockerfile(php.Resource),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryConstantIsAValidExtensionToken()
    {
        // The generator rejects anything that could inject a shell command, so a bad constant would only
        // surface at image build time.
        var constants = typeof(PhpExtensions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);

        foreach (var name in constants)
        {
            Assert.Matches("^[a-z0-9_]+$", name);
        }
    }
}
