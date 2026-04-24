namespace MyBlog.Tests;

public sealed class DockerBuildConfigurationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Dockerignore_Excludes_LargeGeneratedAndNonBuildPaths()
    {
        var dockerIgnorePath = Path.Combine(RepoRoot, ".dockerignore");
        var dockerIgnoreEntries = File.ReadAllLines(dockerIgnorePath)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        var requiredEntries = new[]
        {
            "artifacts/",
            ".artifacts/",
            ".out/",
            "**/bin/",
            "**/obj/",
            "TestResults/",
            ".playwright/",
            "playwright-report/",
            "MyBlog.Tests/",
            "MyBlog.ModerationEval/",
            "docs/",
            "Deployment/"
        };

        foreach (var entry in requiredEntries)
        {
            Assert.Contains(entry, dockerIgnoreEntries);
        }
    }

    [Fact]
    public void Dockerfile_Restores_ProjectReferences_BeforePublish()
    {
        var dockerfilePath = Path.Combine(RepoRoot, "Dockerfile");
        var dockerfile = File.ReadAllText(dockerfilePath);

        Assert.Contains("COPY MyBlog/MyBlog.csproj MyBlog/", dockerfile);
        Assert.Contains("COPY MyBlog.AislePilot/MyBlog.AislePilot.csproj MyBlog.AislePilot/", dockerfile);
        Assert.Contains("RUN dotnet restore MyBlog/MyBlog.csproj", dockerfile);
        Assert.Contains("COPY MyBlog/ MyBlog/", dockerfile);
        Assert.Contains("COPY MyBlog.AislePilot/ MyBlog.AislePilot/", dockerfile);
        Assert.Contains("RUN dotnet publish MyBlog/MyBlog.csproj -c Release -o /app/out --no-restore", dockerfile);
    }
}
