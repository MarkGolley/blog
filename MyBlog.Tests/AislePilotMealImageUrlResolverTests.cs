using MyBlog.Services;

namespace MyBlog.Tests;

public class AislePilotMealImageUrlResolverTests
{
    [Theory]
    [InlineData(null, "/projects/aisle-pilot/images/aislepilot-icon.svg")]
    [InlineData("", "/projects/aisle-pilot/images/aislepilot-icon.svg")]
    [InlineData("   ", "/projects/aisle-pilot/images/aislepilot-icon.svg")]
    [InlineData("/projects/aisle-pilot/images/aislepilot-meals/chilli.png", "/projects/aisle-pilot/images/aislepilot-meals/chilli.png")]
    [InlineData("/images/aislepilot-icon.svg", "/projects/aisle-pilot/images/aislepilot-icon.svg")]
    [InlineData("images/aislepilot-icon.svg", "/projects/aisle-pilot/images/aislepilot-icon.svg")]
    [InlineData("aislepilot-meals/chilli.png", "/projects/aisle-pilot/images/aislepilot-meals/chilli.png")]
    [InlineData("chilli.png", "/projects/aisle-pilot/images/aislepilot-meals/chilli.png")]
    [InlineData("https://cdn.example.com/chilli.png", "https://cdn.example.com/chilli.png")]
    [InlineData("/custom/path/chilli.png", "/custom/path/chilli.png")]
    public void ResolveClientMealImageUrl_NormalizesKnownImagePaths(string? input, string expected)
    {
        var resolved = AislePilotMealImageUrlResolver.ResolveClientMealImageUrl(input);

        Assert.Equal(expected, resolved);
    }
}
