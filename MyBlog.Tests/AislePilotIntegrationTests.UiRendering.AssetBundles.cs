namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    private static async Task<string> GetCombinedAislePilotCssAsync(HttpClient client)
    {
        var assetPaths = new[]
        {
            "/css/aisle-pilot-shell.css",
            "/css/aisle-pilot-overview.css",
            "/css/aisle-pilot-setup.css",
            "/css/aisle-pilot-results.css",
            "/css/aisle-pilot-actions.css",
            "/css/aisle-pilot-responsive.css",
            "/css/aisle-pilot-dark.css",
            "/css/aisle-pilot-header-compact.css"
        };

        var cssChunks = new List<string>(assetPaths.Length);
        foreach (var assetPath in assetPaths)
        {
            cssChunks.Add(await client.GetStringAsync(assetPath));
        }

        return string.Join(Environment.NewLine, cssChunks);
    }

    private static async Task<string> GetCombinedAislePilotScriptAsync(HttpClient client)
    {
        var assetPaths = new[]
        {
            "/js/aisle-pilot/core.js",
            "/js/aisle-pilot/action-menus.js",
            "/js/aisle-pilot/shopping.js",
            "/js/aisle-pilot.js"
        };

        var scriptChunks = new List<string>(assetPaths.Length);
        foreach (var assetPath in assetPaths)
        {
            scriptChunks.Add(await client.GetStringAsync(assetPath));
        }

        return string.Join(Environment.NewLine, scriptChunks);
    }
}
