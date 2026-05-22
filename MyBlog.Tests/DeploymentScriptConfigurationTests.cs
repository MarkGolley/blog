using System.Text.RegularExpressions;

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

    [Fact]
    public void RunChecksScript_StopsLocalMyBlogHosts_BeforeBuildAndTests()
    {
        var runChecksScriptPath = Path.Combine(RepoRoot, "run_checks.ps1");
        var runChecksScript = File.ReadAllText(runChecksScriptPath);

        Assert.Contains("function Stop-LocalMyBlogHosts", runChecksScript);
        Assert.Contains("Stopping local MyBlog host process(es):", runChecksScript);
        Assert.Matches(new Regex(@"-match\s+""[^""]*MyBlog[^""]*dll[^""]*"""), runChecksScript);
        Assert.Contains(@"run --project\\s+.*MyBlog", runChecksScript);
        Assert.Matches(
            new Regex(@"if \(\$Mode -eq ""Tests"" -or \$Mode -eq ""PreDeploy""\)\s*\{\s*Stop-LocalMyBlogHosts"),
            runChecksScript);
        Assert.Matches(
            new Regex(@"if \(\$Mode -eq ""E2E"" -or \$Mode -eq ""PreDeploy""\)\s*\{\s*Stop-LocalMyBlogHosts"),
            runChecksScript);
    }
}
