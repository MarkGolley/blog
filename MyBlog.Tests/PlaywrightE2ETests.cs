using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace MyBlog.Tests;

[Trait("Category", "E2E")]
public sealed class PlaywrightE2ETests : IAsyncLifetime
{
    private const string E2EEnvVar = "RUN_PLAYWRIGHT_E2E";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "password";
    private const string ModerationBannerText =
        "Your comment was not published because it did not meet our moderation standards.";

    private LocalAppHost? _appHost;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        _appHost = await LocalAppHost.StartAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync();
        }
    }

    [Fact]
    public async Task Mobile_ModeratedComment_ShowsModerationBannerAtAddCommentSection()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToFirstPostAsync(page);

        await page.FillAsync("#author-root", "Mobile E2E");
        await page.FillAsync("#content-root", "kill yourself you deserve to die");
        await page.ClickAsync("section[aria-labelledby='add-comment-title'] button[type='submit']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains("commentStatus=moderated", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#add-comment-title", page.Url, StringComparison.OrdinalIgnoreCase);

        var banner = page.Locator("section[aria-labelledby='add-comment-title'] .moderation-notice");
        Assert.True(await banner.IsVisibleAsync());
        Assert.Equal(ModerationBannerText, (await banner.InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task Mobile_AdminLogin_SucceedsWithUppercaseUsernameInput()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "ADMIN");

        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending Comments", await page.ContentAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Desktop_LoginLogoutLogin_DoesNotReturnBadRequest()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        var sawBadRequest = false;

        page.Response += (_, response) =>
        {
            if (response.Status == (int)HttpStatusCode.BadRequest)
            {
                sawBadRequest = true;
            }
        };

        await LoginAsync(page, AdminUsername);
        await LogoutAsync(page);
        await LoginAsync(page, AdminUsername);

        Assert.False(sawBadRequest, "Encountered one or more HTTP 400 responses during login/logout flow.");
        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsE2EEnabled()
    {
        var value = Environment.GetEnvironmentVariable(E2EEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IBrowserContext> CreateMobileContextAsync()
    {
        if (_playwright is null || _browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        if (_playwright.Devices.TryGetValue("iPhone 13", out var iphone13))
        {
            return await _browser.NewContextAsync(iphone13);
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 390,
                Height = 844
            }
        });
    }

    private async Task<IBrowserContext> CreateDesktopContextAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1440,
                Height = 900
            }
        });
    }

    private async Task GoToFirstPostAsync(IPage page)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/blog");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var firstPostLink = page.Locator("main a[href^='/blog/']").First;
        await firstPostLink.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task LoginAsync(IPage page, string username)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/Admin/Login");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", AdminPassword);
        await page.ClickAsync("form button[type='submit']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LogoutAsync(IPage page)
    {
        await page.ClickAsync("button:has-text('Logout')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains("/Blog", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LocalAppHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output;

        private LocalAppHost(Process process, string baseUrl, StringBuilder output)
        {
            _process = process;
            BaseUrl = baseUrl;
            _output = output;
        }

        public string BaseUrl { get; }

        public static async Task<LocalAppHost> StartAsync()
        {
            var port = GetFreePort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var projectPath = Path.Combine(repoRoot, "MyBlog", "MyBlog.csproj");
            var output = new StringBuilder();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\"",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.Environment["PORT"] = port.ToString();
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["ADMIN_USERNAME"] = AdminUsername;
            startInfo.Environment["ADMIN_PASSWORD"] = AdminPassword;
            startInfo.Environment["SUBSCRIBER_NOTIFY_KEY"] = "integration-notify-key";

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WaitUntilReadyAsync(baseUrl, process, output);
            return new LocalAppHost(process, baseUrl, output);
        }

        public async ValueTask DisposeAsync()
        {
            if (_process.HasExited)
            {
                _process.Dispose();
                return;
            }

            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitUntilReadyAsync(string baseUrl, Process process, StringBuilder output)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            var timeoutAt = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < timeoutAt)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Local app process exited before startup completed. Output:{Environment.NewLine}{output}");
                }

                try
                {
                    using var response = await client.GetAsync($"{baseUrl}/health");
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        return;
                    }
                }
                catch
                {
                    // App may still be starting.
                }

                await Task.Delay(500);
            }

            throw new TimeoutException(
                $"Timed out waiting for local app startup at {baseUrl}. Output:{Environment.NewLine}{output}");
        }
    }
}
