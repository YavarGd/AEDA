using System.Text.RegularExpressions;
using PersonalAI.Core.Research;

namespace PersonalAI.Infrastructure.Research;

public sealed class DeterministicClaimExtractionService : IClaimExtractionService
{
    private const int MaxClaimCharacters = 500;

    private static readonly Regex SentenceSplitter = new(
        @"(?<=[.!?])\s+|\r?\n+",
        RegexOptions.Compiled);

    public Task<IReadOnlyList<ResearchClaim>> ExtractClaimsAsync(
        ClaimExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult<IReadOnlyList<ResearchClaim>>([]);
        }

        var maxClaims = Math.Clamp(request.MaxClaims, 1, 20);
        var claims = new List<ResearchClaim>();
        foreach (var candidate in SentenceSplitter
                     .Split(request.Text)
                     .Select(Normalize)
                     .Where(text => text.Length > 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claims.Count >= maxClaims)
            {
                break;
            }

            claims.Add(new ResearchClaim(
                ResearchClaimId.NewId(),
                Bound(candidate, MaxClaimCharacters),
                Classify(candidate),
                DateTimeOffset.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<ResearchClaim>>(claims);
    }

    private static ResearchClaimKind Classify(string text)
    {
        var lower = text.ToLowerInvariant();
        if (ContainsAny(lower, "i think", "i feel", "best", "worst", "beautiful", "better than", "prefer"))
        {
            return ResearchClaimKind.SubjectiveOpinion;
        }

        if (ContainsAny(lower, "maybe", "probably", "could be", "might be") ||
            text.EndsWith("?", StringComparison.Ordinal))
        {
            return ResearchClaimKind.Unverifiable;
        }

        if (ContainsAny(lower, "today", "yesterday", "tomorrow", "latest", "current", "now ", "2025", "2026"))
        {
            return ResearchClaimKind.CurrentOrTimeSensitive;
        }

        if (ContainsAny(lower, ".cs", ".csproj", "method", "class", "build", "test", "compile", "repository"))
        {
            return ResearchClaimKind.CodeRelated;
        }

        if (ContainsAny(lower, "document", "source", "citation", "file", "pdf", "report"))
        {
            return ResearchClaimKind.DocumentOrSourceRelated;
        }

        return ResearchClaimKind.Factual;
    }

    private static bool ContainsAny(string text, params string[] signals) =>
        signals.Any(signal => text.Contains(signal, StringComparison.Ordinal));

    private static string Normalize(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

    private static string Bound(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters];
}
