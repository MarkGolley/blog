using System.Text.Json;

namespace MyBlog.Tests;

public sealed class ObservabilityRuntimeConfigurationTests
{
    [Fact]
    public void DevelopmentConfiguration_DoesNotRequireLocalOtlpCollector()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            var candidate = Path.Combine(directory.FullName, "MyBlog", "appsettings.Development.json");
            if (File.Exists(candidate))
            {
                path = candidate;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var observability = document.RootElement.GetProperty("Observability");

        Assert.False(observability.GetProperty("EnableOtlp").GetBoolean());
        Assert.Equal("http://localhost:4318", observability.GetProperty("OtlpEndpoint").GetString());
    }
}
