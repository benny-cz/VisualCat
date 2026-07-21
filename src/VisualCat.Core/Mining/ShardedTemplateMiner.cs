using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Mining;

/// <summary>
/// The template-partition stage of §5.5: a fixed set of single-writer Drain miners, one
/// per tag-hash shard, driven a batch at a time so that masking and clustering leave the
/// commit thread.
/// </summary>
/// <remarks>
/// <para>
/// Mining is stateful and order-sensitive, so §9.4 permits sharding only under two
/// conditions, both enforced here.
/// </para>
/// <para>
/// <b>Clustering must not depend on the shard count.</b> Routing is by tag and every tag
/// owns its own Drain tree, so a tag's entries always meet the same tree in the same
/// source order regardless of how many shards exist. A shard is an execution container
/// for a set of tags, never a clustering boundary.
/// </para>
/// <para>
/// <b>Numbering must not depend on completion order.</b> Shards run concurrently, so the
/// order in which they create clusters is not reproducible. Identities are therefore not
/// assigned by the shards at all: after the parallel pass, <see cref="AssignBatch"/>
/// walks the batch in source order on one thread and numbers each newly seen cluster.
/// That pass is deterministic, so a session imported with one shard and the same session
/// imported with sixteen produce identical template ids.
/// </para>
/// </remarks>
public sealed class ShardedTemplateMiner
{
    private readonly DrainTemplateMiner[] _shards;
    private readonly TemplateSettings _settings;
    private readonly List<DrainTemplateMiner.Cluster> _numbered = [];

    // Reused across batches. AssignBatch is driven by the commit coordinator alone, so
    // these are single-writer scratch, not shared state: allocating a route list per
    // shard plus a cluster array per batch put several hundred bytes per line back onto
    // the ingest path that the rest of the pipeline works to avoid (§19.3).
    private readonly List<int>[] _routes;
    private DrainTemplateMiner.Cluster?[] _clusters = [];
    private MinedEntry[] _scratch = [];
    private uint _nextId = 1;

    public ShardedTemplateMiner(TemplateSettings settings, int shardCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shardCount);
        _settings = settings;
        _shards = new DrainTemplateMiner[shardCount];
        _routes = new List<int>[shardCount];
        for (var index = 0; index < shardCount; index++)
        {
            _shards[index] = new DrainTemplateMiner(settings);
            _routes[index] = [];
        }
    }

    public int ShardCount => _shards.Length;

    /// <summary>Templates mined so far. Cheap enough for the progress cadence.</summary>
    public int TemplateCount => _numbered.Count;

    /// <summary>
    /// Clusters a batch of already source-ordered entries and returns their template ids,
    /// index-aligned with <paramref name="entries"/>. Entries whose <see cref="MinedEntry.Tag"/>
    /// is null are not mined and receive id zero, which §9.3 reserves for meta/unassigned.
    /// </summary>
    public void AssignBatch(ReadOnlySpan<MinedEntry> entries, Span<uint> templateIds)
    {
        if (entries.Length != templateIds.Length)
        {
            throw new ArgumentException("Template id buffer must match the entry count.", nameof(templateIds));
        }

        if (!_settings.Enabled || entries.Length == 0)
        {
            templateIds.Clear();
            return;
        }

        // Route first so each shard sees only its own tags, still in source order.
        foreach (var route in _routes)
        {
            route.Clear();
        }

        for (var index = 0; index < entries.Length; index++)
        {
            if (entries[index].Tag is { } tag)
            {
                _routes[ShardOf(tag, _shards.Length)].Add(index);
            }
        }

        if (_clusters.Length < entries.Length)
        {
            _clusters = new DrainTemplateMiner.Cluster?[entries.Length];
        }

        var clusters = _clusters;
        Array.Clear(clusters, 0, entries.Length);

        // A span cannot cross into the parallel body, so the batch is copied into a
        // reusable array rather than a fresh one per call.
        if (_scratch.Length < entries.Length)
        {
            _scratch = new MinedEntry[entries.Length];
        }

        var buffers = _scratch;
        entries.CopyTo(buffers);
        var routes = _routes;
        var shards = _shards;

        // One task per shard. Shards share nothing: disjoint tag sets mean disjoint
        // trees, cluster objects, and counters, so no lock is needed here.
        Parallel.For(0, shards.Length, shard =>
        {
            var indices = routes[shard];
            if (indices.Count == 0)
            {
                return;
            }

            var miner = shards[shard];
            foreach (var index in indices)
            {
                var entry = buffers[index];
                clusters[index] = miner.MatchCluster(entry.Tag!, entry.Message, entry.Timestamp, entry.EntryId);
            }
        });

        // Source-ordered numbering. This is what makes the identity independent of how
        // the work was scheduled above.
        for (var index = 0; index < entries.Length; index++)
        {
            if (clusters[index] is not { } cluster)
            {
                templateIds[index] = 0;
                continue;
            }

            if (cluster.GlobalId == 0)
            {
                cluster.GlobalId = _nextId++;
                _numbered.Add(cluster);
            }

            templateIds[index] = cluster.GlobalId;
        }
    }

    /// <summary>
    /// Single-entry form, for entries whose final message is only known at commit time —
    /// a long-format record is not complete until its body lines have been read, so it
    /// cannot be mined with the rest of its batch.
    /// </summary>
    public uint AssignOne(MinedEntry entry)
    {
        Span<uint> id = stackalloc uint[1];
        AssignBatch([entry], id);
        return id[0];
    }

    public IReadOnlyList<TemplateDefinition> GetDefinitions()
    {
        var definitions = new TemplateDefinition[_numbered.Count];
        for (var index = 0; index < _numbered.Count; index++)
        {
            definitions[index] = DrainTemplateMiner.Describe(
                _numbered[index],
                _settings.AlgorithmVersion,
                _numbered[index].GlobalId);
        }

        return definitions;
    }

    /// <summary>
    /// FNV-1a over the tag's UTF-16 code units. <see cref="string.GetHashCode()"/> is
    /// randomized per process, and while that would still cluster correctly — routing
    /// only chooses an execution container — a stable hash keeps a given tag on a given
    /// shard across runs, which makes a shard-imbalance diagnostic reproducible.
    /// </summary>
    internal static int ShardOf(string tag, int shardCount)
    {
        if (shardCount == 1)
        {
            return 0;
        }

        var hash = 2166136261u;
        foreach (var character in tag)
        {
            hash = (hash ^ character) * 16777619u;
        }

        return (int)(hash % (uint)shardCount);
    }
}

/// <summary>
/// The four fields Drain clusters on. Carried as a value type so a batch of them is one
/// flat array rather than one allocation per entry (§19.3).
/// </summary>
/// <param name="Tag">Null for outcomes that are not mined, such as meta records.</param>
public readonly record struct MinedEntry(string? Tag, string Message, InstantUs? Timestamp, long EntryId);
