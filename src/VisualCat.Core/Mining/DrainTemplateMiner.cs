using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Mining;

public sealed class DrainTemplateMiner
{
    private const string Wildcard = "<*>";

    private static readonly (Regex Pattern, string Replacement)[] DefaultMasks =
    [
        (Create(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b"), Wildcard),
        (Create(@"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b"), Wildcard),
        (Create(@"\b(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}\b"), Wildcard),
        (Create(@"\b0x[0-9A-Fa-f]+\b"), Wildcard),
        (Create(@"\b\d{2,4}[-/]\d{2}[-/]\d{2,4}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?\b"), Wildcard),
        (Create(@"\b\d+(?:\.\d+)?\s*(?:us|µs|ms|s|sec|seconds|minutes|hours)\b"), Wildcard),
        (Create(@"/\d+/"), $"/{Wildcard}/"),
        (Create(@"/\d+$"), $"/{Wildcard}"),
        (Create(@"\b-?\d{4,}\b"), Wildcard),
    ];

    private readonly TemplateSettings _settings;
    private readonly Dictionary<string, TagState> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, Cluster> _clustersById = [];

    // Clusters in the order this miner created them. A sharded owner assigns the
    // session-global identity itself (in source order), so the list — not the id — is
    // what this instance can report on its own.
    private readonly List<Cluster> _created = [];

    // Reused across assignments: one miner is only ever driven by a single thread, so one
    // scratch buffer removes a per-entry array allocation from the ingest path.
    private readonly List<Range> _tokenRanges = new(64);
    private uint _nextId = 1;

    public DrainTemplateMiner(TemplateSettings settings) => _settings = settings;

    /// <summary>
    /// Hot ingest path: assigns the entry to a cluster and returns only its identity.
    /// The committer stores nothing else, so the canonical text and extracted parameters
    /// that <see cref="Assign"/> materialises are not rebuilt once per entry.
    /// </summary>
    public uint AssignId(NormalizedEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AssignId(entry.Tag, entry.Message, entry.Timestamp, entry.EntryId);
    }

    /// <summary>
    /// Field-level overload used by the commit coordinator. Clustering reads only these
    /// four values, so taking them directly lets the committer assign the template before
    /// it constructs the entry — the entry-shaped overload forced it to build one
    /// <see cref="NormalizedEntry"/>, mine it, then allocate a second copy that differed
    /// only in <c>TemplateId</c>, once per line in the session (§19.3).
    /// </summary>
    public uint AssignId(string tag, string message, InstantUs? timestamp, long entryId) =>
        _settings.Enabled ? Match(tag, message, timestamp, entryId, null)?.Id ?? 0u : 0u;

    /// <summary>
    /// Clusters one entry and returns the cluster itself rather than an identity. Used by
    /// <see cref="ShardedTemplateMiner"/>, which owns the session-global numbering so that
    /// identities stay ordered by first appearance in the source no matter how many shards
    /// ran (§9.4).
    /// </summary>
    internal Cluster? MatchCluster(string tag, string message, InstantUs? timestamp, long entryId) =>
        _settings.Enabled ? Match(tag, message, timestamp, entryId, null) : null;

    /// <summary>
    /// Stops this shard from minting clusters after its owner spends the session-wide
    /// budget. Existing clusters continue matching and accumulating evidence.
    /// </summary>
    internal bool PreventNewClusters { get; set; }

    /// <summary>Clusters this miner created, in creation order.</summary>
    internal IReadOnlyList<Cluster> CreatedClusters => _created;

    public TemplateAssignment Assign(NormalizedEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_settings.Enabled)
        {
            return new TemplateAssignment(0, string.Empty, []);
        }

        var parameters = new List<string>();
        var cluster = Match(entry.Tag, entry.Message, entry.Timestamp, entry.EntryId, parameters);
        if (cluster is null)
        {
            return new TemplateAssignment(0, string.Empty, []);
        }

        return new TemplateAssignment(cluster.Id, string.Join(' ', cluster.Tokens), parameters);
    }

    /// <summary>
    /// Drain routing: token count, then a fixed-depth prefix descent, then the most
    /// similar cluster at the leaf (§9.3). State is keyed by tag — never by shard — so
    /// clustering cannot depend on worker configuration (§9.4).
    /// </summary>
    private Cluster? Match(string tag, string message, InstantUs? entryTimestamp, long entryId, List<string>? parameters)
    {
        var masked = ApplyMasks(message);
        var tokens = _tokenRanges;
        tokens.Clear();
        Tokenize(masked, tokens);

        if (!_tags.TryGetValue(tag, out var tagState))
        {
            tagState = new TagState();
            _tags.Add(tag, tagState);
        }

        if (!tagState.ByTokenCount.TryGetValue(tokens.Count, out var root))
        {
            root = new Node();
            tagState.ByTokenCount.Add(tokens.Count, root);
        }

        var leaf = Descend(root, masked, tokens);
        var best = SelectCluster(leaf, tagState, masked, tokens);
        if (best is null)
        {
            return null;
        }

        if (Generalize(best, masked, tokens, parameters))
        {
            best.ShapeRevision++;
        }

        best.Count++;
        if (entryTimestamp is { } timestamp)
        {
            best.First = best.First is null || timestamp < best.First ? timestamp : best.First;
            best.Last = best.Last is null || timestamp > best.Last ? timestamp : best.Last;
        }

        if (best.Examples.Count < _settings.RepresentativeExamples)
        {
            best.Examples.Add(entryId);
        }

        best.Revision++;

        return best;
    }

    private Node Descend(Node root, string masked, List<Range> tokens)
    {
        var node = root;
        var depth = Math.Min(_settings.Depth, tokens.Count);
        for (var level = 0; level < depth; level++)
        {
            var token = masked.AsSpan(tokens[level]);

            // §9.3 step 5: tokens that obviously carry a value route through the
            // wildcard branch, so one path cannot be created per observed value.
            if (token.ContainsAnyInRange('0', '9'))
            {
                node = node.WildcardChild ??= new Node();
                continue;
            }

            // Consecutive log lines overwhelmingly share their leading tokens, so a
            // one-entry memo answers most descents with a single span comparison and
            // avoids building the span lookup (and hashing the token) every level.
            if (node.LastKey is { } lastKey && token.Equals(lastKey, StringComparison.Ordinal))
            {
                node = node.LastChild!;
                continue;
            }

            var lookup = node.Children.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(token, out var child))
            {
                node.LastKey = child.Key;
                node.LastChild = child;
                node = child;
                continue;
            }

            // §9.3 step 8: bounded fan-out. Past the limit, new prefixes share the
            // wildcard branch instead of growing the tree without bound.
            if (node.Children.Count >= _settings.MaximumChildren)
            {
                node = node.WildcardChild ??= new Node();
                continue;
            }

            child = new Node { Key = token.ToString() };
            node.Children.Add(child.Key, child);
            node.LastKey = child.Key;
            node.LastChild = child;
            node = child;
        }

        return node;
    }

