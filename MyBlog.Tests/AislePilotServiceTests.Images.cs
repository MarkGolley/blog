using System.Reflection;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Fact]
    public void MealImageGenerationThrottle_DefaultsToSmallBoundedParallelism()
    {
        var concurrency = GetPrivateStaticInt("MealImageGenerationMaxConcurrency");
        var throttle = GetRequiredStaticField("MealImageGenerationThrottle");
        var currentCountProperty = throttle.GetType().GetProperty("CurrentCount");
        Assert.NotNull(currentCountProperty);

        var currentCount = currentCountProperty!.GetValue(throttle);

        Assert.Equal(3, concurrency);
        Assert.NotNull(currentCount);
        Assert.Equal(concurrency, (int)currentCount!);
    }

    [Fact]
    public void GetMealNamesRequiringHydration_SkipsCachedMissedAndRecentlyCheckedMeals()
    {
        ClearMealImageState();
        var nowUtc = DateTime.UtcNow;

        SetConcurrentDictionaryValue(GetRequiredStaticField("MealImagePool"), "Egg fried rice", "/images/custom.png");
        SetConcurrentDictionaryValue(GetRequiredStaticField("MealImageLookupMissesUtc"), "Chicken stir fry with rice", nowUtc.AddSeconds(10));
        SetConcurrentDictionaryValue(GetRequiredStaticField("MealImageLookupChecksUtc"), "Prawn tomato pasta", nowUtc);

        var method = typeof(AislePilotService).GetMethod("GetMealNamesRequiringHydration", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var mealNames = new List<string>
        {
            "Egg fried rice",
            "Chicken stir fry with rice",
            "Prawn tomato pasta",
            "Sausage traybake"
        };
        var result = method!.Invoke(_service, [mealNames, nowUtc]) as IReadOnlyList<string>;

        Assert.NotNull(result);
        Assert.Equal(["Sausage traybake"], result);
    }

    [Fact]
    public void TryResolveImmediateMealImageUrl_ReturnsCachedUrl_ForCachedMealName()
    {
        ClearMealImageState();
        SetConcurrentDictionaryValue(GetRequiredStaticField("MealImagePool"), "Egg fried rice", "/images/custom.png");

        var method = typeof(AislePilotService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                if (!candidate.Name.Equals("TryResolveImmediateMealImageUrl", StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = candidate.GetParameters();
                return parameters.Length == 3 &&
                       parameters[0].ParameterType == typeof(string) &&
                       parameters[1].ParameterType == typeof(string) &&
                       parameters[2].ParameterType == typeof(string).MakeByRefType();
            });
        Assert.NotNull(method);

        var args = new object?[] { "Egg fried rice", string.Empty, null };
        var didResolve = method!.Invoke(_service, args);

        Assert.NotNull(didResolve);
        Assert.True((bool)didResolve!);
        Assert.Equal("/images/custom.png", args[2] as string);
    }

    private static void ClearMealImageState()
    {
        ClearConcurrentDictionary(GetRequiredStaticField("MealImagePool"));
        ClearConcurrentDictionary(GetRequiredStaticField("MealImageLookupMissesUtc"));
        ClearConcurrentDictionary(GetRequiredStaticField("MealImageLookupChecksUtc"));
    }

    private static void SetConcurrentDictionaryValue(object dictionary, string key, object? value)
    {
        var indexer = dictionary.GetType().GetProperty("Item");
        Assert.NotNull(indexer);
        indexer!.SetValue(dictionary, value, [key]);
    }
}
