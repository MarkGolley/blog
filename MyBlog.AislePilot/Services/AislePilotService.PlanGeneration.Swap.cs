using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    public AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames)
    {
        return _mealSwapPipeline.SwapMealForDayAsync(
                this,
                request,
                dayIndex,
                currentMealName,
                currentPlanMealNames,
                seenMealNames)
            .GetAwaiter()
            .GetResult();
    }

    public Task<AislePilotPlanResultViewModel> SwapMealForDayAsync(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames,
        CancellationToken cancellationToken = default)
    {
        return _mealSwapPipeline.SwapMealForDayAsync(
            this,
            request,
            dayIndex,
            currentMealName,
            currentPlanMealNames,
            seenMealNames,
            cancellationToken);
    }

}
