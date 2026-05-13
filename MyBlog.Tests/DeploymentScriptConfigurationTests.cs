namespace MyBlog.Tests;

public sealed class DeploymentScriptConfigurationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void DeployScript_UsesUpdateEnvVars_ToPreserveExistingSecrets()
    {
        var deployScriptPath = Path.Combine(RepoRoot, "Deployment", "deploy.ps1");
        var deployScript = File.ReadAllText(deployScriptPath);

        Assert.Contains("\"--update-env-vars\", $envVarsArgument", deployScript);
        Assert.DoesNotContain("\"--set-env-vars\", $envVarsArgument", deployScript);
    }
}
