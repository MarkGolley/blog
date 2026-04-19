using Microsoft.Extensions.Logging;
using MyBlog.Models;
using System.Text.Json;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private static readonly DateTime UkBasketBenchmarkVerifiedUtc = new(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CoOpPricingVerifiedUtc = new(2026, 2, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MarksAndSpencerPricingVerifiedUtc = new(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime IcelandPricingVerifiedUtc = new(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc);
    private const string TescoPriceBaselineLabel = "Relative to Tesco standard basket pricing";

    private static readonly IReadOnlyList<SupermarketPriceEvidence> WhichBasketBenchmarkEvidence =
    [
        new SupermarketPriceEvidence
        {
            Title = "Which? cheapest supermarkets 2026",
            Url = "https://www.which.co.uk/reviews/supermarkets/article/supermarket-price-comparison-aPpYp9j1MFin",
            SourceType = "article"
        }
    ];

    private static readonly IReadOnlyDictionary<string, SupermarketPriceResolution> CuratedSupermarketPriceProfiles =
        new Dictionary<string, SupermarketPriceResolution>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tesco"] = CreateSupermarketPriceProfile(
                1.00m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Aldi"] = CreateSupermarketPriceProfile(
                0.85m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Lidl"] = CreateSupermarketPriceProfile(
                0.86m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Asda"] = CreateSupermarketPriceProfile(
                0.96m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Morrisons"] = CreateSupermarketPriceProfile(
                1.00m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Sainsbury's"] = CreateSupermarketPriceProfile(
                1.02m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Waitrose"] = CreateSupermarketPriceProfile(
                1.17m,
                "Curated public basket benchmark",
                0.84m,
                isDirectBasketData: true,
                needsReview: false,
                UkBasketBenchmarkVerifiedUtc,
                TescoPriceBaselineLabel,
                WhichBasketBenchmarkEvidence),
            ["Co-op"] = CreateSupermarketPriceProfile(
                1.08m,
                "Curated chain positioning estimate",
                0.58m,
                isDirectBasketData: false,
                needsReview: true,
                CoOpPricingVerifiedUtc,
                "Convenience-led estimate above Tesco baseline",
                new[]
                {
                    new SupermarketPriceEvidence
                    {
                        Title = "Co-op member price savings introduced for Just Eat shoppers",
                        Url = "https://www.co-operative.coop/media/news-releases/online-savings-for-just-eat-shoppers-as-co-op-member-price-savings",
                        SourceType = "official"
                    },
                    new SupermarketPriceEvidence
                    {
                        Title = "Which? cheapest supermarkets 2026",
                        Url = "https://www.which.co.uk/reviews/supermarkets/article/supermarket-price-comparison-aPpYp9j1MFin",
                        SourceType = "article"
                    }
                }),
            ["Iceland"] = CreateSupermarketPriceProfile(
                0.97m,
                "Curated chain positioning estimate",
                0.64m,
                isDirectBasketData: false,
                needsReview: true,
                IcelandPricingVerifiedUtc,
                "Value-led estimate slightly below Tesco baseline",
                new[]
                {
                    new SupermarketPriceEvidence
                    {
                        Title = "Iceland strategy: value",
                        Url = "https://about.iceland.co.uk/our-strategy/",
                        SourceType = "official"
                    },
                    new SupermarketPriceEvidence
                    {
                        Title = "Which? cheapest supermarkets 2026",
                        Url = "https://www.which.co.uk/reviews/supermarkets/article/supermarket-price-comparison-aPpYp9j1MFin",
                        SourceType = "article"
                    }
                }),
            ["M&S Food"] = CreateSupermarketPriceProfile(
                1.18m,
                "Curated chain positioning estimate",
                0.57m,
                isDirectBasketData: false,
                needsReview: true,
                MarksAndSpencerPricingVerifiedUtc,
                "Premium estimate anchored between Waitrose and M&S public value signals",
                new[]
                {
                    new SupermarketPriceEvidence
                    {
                        Title = "M&S Remarksable Value",
                        Url = "https://www.marksandspencer.com/food/l/ways-to-save/remarksable-value",
                        SourceType = "official"
                    },
                    new SupermarketPriceEvidence
                    {
                        Title = "Which? M&S vs Waitrose comparison",
                        Url = "https://www.which.co.uk/news/article/marks-and-spencer-vs-waitrose-how-do-they-compare-ap7jV3X4wJTC",
                        SourceType = "article"
                    },
                    new SupermarketPriceEvidence
                    {
                        Title = "Which? cheapest supermarkets 2026",
                        Url = "https://www.which.co.uk/reviews/supermarkets/article/supermarket-price-comparison-aPpYp9j1MFin",
                        SourceType = "article"
                    }
                })
        };

    private SupermarketPriceResolution ResolveSupermarketPriceProfile(string supermarket)
    {
        var normalizedSupermarket = NormalizeSupermarket(supermarket);
        if (normalizedSupermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            return CreateSupermarketPriceProfile(
                1.00m,
                "Standard estimate",
                0.38m,
                isDirectBasketData: false,
                needsReview: true,
                lastVerifiedUtc: null,
                "No stored chain price profile for custom layouts",
                evidence: []);
        }

        var reviewedProfiles = GetReviewedSupermarketPriceProfiles();
        if (reviewedProfiles.TryGetValue(normalizedSupermarket, out var reviewedProfile))
        {
            return reviewedProfile;
        }

        return CuratedSupermarketPriceProfiles.TryGetValue(normalizedSupermarket, out var profile)
            ? profile
            : CuratedSupermarketPriceProfiles["Tesco"];
    }

    private IReadOnlyDictionary<string, SupermarketPriceResolution> GetReviewedSupermarketPriceProfiles()
    {
        var resolvedPath = ResolveSupermarketPriceProfilesFilePath();
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            return EmptyReviewedSupermarketPriceProfiles;
        }

        try
        {
            var lastWriteUtc = File.GetLastWriteTimeUtc(resolvedPath);
            if (SupermarketPriceFileCache.TryGetValue(resolvedPath, out var cached) &&
                cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Profiles;
            }

            SupermarketPriceFileLoadLock.Wait();
            try
            {
                if (SupermarketPriceFileCache.TryGetValue(resolvedPath, out cached) &&
                    cached.LastWriteUtc == lastWriteUtc)
                {
                    return cached.Profiles;
                }

                var loadedProfiles = LoadReviewedSupermarketPriceProfilesFromFile(resolvedPath);
                SupermarketPriceFileCache[resolvedPath] = new SupermarketPriceFileCacheEntry
                {
                    Path = resolvedPath,
                    LastWriteUtc = lastWriteUtc,
                    Profiles = loadedProfiles
                };
                return loadedProfiles;
            }
            finally
            {
                SupermarketPriceFileLoadLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Unable to load reviewed supermarket price profiles from '{Path}'. Falling back to curated defaults.",
                resolvedPath);
            return EmptyReviewedSupermarketPriceProfiles;
        }
    }

    private IReadOnlyDictionary<string, SupermarketPriceResolution> LoadReviewedSupermarketPriceProfilesFromFile(
        string resolvedPath)
    {
        var fileContent = File.ReadAllText(resolvedPath);
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return EmptyReviewedSupermarketPriceProfiles;
        }

        var payload = JsonSerializer.Deserialize<SupermarketPriceProfilesFilePayload>(fileContent, JsonOptions);
        if (payload?.Profiles is null || payload.Profiles.Count == 0)
        {
            return EmptyReviewedSupermarketPriceProfiles;
        }

        if (payload.Version.HasValue && payload.Version.Value != SupermarketPriceProfilesFileVersion)
        {
            _logger?.LogWarning(
                "Reviewed supermarket price profile file '{Path}' has unsupported version {Version}. Expected {ExpectedVersion}.",
                resolvedPath,
                payload.Version.Value,
                SupermarketPriceProfilesFileVersion);
            return EmptyReviewedSupermarketPriceProfiles;
        }

        var reviewedAtUtc = payload.ReviewedAtUtc;
        var resolvedProfiles = new Dictionary<string, SupermarketPriceResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in payload.Profiles)
        {
            var normalizedSupermarket = NormalizeSupermarket(profile.Supermarket ?? string.Empty);
            if (normalizedSupermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!profile.RelativeCostFactor.HasValue || profile.RelativeCostFactor.Value <= 0m)
            {
                continue;
            }

            var sourceLabel = string.IsNullOrWhiteSpace(profile.SourceLabel)
                ? "Reviewed price file"
                : profile.SourceLabel.Trim();
            var basis = string.IsNullOrWhiteSpace(profile.RelativeCostBasis)
                ? TescoPriceBaselineLabel
                : profile.RelativeCostBasis.Trim();
            var confidenceScore = Math.Clamp(profile.ConfidenceScore ?? 0.6m, 0m, 1m);
            var evidence = NormalizeSupermarketPriceEvidence(profile.Evidence?
                .Select(source => new SupermarketPriceEvidence
                {
                    Title = source.Title?.Trim() ?? string.Empty,
                    Url = source.Url?.Trim() ?? string.Empty,
                    SourceType = source.SourceType?.Trim() ?? string.Empty
                }) ?? []);

            resolvedProfiles[normalizedSupermarket] = new SupermarketPriceResolution
            {
                RelativeCostFactor = NormalizeSupermarketPriceFactor(profile.RelativeCostFactor.Value),
                RelativeCostBasis = basis,
                SourceLabel = sourceLabel,
                ConfidenceScore = confidenceScore,
                ConfidenceLabel = ResolveConfidenceLabel(confidenceScore, profile.ConfidenceLabel),
                IsDirectBasketData = profile.IsDirectBasketData ?? false,
                NeedsReview = profile.NeedsReview ?? false,
                LastVerifiedUtc = profile.LastVerifiedUtc ?? reviewedAtUtc,
                Evidence = evidence
            };
        }

        return resolvedProfiles;
    }

    private string ResolveSupermarketPriceProfilesFilePath()
    {
        var configuredPath = _supermarketPriceProfilesPath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolveCandidateDataFilePath(configuredPath);
        }

        foreach (var candidate in BuildDefaultSupermarketPriceProfilePathCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return BuildDefaultSupermarketPriceProfilePathCandidates().FirstOrDefault() ?? string.Empty;
    }

    private IReadOnlyList<string> BuildDefaultSupermarketPriceProfilePathCandidates()
    {
        var candidates = new List<string>();
        var outputRelativePath = Path.Combine(DefaultSupermarketPriceProfilesOutputDirectory, DefaultSupermarketPriceProfilesFileName);
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, outputRelativePath)));

        if (_webHostEnvironment is not null && !string.IsNullOrWhiteSpace(_webHostEnvironment.ContentRootPath))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(
                _webHostEnvironment.ContentRootPath,
                "..",
                "MyBlog.AislePilot",
                "Data",
                DefaultSupermarketPriceProfilesFileName)));
        }

        candidates.Add(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "MyBlog.AislePilot",
            "Data",
            DefaultSupermarketPriceProfilesFileName)));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveCandidateDataFilePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath))
        };

        if (_webHostEnvironment is not null && !string.IsNullOrWhiteSpace(_webHostEnvironment.ContentRootPath))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(_webHostEnvironment.ContentRootPath, configuredPath)));
        }

        candidates.Add(Path.GetFullPath(configuredPath));

        var distinctCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distinctCandidates.FirstOrDefault(File.Exists) ?? distinctCandidates.First();
    }

    internal static AislePilotSupermarketPriceInsightViewModel BuildPriceInsightViewModel(
        SupermarketPriceResolution resolution)
    {
        return new AislePilotSupermarketPriceInsightViewModel
        {
            SourceLabel = resolution.SourceLabel,
            ConfidenceScore = resolution.ConfidenceScore,
            ConfidenceLabel = resolution.ConfidenceLabel,
            RelativeCostFactor = resolution.RelativeCostFactor,
            RelativeCostBasis = resolution.RelativeCostBasis,
            IsDirectBasketData = resolution.IsDirectBasketData,
            NeedsReview = resolution.NeedsReview,
            LastVerifiedUtc = resolution.LastVerifiedUtc,
            Evidence = resolution.Evidence
                .Select(source => new AislePilotSupermarketPriceEvidenceViewModel
                {
                    Title = source.Title,
                    Url = source.Url,
                    SourceType = source.SourceType
                })
                .ToList()
        };
    }

    internal static string DescribeRelativePriceFactor(decimal relativeCostFactor)
    {
        var normalizedFactor = NormalizeSupermarketPriceFactor(relativeCostFactor);
        var deltaPercent = decimal.Round(Math.Abs((normalizedFactor - 1m) * 100m), 0, MidpointRounding.AwayFromZero);
        if (deltaPercent == 0m)
        {
            return "around Tesco baseline";
        }

        return normalizedFactor > 1m
            ? $"{deltaPercent:0}% above Tesco baseline"
            : $"{deltaPercent:0}% below Tesco baseline";
    }

    private static SupermarketPriceResolution CreateSupermarketPriceProfile(
        decimal relativeCostFactor,
        string sourceLabel,
        decimal confidenceScore,
        bool isDirectBasketData,
        bool needsReview,
        DateTime? lastVerifiedUtc,
        string relativeCostBasis,
        IEnumerable<SupermarketPriceEvidence> evidence)
    {
        var normalizedFactor = NormalizeSupermarketPriceFactor(relativeCostFactor);
        var normalizedConfidence = Math.Clamp(confidenceScore, 0m, 1m);

        return new SupermarketPriceResolution
        {
            RelativeCostFactor = normalizedFactor,
            RelativeCostBasis = relativeCostBasis,
            SourceLabel = sourceLabel,
            ConfidenceScore = normalizedConfidence,
            ConfidenceLabel = ResolveConfidenceLabel(normalizedConfidence),
            IsDirectBasketData = isDirectBasketData,
            NeedsReview = needsReview,
            LastVerifiedUtc = lastVerifiedUtc,
            Evidence = NormalizeSupermarketPriceEvidence(evidence)
        };
    }

    private static IReadOnlyList<SupermarketPriceEvidence> NormalizeSupermarketPriceEvidence(
        IEnumerable<SupermarketPriceEvidence> evidence)
    {
        return evidence
            .Where(source => !string.IsNullOrWhiteSpace(source.Url))
            .Select(source =>
            {
                var normalizedUrl = NormalizeEvidenceUrl(source.Url);
                return string.IsNullOrWhiteSpace(normalizedUrl)
                    ? null
                    : new SupermarketPriceEvidence
                    {
                        Title = string.IsNullOrWhiteSpace(source.Title) ? normalizedUrl : source.Title.Trim(),
                        Url = normalizedUrl,
                        SourceType = NormalizeEvidenceSourceType(source.SourceType)
                    };
            })
            .Where(source => source is not null)
            .Select(source => source!)
            .DistinctBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    internal static decimal NormalizeSupermarketPriceFactor(decimal relativeCostFactor)
    {
        return Math.Clamp(relativeCostFactor, 0.75m, 1.35m);
    }

    private static readonly IReadOnlyDictionary<string, SupermarketPriceResolution> EmptyReviewedSupermarketPriceProfiles =
        new Dictionary<string, SupermarketPriceResolution>(StringComparer.OrdinalIgnoreCase);
}
