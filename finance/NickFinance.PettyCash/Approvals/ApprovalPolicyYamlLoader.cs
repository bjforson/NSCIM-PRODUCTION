using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NickFinance.PettyCash.Approvals;

/// <summary>
/// Parses a YAML file conforming to <c>PETTY_CASH.md §5.1</c> into an
/// in-memory <see cref="ApprovalPolicy"/>. Hot-reload is a v1.2 concern —
/// for v1.1 callers reload by re-instantiating
/// <see cref="PolicyApprovalEngine"/> with a freshly-loaded policy.
/// </summary>
/// <remarks>
/// <para>
/// Recognised top-level shape:
/// </para>
/// <code>
/// version: "2026-04-26"
/// categories:
///   TRANSPORT:
///     bands:
///       - { max: 20000,  steps: [line_manager] }
///       - { max: 100000, steps: [line_manager, site_supervisor] }
///       - { max: 500000, steps: [line_manager, site_supervisor, finance] }
///   FUEL:
///     bands:
///       - { max: 50000,  steps: [site_supervisor] }
///       - { max: 300000, steps: [site_supervisor, finance] }
/// </code>
/// <para>
/// Unknown extra keys (e.g. <c>defaults</c>, <c>delegation</c>,
/// <c>escalation</c> from the spec) are <em>tolerated</em> — they'll be
/// wired in subsequent versions. Categories are matched against the
/// <see cref="VoucherCategory"/> enum case-insensitively.
/// </para>
/// </remarks>
public static class ApprovalPolicyYamlLoader
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Parse a YAML string. Throws on shape errors with a helpful message.</summary>
    public static ApprovalPolicy Load(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new ArgumentException("YAML content is empty.", nameof(yaml));
        }

        Root? root;
        try
        {
            root = _yaml.Deserialize<Root>(yaml);
        }
        catch (Exception ex)
        {
            throw new PettyCashException($"Failed to parse approval policy YAML: {ex.Message}", ex);
        }
        if (root is null)
        {
            throw new PettyCashException("Approval policy YAML produced an empty document.");
        }

        var version = string.IsNullOrWhiteSpace(root.Version)
            ? throw new PettyCashException("`version` is required at the top level of the approval policy.")
            : root.Version!.Trim();

        if (root.Categories is null || root.Categories.Count == 0)
        {
            throw new PettyCashException("`categories` is required and must contain at least one entry.");
        }

        var built = new Dictionary<VoucherCategory, IReadOnlyList<ApprovalBand>>();
        foreach (var (rawName, catBody) in root.Categories)
        {
            if (!Enum.TryParse<VoucherCategory>(rawName, ignoreCase: true, out var cat))
            {
                throw new PettyCashException(
                    $"Unknown category '{rawName}' in approval policy. Known: {string.Join(", ", Enum.GetNames<VoucherCategory>())}.");
            }
            if (catBody?.Bands is null || catBody.Bands.Count == 0)
            {
                throw new PettyCashException($"Category '{rawName}' has no bands.");
            }

            var bands = new List<ApprovalBand>(catBody.Bands.Count);
            long? lastMax = null;
            foreach (var b in catBody.Bands)
            {
                if (b.Max <= 0)
                {
                    throw new PettyCashException($"Band in category '{rawName}' has non-positive max ({b.Max}).");
                }
                if (b.Steps is null || b.Steps.Count == 0)
                {
                    throw new PettyCashException($"Band in category '{rawName}' (max {b.Max}) has no steps.");
                }
                if (lastMax is not null && b.Max <= lastMax.Value)
                {
                    throw new PettyCashException(
                        $"Bands in category '{rawName}' must be in ascending `max` order; got {lastMax} -> {b.Max}.");
                }
                bands.Add(new ApprovalBand(b.Max, b.Steps.Select(s => s.Trim()).ToList()));
                lastMax = b.Max;
            }
            built[cat] = bands;
        }

        return new ApprovalPolicy(version, built);
    }

    /// <summary>Parse a YAML file by path.</summary>
    public static ApprovalPolicy LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    // ---------------------------------------------------------------------
    // YAML shape DTOs (private; never leak into the public surface).
    // ---------------------------------------------------------------------

    private sealed class Root
    {
        public string? Version { get; set; }
        public Dictionary<string, CategoryBody>? Categories { get; set; }
    }

    private sealed class CategoryBody
    {
        public List<BandBody>? Bands { get; set; }
    }

    private sealed class BandBody
    {
        public long Max { get; set; }
        public List<string>? Steps { get; set; }
    }
}
