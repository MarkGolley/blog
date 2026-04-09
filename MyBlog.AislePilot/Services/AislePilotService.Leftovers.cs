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
    internal static IReadOnlyList<int> BuildMealPortionMultipliers(
        int cookDays,
        int leftoverDays,
        IReadOnlyList<int>? requestedLeftoverSourceDays = null,
        int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var normalizedCookDays = NormalizeCookDays(cookDays, normalizedPlanDays);
        var normalizedLeftoverDays = Math.Clamp(leftoverDays, 0, normalizedPlanDays - normalizedCookDays);
        var defaultMultipliers = Enumerable.Repeat(1, normalizedCookDays).ToArray();
        for (var i = 0; i < normalizedLeftoverDays; i++)
        {
            defaultMultipliers[i % normalizedCookDays]++;
        }

        if (normalizedLeftoverDays == 0)
        {
            return defaultMultipliers;
        }

        var requestedWeekDays = (requestedLeftoverSourceDays ?? [])
            .Where(dayIndex => dayIndex >= 0 && dayIndex < normalizedPlanDays)
            .Take(normalizedLeftoverDays)
            .ToList();

        if (requestedWeekDays.Count == 0)
        {
            return defaultMultipliers;
        }

        var candidates = BuildMealPortionMultiplierCandidates(normalizedCookDays, normalizedPlanDays).ToList();
        if (candidates.Count == 0)
        {
            return defaultMultipliers;
        }

        var rankedCandidates = candidates
            .Select(candidate =>
            {
                var sourceWeekDays = BuildLeftoverSourceWeekDays(candidate, normalizedPlanDays);
                return new
                {
                    Candidate = candidate,
                    SourceWeekDays = sourceWeekDays,
                    Overlap = CalculateLeftoverSourceOverlap(requestedWeekDays, sourceWeekDays, normalizedPlanDays)
                };
            })
            .ToList();

        var exactCandidate = rankedCandidates
            .Where(item => HasEquivalentLeftoverSourceDayCounts(requestedWeekDays, item.SourceWeekDays, normalizedPlanDays))
            .OrderBy(item => string.Join(",", item.Candidate))
            .FirstOrDefault();
        if (exactCandidate is not null)
        {
            return exactCandidate.Candidate;
        }

        return rankedCandidates
            .OrderByDescending(item => item.Overlap)
            .ThenBy(item => CalculateLeftoverSourceDistance(requestedWeekDays, item.SourceWeekDays, normalizedPlanDays))
            .ThenBy(item => string.Join(",", item.Candidate))
            .First()
            .Candidate;
    }

    internal static IReadOnlyList<int> ParseRequestedLeftoverSourceDays(
        string? leftoverCookDayIndexesCsv,
        int cookDays,
        int leftoverDays,
        int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var normalizedCookDays = NormalizeCookDays(cookDays, normalizedPlanDays);
        var normalizedLeftoverDays = Math.Clamp(leftoverDays, 0, normalizedPlanDays - normalizedCookDays);
        if (normalizedLeftoverDays == 0 || string.IsNullOrWhiteSpace(leftoverCookDayIndexesCsv))
        {
            return [];
        }

        var requestedDays = new List<int>(normalizedLeftoverDays);
        var tokens = leftoverCookDayIndexesCsv
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (requestedDays.Count >= normalizedLeftoverDays)
            {
                break;
            }

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayIndex))
            {
                continue;
            }

            if (dayIndex < 0 || dayIndex >= normalizedPlanDays)
            {
                continue;
            }

            requestedDays.Add(dayIndex);
        }

        return requestedDays;
    }

    private static IEnumerable<int[]> BuildMealPortionMultiplierCandidates(int cookDays, int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var normalizedCookDays = NormalizeCookDays(cookDays, normalizedPlanDays);
        var candidate = new int[normalizedCookDays];
        foreach (var composition in BuildMultiplierCompositions(0, normalizedCookDays, normalizedPlanDays, candidate))
        {
            yield return composition;
        }
    }

    private static IEnumerable<int[]> BuildMultiplierCompositions(
        int position,
        int totalSlots,
        int remainingTotal,
        int[] candidate)
    {
        if (position == totalSlots - 1)
        {
            if (remainingTotal >= 1)
            {
                candidate[position] = remainingTotal;
                yield return [.. candidate];
            }

            yield break;
        }

        var maxValue = remainingTotal - (totalSlots - position - 1);
        for (var value = 1; value <= maxValue; value++)
        {
            candidate[position] = value;
            foreach (var composition in BuildMultiplierCompositions(position + 1, totalSlots, remainingTotal - value, candidate))
            {
                yield return composition;
            }
        }
    }

    private static IReadOnlyList<int> BuildLeftoverSourceWeekDays(IReadOnlyList<int> multipliers, int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var sourceDays = new List<int>();
        var dayCursor = 0;
        foreach (var multiplier in multipliers)
        {
            var extras = Math.Max(0, multiplier - 1);
            for (var i = 0; i < extras; i++)
            {
                sourceDays.Add(Math.Clamp(dayCursor, 0, normalizedPlanDays - 1));
            }

            dayCursor += Math.Max(1, multiplier);
        }

        return sourceDays;
    }

    private static int CalculateLeftoverSourceOverlap(
        IReadOnlyList<int> requestedSourceDays,
        IReadOnlyList<int> candidateSourceDays,
        int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var requestedCounts = BuildWeekDayCountMap(requestedSourceDays, normalizedPlanDays);
        var candidateCounts = BuildWeekDayCountMap(candidateSourceDays, normalizedPlanDays);
        var overlap = 0;
        for (var day = 0; day < normalizedPlanDays; day++)
        {
            overlap += Math.Min(requestedCounts[day], candidateCounts[day]);
        }

        return overlap;
    }

    private static int CalculateLeftoverSourceDistance(
        IReadOnlyList<int> requestedSourceDays,
        IReadOnlyList<int> candidateSourceDays,
        int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        if (requestedSourceDays.Count == 0 || candidateSourceDays.Count == 0)
        {
            return int.MaxValue / 4;
        }

        var requestedSorted = requestedSourceDays.OrderBy(x => x).ToArray();
        var candidateSorted = candidateSourceDays.OrderBy(x => x).ToArray();
        var comparableLength = Math.Min(requestedSorted.Length, candidateSorted.Length);
        var distance = 0;
        for (var i = 0; i < comparableLength; i++)
        {
            distance += Math.Abs(requestedSorted[i] - candidateSorted[i]);
        }

        if (requestedSorted.Length != candidateSorted.Length)
        {
            distance += Math.Abs(requestedSorted.Length - candidateSorted.Length) * normalizedPlanDays;
        }

        return distance;
    }

    private static int[] BuildWeekDayCountMap(IReadOnlyList<int> sourceDays, int planDays = 7)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        var counts = new int[normalizedPlanDays];
        foreach (var day in sourceDays)
        {
            if (day >= 0 && day < normalizedPlanDays)
            {
                counts[day]++;
            }
        }

        return counts;
    }

    private static bool HasEquivalentLeftoverSourceDayCounts(
        IReadOnlyList<int> requestedSourceDays,
        IReadOnlyList<int> candidateSourceDays,
        int planDays = 7)
    {
        var requestedCounts = BuildWeekDayCountMap(requestedSourceDays, planDays);
        var candidateCounts = BuildWeekDayCountMap(candidateSourceDays, planDays);
        if (requestedCounts.Length != candidateCounts.Length)
        {
            return false;
        }

        for (var day = 0; day < requestedCounts.Length; day++)
        {
            if (requestedCounts[day] != candidateCounts[day])
            {
                return false;
            }
        }

        return true;
    }

}
