using MyBlog.Services;
using MyBlog.Startup;

namespace MyBlog.Tests;

public sealed class AislePilotMealImageVersioningTests
{
    [Fact]
    public void BuildFileName_IsStableForTheSameContentAndChangesWithContent()
    {
        var first = AislePilotMealImageVersioning.BuildFileName("chicken-curry", [1, 2, 3]);
        var repeated = AislePilotMealImageVersioning.BuildFileName("chicken-curry", [1, 2, 3]);
        var changed = AislePilotMealImageVersioning.BuildFileName("chicken-curry", [1, 2, 4]);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, changed);
        Assert.Matches(@"^chicken-curry-[a-f0-9]{16}\.jpg$", first);
        Assert.True(AislePilotMealImageVersioning.IsVersionedPath($"/images/aislepilot-meals/{first}"));
    }

    [Theory]
    [InlineData("/images/aislepilot-meals/chicken-curry-0123456789abcdef.jpg", "public, max-age=31536000, immutable")]
    [InlineData("/projects/aisle-pilot/images/aislepilot-meals/chicken-curry-0123456789abcdef.webp", "public, max-age=31536000, immutable")]
    [InlineData("/images/aislepilot-meals/chicken-curry.jpg", "public, max-age=86400")]
    [InlineData("/images/site-logo-0123456789abcdef.png", null)]
    public void ResolveCacheControl_UsesImmutableCachingOnlyForVersionedMealImages(
        string path,
        string? expected)
    {
        Assert.Equal(expected, AppRequestPolicies.ResolveAislePilotMealImageCacheControl(path));
    }
}
