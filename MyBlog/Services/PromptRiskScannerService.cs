using System.Text.RegularExpressions;
using MyBlog.Models;

namespace MyBlog.Services;

public class PromptRiskScannerService
{
    private sealed record RiskRule(string Name, int Weight, params string[] Patterns);

    private static readonly RiskRule[] Rules =
    {
        new("Instruction override attempt", 30,
            "ignore previous",
            "disregard above",
            "forget prior",
            "override instructions"),
        new("System prompt extraction attempt", 35,
            "reveal system prompt",
            "show system prompt",
            "print hidden prompt",
            "what are your internal instructions"),
        new("Secret exfiltration attempt", 30,
            "reveal api key",
            "print token",
            "dump secrets",
            "show password"),
        new("Jailbreak role-play pattern", 20,
            "developer mode",
            "do anything now",
            "no restrictions",
            "unfiltered mode"),
        new("Tool abuse or destructive intent", 35,
            "delete database",
            "drop table",
            "rm -rf",
            "run shell command",
            "exfiltrate"),
        new("Obfuscation pattern", 10,
            "base64",
            "rot13",
            "hex decode",
            "unicode escape")
    };

    public PromptRiskEvaluationResult Evaluate(string input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PromptRiskEvaluationResult
            {
                RiskScore = 0,
                RiskLevel = "Low",
                Summary = "No input provided.",
                Recommendation = "Add a prompt to run risk checks."
            };
        }

        var normalized = Normalize(text);
        var matchedRules = new List<string>();
        var score = 0;

        foreach (var rule in Rules)
        {
            if (rule.Patterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal)))
            {
                matchedRules.Add(rule.Name);
                score += rule.Weight;
            }
        }

        var clampedScore = (int)Math.Clamp(score, 0, 100);
        var level = ResolveLevel(clampedScore);

        var summary = matchedRules.Count == 0
            ? "No common injection or exfiltration markers detected."
            : $"Detected {matchedRules.Count} risk marker(s).";

        var recommendation = level switch
        {
            "Critical" => "Block and require manual review.",
            "High" => "Block by default and investigate intent.",
            "Medium" => "Apply stronger guardrails and log for review.",
            _ => "Allow with standard logging."
        };

        return new PromptRiskEvaluationResult
        {
            RiskScore = clampedScore,
            RiskLevel = level,
            MatchedRules = matchedRules,
            Summary = summary,
            Recommendation = recommendation
        };
    }

    private static string ResolveLevel(int riskScore)
    {
        return riskScore switch
        {
            >= 80 => "Critical",
            >= 50 => "High",
            >= 20 => "Medium",
            _ => "Low"
        };
    }

    private static string Normalize(string input)
    {
        var lower = input.ToLowerInvariant();
        return Regex.Replace(lower, @"\s+", " ").Trim();
    }
}