    private Cluster? SelectCluster(Node leaf, TagState tagState, string masked, List<Range> tokens)
    {
        Cluster? best = null;
        var bestSimilarity = -1d;
        foreach (var candidate in leaf.Clusters)
        {
            var similarity = Similarity(candidate.Tokens, masked, tokens);

            // Strictly greater keeps the lowest-numbered cluster on a tie, which is what
            // makes replay order-independent for identical input.
            if (similarity > bestSimilarity)
            {
                best = candidate;
                bestSimilarity = similarity;
            }
        }

        if (best is not null && bestSimilarity >= _settings.SimilarityThreshold)
        {
            return best;
        }

        if (PreventNewClusters)
        {
            return best;
        }

        if (tagState.ClusterCount >= _settings.MaximumClustersPerTag)
        {
            // The per-tag budget is spent. Absorbing into an existing cluster keeps
            // memory bounded and stays deterministic; an unbounded tail of one-off
            // clusters would satisfy neither (§9.6).
            return best ?? CreateCluster(leaf, tagState, masked, tokens);
        }

        return CreateCluster(leaf, tagState, masked, tokens);
    }

    private Cluster CreateCluster(Node leaf, TagState tagState, string masked, List<Range> tokens)
    {
        var materialized = new string[tokens.Count];
        for (var index = 0; index < tokens.Count; index++)
        {
            materialized[index] = masked[tokens[index]];
        }

        var created = new Cluster(_nextId++, materialized);
        leaf.Clusters.Add(created);
        tagState.ClusterCount++;
        _clustersById.Add(created.Id, created);
        _created.Add(created);
        return created;
    }

    private static bool Generalize(Cluster cluster, string masked, List<Range> tokens, List<string>? parameters)
    {
        var changed = false;
        // A forced absorption can land on a cluster of a different length; only the
        // positions the two share can generalize.
        var shared = Math.Min(cluster.Tokens.Length, tokens.Count);
        for (var index = 0; index < shared; index++)
        {
            var token = masked.AsSpan(tokens[index]);
            if (!token.Equals(cluster.Tokens[index], StringComparison.Ordinal))
            {
                changed |= cluster.Tokens[index] != Wildcard;
                cluster.Tokens[index] = Wildcard;
                parameters?.Add(token.ToString());
            }
            else if (cluster.Tokens[index] == Wildcard)
            {
                parameters?.Add(token.ToString());
            }
        }

        return changed;
    }

