using System;
using System.Collections.Generic;
using System.Linq;

namespace MyBlog.Models;

public sealed class AislePilotSupermarketInsightStripViewModel
{
    public IReadOnlyList<string> Facts { get; init; } = Array.Empty<string>();

    public string? LayoutDetail { get; init; }

    public string? PricingDetail { get; init; }

    public string? ReviewDetail { get; init; }

    public bool HasContent =>
        Facts.Count > 0 ||
        !string.IsNullOrWhiteSpace(LayoutDetail) ||
        !string.IsNullOrWhiteSpace(PricingDetail) ||
        !string.IsNullOrWhiteSpace(ReviewDetail);

    public static AislePilotSupermarketInsightStripViewModel Create(
        AislePilotSupermarketLayoutInsightViewModel layoutInsight,
        AislePilotSupermarketPriceInsightViewModel priceInsight,
        string priceAdjustmentText,
        string? layoutSummary,
        string? supermarketPriceSummary,
        string? reviewDetail)
    {
        var facts = new List<string>();

        if (!string.IsNullOrWhiteSpace(layoutInsight.SourceLabel))
        {
            facts.Add(layoutInsight.SourceLabel);
        }

        if (!string.IsNullOrWhiteSpace(layoutInsight.ConfidenceLabel))
        {
            facts.Add($"{layoutInsight.ConfidenceLabel} confidence");
        }

        if (!string.IsNullOrWhiteSpace(priceAdjustmentText))
        {
            facts.Add(priceAdjustmentText);
        }

        var checkedFact = priceInsight.LastVerifiedUtc.HasValue
            ? $"Checked {priceInsight.LastVerifiedUtc.Value.ToLocalTime():MMM yyyy}"
            : layoutInsight.LastVerifiedUtc.HasValue
                ? $"Checked {layoutInsight.LastVerifiedUtc.Value.ToLocalTime():MMM yyyy}"
                : null;

        if (!string.IsNullOrWhiteSpace(checkedFact))
        {
            facts.Add(checkedFact);
        }

        return new AislePilotSupermarketInsightStripViewModel
        {
            Facts = facts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            LayoutDetail = string.IsNullOrWhiteSpace(layoutSummary) ? null : layoutSummary,
            PricingDetail = string.IsNullOrWhiteSpace(supermarketPriceSummary) ? null : supermarketPriceSummary,
            ReviewDetail = reviewDetail
        };
    }
}
