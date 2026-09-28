using System.Diagnostics;
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

    [Fact]
    public void AislePilotAssetBudgetCheck_NormalizesWindowsLineEndings()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"aisle-pilot-budget-{Guid.NewGuid():N}");
        var cssDirectory = Path.Combine(temporaryRoot, "MyBlog", "wwwroot", "css");
        var javascriptDirectory = Path.Combine(temporaryRoot, "MyBlog", "wwwroot", "js", "aisle-pilot");

        try
        {
            Directory.CreateDirectory(cssDirectory);
            Directory.CreateDirectory(javascriptDirectory);
            File.WriteAllText(Path.Combine(cssDirectory, "aisle-pilot.css"), "a\r\nb\r\n");
            File.WriteAllText(Path.Combine(javascriptDirectory, "module.js"), "a\r\nb\r\n");
            File.WriteAllText(Path.Combine(temporaryRoot, "MyBlog", "wwwroot", "js", "aisle-pilot.js"), "a\r\nb\r\n");

            var scriptPath = Path.Combine(RepoRoot, "scripts", "check-aislepilot-asset-budgets.ps1");
            var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            var startInfo = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-RepoRoot");
            startInfo.ArgumentList.Add(temporaryRoot);
            startInfo.ArgumentList.Add("-CssBudgetBytes");
            startInfo.ArgumentList.Add("4");
            startInfo.ArgumentList.Add("-JavaScriptBudgetBytes");
            startInfo.ArgumentList.Add("8");
            startInfo.ArgumentList.Add("-SingleJavaScriptBudgetBytes");
            startInfo.ArgumentList.Add("4");

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"{output}{Environment.NewLine}{error}");
            Assert.Contains("AislePilot CSS: 4 / 4 bytes", output);
            Assert.Contains("AislePilot JavaScript: 8 / 8 bytes", output);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