    private static double Similarity(string[] template, string masked, List<Range> tokens)
    {
        if (template.Length != tokens.Count)
        {
            return 0;
        }

        if (template.Length == 0)
        {
            return 1;
        }

        var equal = 0;
        for (var index = 0; index < template.Length; index++)
        {
            if (template[index] == Wildcard ||
                masked.AsSpan(tokens[index]).Equals(template[index], StringComparison.Ordinal))
            {
                equal++;
            }
        }

        return equal / (double)template.Length;
    }

    /// <summary>
    /// Splits on whitespace into ranges over <paramref name="masked"/>. Token strings
    /// are materialised only when a cluster is created, not once per entry.
    /// </summary>
    private static void Tokenize(string masked, List<Range> tokens)
    {
        var index = 0;
        while (index < masked.Length)
        {
            while (index < masked.Length && char.IsWhiteSpace(masked[index]))
            {
                index++;
            }

            if (index >= masked.Length)
            {
                break;
            }

            var start = index;
            while (index < masked.Length && !char.IsWhiteSpace(masked[index]))
            {
                index++;
            }

            tokens.Add(new Range(start, index));
        }
    }

    /// <summary>
    /// Number of clusters mined so far. Progress publication needs the count on a
    /// cadence measured in milliseconds; materializing every definition to obtain it
    /// would hash and re-serialize the whole template table on each tick.
    /// </summary>
    public int TemplateCount => _clustersById.Count;

    public IReadOnlyList<TemplateDefinition> GetDefinitions() =>
        _clustersById.Values
            .OrderBy(static cluster => cluster.Id)
            .Select(cluster => Describe(cluster, _settings.AlgorithmVersion, cluster.Id))
            .ToArray();

    internal static TemplateDefinition Describe(Cluster cluster, string algorithmVersion, uint id)
    {
        if (cluster.CachedDefinition is { } cached &&
            cluster.CachedRevision == cluster.Revision &&
            cluster.CachedId == id &&
            string.Equals(cluster.CachedAlgorithmVersion, algorithmVersion, StringComparison.Ordinal))
        {
            return cached;
        }

        var canonical = string.Join(' ', cluster.Tokens);
        var definition = new TemplateDefinition(
            id,
            canonical,
            "drain",
            algorithmVersion,
            cluster.Tokens.ToArray(),
            cluster.First,
            cluster.Last,
            cluster.Count,
            cluster.Examples.ToArray(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        cluster.CachedRevision = cluster.Revision;
        cluster.CachedId = id;
        cluster.CachedAlgorithmVersion = algorithmVersion;
        cluster.CachedDefinition = definition;
        return definition;
    }

    [Flags]
    private enum MessageShape
    {
        None = 0,
        Digit = 1 << 0,
        Dash = 1 << 1,
        Dot = 1 << 2,
        Colon = 1 << 3,
        LetterX = 1 << 4,
        LetterS = 1 << 5,
        Slash = 1 << 6,
        FourDigitRun = 1 << 7,
    }

    /// <summary>
    /// Characters each mask cannot match without. Gating on these lets most messages
    /// skip most rules: running all nine unconditionally cost roughly 2 microseconds
    /// per message and dominated template mining.
    /// </summary>
    private static readonly MessageShape[] MaskPreconditions =
    [
        MessageShape.Dash | MessageShape.Digit,                          // UUID (third group starts [1-5])
        MessageShape.Dot | MessageShape.Digit,                           // IPv4
        MessageShape.Colon,                                              // MAC — may be all hex letters
        MessageShape.LetterX | MessageShape.Digit,                       // 0x…
        MessageShape.Colon | MessageShape.Digit,                         // timestamp
        MessageShape.LetterS | MessageShape.Digit,                       // duration units all contain 's'
        MessageShape.Slash | MessageShape.Digit,                         // /123/
        MessageShape.Slash | MessageShape.Digit,                         // /123 at end
        MessageShape.FourDigitRun,                                       // long standalone integer
    ];

    internal static string ApplyMasks(string message)
    {
        var shape = Classify(message);
        var result = message;
        for (var rule = 0; rule < DefaultMasks.Length; rule++)
        {
            // Skipping is sound in both directions: the precondition is necessary for
            // the rule to match, and masking only ever removes characters, so a rule
            // ruled out on the original message stays ruled out on the intermediate.
            if ((shape & MaskPreconditions[rule]) != MaskPreconditions[rule])
            {
                continue;
            }

            result = DefaultMasks[rule].Pattern.Replace(result, DefaultMasks[rule].Replacement);
        }

        return result;
    }

    private static MessageShape Classify(string message)
    {
        var shape = MessageShape.None;
        var digitRun = 0;
        foreach (var character in message)
        {
            if (char.IsAsciiDigit(character))
            {
                shape |= MessageShape.Digit;
                if (++digitRun >= 4)
                {
                    shape |= MessageShape.FourDigitRun;
                }

                continue;
            }

            digitRun = 0;
            switch (character)
            {
                case '-': shape |= MessageShape.Dash; break;
                case '.': shape |= MessageShape.Dot; break;
                case ':': shape |= MessageShape.Colon; break;
                case 'x' or 'X': shape |= MessageShape.LetterX; break;
                case 's' or 'S': shape |= MessageShape.LetterS; break;
                case '/': shape |= MessageShape.Slash; break;
                default: break;
            }
        }

        return shape;
    }

    /// <summary>
    /// The original rule-at-a-time masker, retained as the equivalence oracle for
    /// <see cref="ApplyMasks"/> so the combined pattern cannot drift unnoticed.
    /// </summary>
    internal static string ApplyMasksSequentially(string message)
    {
        var result = message;
        foreach (var mask in DefaultMasks)
        {
            result = mask.Pattern.Replace(result, mask.Replacement);
        }

        return result;
    }

    private static string[] Tokenize(string message) =>
        message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static double Similarity(string[] template, string[] candidate)
    {
        if (template.Length != candidate.Length)
        {
            return 0;
        }

        if (template.Length == 0)
        {
            return 1;
        }

        var equal = 0;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == Wildcard || string.Equals(template[i], candidate[i], StringComparison.Ordinal))
            {
                equal++;
            }
        }

        return equal / (double)template.Length;
    }

