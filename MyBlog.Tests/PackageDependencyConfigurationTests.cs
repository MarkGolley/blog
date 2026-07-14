using System.Xml.Linq;

namespace MyBlog.Tests;

public sealed class PackageDependencyConfigurationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void MyBlog_UsesPatchedMailKitVersion()
    {
        var projectPath = Path.Combine(RepoRoot, "MyBlog", "MyBlog.csproj");
        var project = XDocument.Load(projectPath);

        var mailKitReference = project
            .Descendants("PackageReference")
            .Single(element => string.Equals(
                element.Attribute("Include")?.Value,
                "MailKit",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal("4.17.0", mailKitReference.Attribute("Version")?.Value);
    }
}
