namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Get_DoesNotReferenceMissingScopedStylesheetAsset()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("/css/site.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/MyBlog.styles.css", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_LoadsJQueryBeforeValidationBundles()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        var jqueryIndex = html.IndexOf("/lib/jquery/dist/jquery.min.js", StringComparison.OrdinalIgnoreCase);
        var validateIndex = html.IndexOf("/lib/jquery-validation/dist/jquery.validate.min.js", StringComparison.OrdinalIgnoreCase);
        var unobtrusiveIndex = html.IndexOf("/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js", StringComparison.OrdinalIgnoreCase);

        Assert.True(jqueryIndex >= 0, "Expected jQuery to be included in AislePilot scripts.");
        Assert.True(validateIndex > jqueryIndex, "Expected jquery.validate to load after jQuery.");
        Assert.True(unobtrusiveIndex > validateIndex, "Expected jquery.validate.unobtrusive to load after jquery.validate.");
    }
}