    /// <summary>
    /// Log text is untrusted input, so masks run on the non-backtracking engine under a
    /// timeout. The timeout must also leave room for the engine's first-use setup on
    /// slower hosts; 100 ms proved too tight on a cold macOS runner even for a tiny
    /// input. Non-backtracking keeps match time linear while the upstream line-size
    /// limit and this one-second ceiling bound total work.
    /// </summary>
    private static Regex Create(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Per-tag mining state. Keying by tag rather than by shard is the precondition
    /// that makes template output independent of worker count (§9.4).
    /// </summary>
    private sealed class TagState
    {
        public Dictionary<int, Node> ByTokenCount { get; } = [];

        /// <summary>Clusters across every token-count tree for this tag.</summary>
        public int ClusterCount { get; set; }
    }

    /// <summary>One fixed-depth prefix node; clusters live at the depth limit.</summary>
    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public Node? WildcardChild { get; set; }
        public List<Cluster> Clusters { get; } = [];

        /// <summary>Token that selected this node from its parent.</summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>Most recently taken branch; a pure lookup accelerator.</summary>
        public string? LastKey { get; set; }

        public Node? LastChild { get; set; }
    }

    internal sealed class Cluster(uint id, string[] tokens)
    {
        /// <summary>Identity within the single miner that created this cluster.</summary>
        public uint Id { get; } = id;

        /// <summary>
        /// Session-global identity, or zero while unassigned. Shards create clusters
        /// concurrently, so the order in which they do so is not reproducible; the
        /// sharded owner therefore ignores <see cref="Id"/> and stamps this field during
        /// its source-ordered pass, which is what keeps template numbering independent of
        /// the shard count (§9.4, §9.5).
        /// </summary>
        public uint GlobalId { get; set; }

        public string[] Tokens { get; } = tokens;
        public long Count { get; set; }
        public InstantUs? First { get; set; }
        public InstantUs? Last { get; set; }
        public List<long> Examples { get; } = [];
        public long Revision { get; set; }
        /// <summary>
        /// Changes only when the persisted canonical shape changes. Counts, time bounds,
        /// and examples receive one authoritative revision at finalization instead of
        /// making every live publication rewrite every hot template.
        /// </summary>
        public long ShapeRevision { get; set; } = 1;
        public long PublishedShapeRevision { get; set; }
        public long CachedRevision { get; set; } = -1;
        public uint CachedId { get; set; }
        public string? CachedAlgorithmVersion { get; set; }
        public TemplateDefinition? CachedDefinition { get; set; }
    }
}
