using System.Diagnostics;
using System.Text;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task AislePilotScript_CombinedBundles_AreValidJavaScript()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var script = await GetCombinedAislePilotScriptAsync(client);

        var tempScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"aislepilot-script-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"--check \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var diagnostics = string.Join(
                Environment.NewLine,
                new[] { standardOutput, standardError }.Where(chunk => !string.IsNullOrWhiteSpace(chunk)));

            Assert.True(
                process.ExitCode == 0,
                string.IsNullOrWhiteSpace(diagnostics)
                    ? "Expected combined AislePilot bundles to parse as valid JavaScript."
                    : $"Expected combined AislePilot bundles to parse as valid JavaScript.{Environment.NewLine}{diagnostics}");
        }
        finally
        {
            if (File.Exists(tempScriptPath))
            {
                File.Delete(tempScriptPath);
            }
        }
    }
}
