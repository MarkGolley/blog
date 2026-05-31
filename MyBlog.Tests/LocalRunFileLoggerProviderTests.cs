using Microsoft.Extensions.Logging;
using MyBlog.Startup;

namespace MyBlog.Tests;

public sealed class LocalRunFileLoggerProviderTests
{
    [Fact]
    public void LocalRunLogs_OverwriteMode_ReplacesPreviousRunFile()
    {
        var tempRoot = CreateTempRootDirectory();
        try
        {
            var options = new LocalRunLogOptions(
                Enabled: true,
                DirectoryPath: "runtime-logs",
                Mode: LocalRunLogMode.OverwriteSingleFile,
                OverwriteFileName: "latest-run.log",
                RetainedRunFiles: 10,
                MinimumLevel: LogLevel.Information);

            var firstLogPath = WriteSingleRun(options, tempRoot, "first-run-marker");
            Assert.True(File.Exists(firstLogPath));

            var secondLogPath = WriteSingleRun(options, tempRoot, "second-run-marker");
            Assert.Equal(firstLogPath, secondLogPath);
            Assert.True(File.Exists(secondLogPath));

            var finalText = File.ReadAllText(secondLogPath);
            Assert.Contains("second-run-marker", finalText, StringComparison.Ordinal);
            Assert.DoesNotContain("first-run-marker", finalText, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void LocalRunLogs_PerRunMode_KeepsOnlyMostRecentRunFiles()
    {
        var tempRoot = CreateTempRootDirectory();
        try
        {
            var options = new LocalRunLogOptions(
                Enabled: true,
                DirectoryPath: "runtime-logs",
                Mode: LocalRunLogMode.PerRunFiles,
                OverwriteFileName: "latest-run.log",
                RetainedRunFiles: 2,
                MinimumLevel: LogLevel.Information);

            WriteSingleRun(options, tempRoot, "run-1-marker");
            WriteSingleRun(options, tempRoot, "run-2-marker");
            WriteSingleRun(options, tempRoot, "run-3-marker");
            WriteSingleRun(options, tempRoot, "run-4-marker");

            var logDirectoryPath = Path.Combine(tempRoot, "runtime-logs");
            var runFiles = Directory.GetFiles(logDirectoryPath, "run-*.log", SearchOption.TopDirectoryOnly);
            Assert.Equal(2, runFiles.Length);

            var combinedText = string.Join(Environment.NewLine, runFiles.Select(File.ReadAllText));
            Assert.Contains("run-3-marker", combinedText, StringComparison.Ordinal);
            Assert.Contains("run-4-marker", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("run-1-marker", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("run-2-marker", combinedText, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static string WriteSingleRun(LocalRunLogOptions options, string contentRootPath, string marker)
    {
        var provider = LocalRunFileLoggerProvider.TryCreate(options, contentRootPath);
        Assert.NotNull(provider);
        var logFilePath = provider.LogFilePath;

        using (provider)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(provider);
            });

            var logger = loggerFactory.CreateLogger<LocalRunFileLoggerProviderTests>();
            logger.LogInformation("Local run logging marker {Marker}", marker);
        }

        return logFilePath;
    }

    private static string CreateTempRootDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "myblog-local-run-log-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
