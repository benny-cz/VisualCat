using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Configuration;
using VisualCat.Infrastructure.Diagnostics;
using VisualCat.Infrastructure.Files;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

public sealed class SessionPersistenceTests
{
    private const string Log =
        "05-15 14:13:37.496  1073  1151 D TagA: alpha 1000\n" +
        "05-15 14:13:37.498  1073  1152 W TagB: beta 2000\n";

    [Fact]
    public async Task SavedViewsRoundTripAndMalformedFilesFallBackSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-views-{Guid.NewGuid():N}.vcat");
        Directory.CreateDirectory(root);
        try
        {
            var store = new SessionViewStore(root);
            var range = new TimeRange(new InstantUs(100), new InstantUs(200));
            var filter = new FilterSpec
            {
                IncludedLevels = ImmutableHashSet.Create(LogLevel.Warn),
                IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagB"),
                Search = new TextSearchSpec("beta", CaseSensitive: true),
            };
            var active = new SessionViewState("Last view", range, filter, EntryOrder.SourceSequence, true);
            var preset = active with { Name = "Warnings" };
            await store.SaveAsync(active, [preset]);

            var restored = await store.LoadAsync();
            Assert.NotNull(restored.Active);
            Assert.Equal(active.Name, restored.Active.Name);
            Assert.Equal(active.Viewport, restored.Active.Viewport);
            Assert.Equal(active.Filter.Fingerprint(), restored.Active.Filter.Fingerprint());
            Assert.Equal(active.EntryOrder, restored.Active.EntryOrder);
            Assert.Equal(active.FollowLatest, restored.Active.FollowLatest);
            Assert.Equal(preset.Name, Assert.Single(restored.Presets).Name);

            await File.WriteAllTextAsync(Path.Combine(root, "view.json"), "{not-json");
            var malformed = await store.LoadAsync();
            Assert.Null(malformed.Active);
            Assert.Empty(malformed.Presets);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StandardAndPortableSaveAreAtomicAndVerifierBacked()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"visualcat-save-source-{Guid.NewGuid():N}.vcat");
        var standard = Path.Combine(Path.GetTempPath(), $"visualcat-save-standard-{Guid.NewGuid():N}.vcat");
        var portable = Path.Combine(Path.GetTempPath(), $"visualcat-save-portable-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), [11]);
        var result = await SessionCoordinator.ImportAsync(source, sourceRoot, Settings());
        try
        {
            var viewStore = new SessionViewStore(sourceRoot);
            await viewStore.SaveAsync(
                new SessionViewState("Last view", result.Snapshot.TimedRange, FilterSpec.All, EntryOrder.Chronological, false),
                []);

            await SessionSaveService.SaveAsync(result.Snapshot, standard, portable: false);
            await PortableSessionService.SavePortableAsync(result.Snapshot, portable);

            var standardReport = await SessionVerifier.VerifyAsync(standard);
            var portableReport = await SessionVerifier.VerifyAsync(portable);
            Assert.True(standardReport.IsValid);
            Assert.True(portableReport.IsValid);
            Assert.True(File.Exists(Path.Combine(standard, "view.json")));
            Assert.True(File.Exists(Path.Combine(portable, "raw.log")));
            using var portableSnapshot = await SessionStore.OpenAsync(portable);
            Assert.True(portableSnapshot.Manifest.Source.Embedded);
            Assert.Null(portableSnapshot.Manifest.Source.Path);
            await Assert.ThrowsAsync<IOException>(() =>
                SessionSaveService.SaveAsync(result.Snapshot, standard, portable: false));
            await Assert.ThrowsAsync<IOException>(() =>
                SessionSaveService.SaveAsync(result.Snapshot, Path.Combine(sourceRoot, "nested.vcat"), portable: false));
        }
        finally
        {
            result.Snapshot.Dispose();
            DeleteIfPresent(sourceRoot);
            DeleteIfPresent(standard);
            DeleteIfPresent(portable);
        }
    }

    /// <summary>
    /// A portable save embeds the session's recorded prefix, not whatever the external file
    /// holds at save time, and refuses outright when that prefix no longer matches.
    /// </summary>
    /// <remarks>
    /// Indexing a capture that is still being written to is the ordinary workflow, so the
    /// append case has to keep working (ADR 0020). Copying the file wholesale embedded bytes
    /// the session never indexed and then failed the copy's own verification, reporting
    /// damaged embedded evidence for a source that was merely longer.
    /// </remarks>
    [Fact]
    public async Task PortableSaveEmbedsTheRecordedPrefixOfAGrownSourceAndRefusesARewrittenOne()
    {
        var container = Path.Combine(Path.GetTempPath(), $"visualcat-portable-prefix-{Guid.NewGuid():N}");
        var logPath = Path.Combine(container, "capture.txt");
        var sourceRoot = Path.Combine(container, "capture.vcat");
        var portable = Path.Combine(container, "portable.vcat");
        var refused = Path.Combine(container, "refused.vcat");
        Directory.CreateDirectory(container);
        SessionSnapshot? snapshot = null;
        try
        {
            await File.WriteAllTextAsync(logPath, Log);
            await using (var source = new FileLogSource(logPath))
            {
                var result = await SessionCoordinator.ImportAsync(
                    source,
                    sourceRoot,
                    Settings() with { PortableRaw = false });
                snapshot = result.Snapshot;
            }

            await File.AppendAllTextAsync(
                logPath,
                "05-15 14:13:37.500  1073  1153 I TagC: gamma 3000\n");

            await PortableSessionService.SavePortableAsync(snapshot, portable);
            Assert.Equal(Log, await File.ReadAllTextAsync(Path.Combine(portable, "raw.log")));
            Assert.True((await SessionVerifier.VerifyAsync(portable)).IsValid);

            await File.WriteAllTextAsync(logPath, "X" + Log[1..]);
            var failure = await Assert.ThrowsAsync<RawEvidenceException>(
                () => PortableSessionService.SavePortableAsync(snapshot, refused));
            Assert.Contains("no longer matches", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(refused));
        }
        finally
        {
            snapshot?.Dispose();
            DeleteIfPresent(container);
        }
    }

    [Fact]
    public async Task TemporaryCleanupIsOptInAgeAndSizeBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var old = CreateFakeSession(root, "old.vcat", now.AddDays(-40), 10);
            var middle = CreateFakeSession(root, "middle.vcat", now.AddDays(-2), 20);
            var newest = CreateFakeSession(root, "newest.vcat", now.AddHours(-1), 30);
            Directory.CreateDirectory(Path.Combine(root, "not-a-session"));

            var disabled = await TemporarySessionRetentionService.CleanupAsync(
                root,
                enabled: false,
                TimeSpan.FromDays(30),
                maximumTotalBytes: null,
                now);
            Assert.Empty(disabled.DeletedPaths);
            Assert.True(Directory.Exists(old));
            var newestSize = Assert.Single(
                disabled.Sessions,
                session => string.Equals(session.Path, newest, StringComparison.OrdinalIgnoreCase)).SizeBytes;

            var aged = await TemporarySessionRetentionService.CleanupAsync(
                root,
                enabled: true,
                TimeSpan.FromDays(30),
                maximumTotalBytes: newestSize,
                now);
            Assert.Contains(old, aged.DeletedPaths);
            Assert.Contains(middle, aged.DeletedPaths);
            Assert.True(Directory.Exists(newest));
            Assert.True(Directory.Exists(Path.Combine(root, "not-a-session")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task TemporaryCleanupPreviewSeparatesPolicyReasonsAndProtectsOpenWork()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-retention-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var open = CreateFakeSession(root, "open.vcat", now.AddDays(-40), 10);
            var capturing = CreateFakeSession(root, "capturing.vcat", now.AddDays(-10), 20);
            var middle = CreateFakeSession(root, "middle.vcat", now.AddDays(-2), 30);
            var newest = CreateFakeSession(root, "newest.vcat", now.AddHours(-1), 40);

            var none = await TemporarySessionRetentionService.PreviewAsync(
                root, TimeSpan.FromDays(365), null, now);
            Assert.Empty(none);

            var ageOnly = await TemporarySessionRetentionService.PreviewAsync(
                root, TimeSpan.FromDays(30), null, now);
            Assert.Equal([open], ageOnly.Select(static session => session.Path));

            var scanned = await TemporarySessionRetentionService.ScanAsync(root);
            var newestSize = Assert.Single(
                scanned,
                session => string.Equals(session.Path, newest, StringComparison.OrdinalIgnoreCase)).SizeBytes;
            var sizeOnly = await TemporarySessionRetentionService.PreviewAsync(
                root, TimeSpan.FromDays(365), newestSize, now);
            Assert.Equal(
                new[] { open, capturing, middle }.OrderBy(static path => path),
                sizeOnly.Select(static session => session.Path).OrderBy(static path => path));

            var protectedPaths = new HashSet<string>([open, capturing], StringComparer.OrdinalIgnoreCase);
            var protectedPreview = await TemporarySessionRetentionService.PreviewAsync(
                root, TimeSpan.FromDays(1), null, now, protectedPaths);
            Assert.Equal([middle], protectedPreview.Select(static session => session.Path));

            var cleaned = await TemporarySessionRetentionService.CleanupAsync(
                root,
                enabled: true,
                TimeSpan.FromDays(1),
                maximumTotalBytes: null,
                now,
                protectedPaths);
            Assert.Equal([middle], cleaned.DeletedPaths);
            Assert.True(Directory.Exists(open));
            Assert.True(Directory.Exists(capturing));
            Assert.True(Directory.Exists(newest));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExactSessionDeletionCannotEscapeTheCacheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-exact-delete-{Guid.NewGuid():N}");
        var outsideRoot = Path.Combine(Path.GetTempPath(), $"visualcat-exact-delete-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var session = CreateFakeSession(root, "chosen.vcat", DateTimeOffset.UtcNow, 10);
            var outside = CreateFakeSession(outsideRoot, "outside.vcat", DateTimeOffset.UtcNow, 10);

            Assert.Throws<IOException>(() =>
                TemporarySessionRetentionService.DeleteExactSession(root, outside));
            Assert.True(Directory.Exists(outside));

            TemporarySessionRetentionService.DeleteExactSession(root, session);
            Assert.False(Directory.Exists(session));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outsideRoot, true);
        }
    }

    [Fact]
    public async Task DiagnosticsRollAndBundleExcludeSensitiveValuesAndRawMessages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-diagnostics-{Guid.NewGuid():N}");
        var logs = Path.Combine(root, "logs");
        var session = Path.Combine(root, "session.vcat");
        var bundle = Path.Combine(root, "bundle.zip");
        // A fixture value that must never reach a diagnostic bundle.
        const string secret = "TOP-SECRET-VISUALCAT-VALUE"; // gitleaks:allow
        Directory.CreateDirectory(root);
        try
        {
            await using (var logger = new RollingDiagnosticLogger(logs, maximumFileBytes: 64 * 1024, retainedFiles: 3))
            {
                await logger.WriteAsync(new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    "information",
                    "test",
                    "bundle",
                    Guid.NewGuid(),
                    1,
                    new Dictionary<string, string>
                    {
                        ["sourcePath"] = $@"C:\private\{secret}.txt",
                        ["message"] = secret,
                        ["safeCount"] = "42",
                    }));
            }

            await using (var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log + secret + "\n"), [31]))
            {
                var result = await SessionCoordinator.ImportAsync(source, session, Settings());
                result.Snapshot.Dispose();
            }

            await DiagnosticBundleService.CreateAsync(logs, bundle, [session]);
            using var archive = ZipFile.OpenRead(bundle);
            Assert.Contains(archive.Entries, entry => entry.FullName == "SENSITIVE-DATA-WARNING.txt");
            Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("sessions/", StringComparison.Ordinal));
            foreach (var entry in archive.Entries.Where(static entry => entry.Length > 0))
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = await reader.ReadToEndAsync();
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ApplicationSettingsAreValidatedAndRemainHumanReadable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(path);
            var rememberedSessions = Enumerable.Range(0, 14)
                .Select(index => Path.Combine(root, $"session-{index:D2}"))
                .Concat([Path.Combine(root, "session-00"), " "])
                .ToArray();
            await store.SaveAsync(new ApplicationSettings(
                Theme: "invalid",
                TextScale: 99,
                DefaultCaptureBuffers: ["unknown"],
                DefaultCapturePreRollSeconds: 99_999,
                UiRefreshLimit: 0,
                IntensityScale: "invalid",
                ExportOrder: "invalid",
                ExportEncoding: "invalid",
                TemporaryRetentionDays: 0,
                WindowWidth: 1,
                WindowHeight: 1,
                LiveCaptureNoticeAcknowledged: true,
                OpenSessionPaths: rememberedSessions,
                OpenSessionIndex: 99));
            var loaded = await store.LoadAsync();

            Assert.Equal("System", loaded.Theme);
            Assert.Equal(2, loaded.TextScale);
            Assert.Equal(["main", "system", "crash"], loaded.DefaultCaptureBuffers!);
            Assert.Equal(3600, loaded.DefaultCapturePreRollSeconds);
            Assert.Equal(1, loaded.UiRefreshLimit);
            Assert.Equal("Logarithmic", loaded.IntensityScale);
            Assert.Equal("SourceSequence", loaded.ExportOrder);
            Assert.Equal("utf-8-bom", loaded.ExportEncoding);
            Assert.Equal(900, loaded.WindowWidth);
            Assert.Equal(600, loaded.WindowHeight);
            Assert.True(loaded.LiveCaptureNoticeAcknowledged);
            Assert.Equal(12, loaded.OpenSessionPaths!.Length);
            Assert.Equal(rememberedSessions.Take(12), loaded.OpenSessionPaths);
            Assert.Equal(11, loaded.OpenSessionIndex);
            Assert.Contains("\"openSessionPaths\"", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
            Assert.Contains("\"version\"", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PortableArchiveRoundTripsAndRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-archive-{Guid.NewGuid():N}");
        var sourceRoot = Path.Combine(root, "source.vcat");
        var archivePath = Path.Combine(root, "portable.vcat.zip");
        var extracted = Path.Combine(root, "extracted.vcat");
        var malicious = Path.Combine(root, "malicious.zip");
        Directory.CreateDirectory(root);
        try
        {
            await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), [7, 13]);
            var result = await SessionCoordinator.ImportAsync(source, sourceRoot, Settings());
            using (result.Snapshot)
            {
                await PortableSessionArchiveService.CreateAsync(result.Snapshot, archivePath);
            }

            await PortableSessionArchiveService.ExtractAsync(archivePath, extracted);
            var report = await SessionVerifier.VerifyAsync(extracted);
            Assert.True(report.IsValid, string.Join("; ", report.Issues.Select(static issue => issue.Message)));

            using (var archive = ZipFile.Open(malicious, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("escape");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PortableSessionArchiveService.ExtractAsync(malicious, Path.Combine(root, "malicious.vcat")));
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PublishingACompletedDirectoryWaitsForATransientWindowsScanner()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"visualcat-publish-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "ready.tmp");
        var destination = Path.Combine(root, "ready.vcat");
        Directory.CreateDirectory(source);
        var payload = Path.Combine(source, "payload.bin");
        await File.WriteAllBytesAsync(payload, [1, 2, 3]);
        try
        {
            using var heldByScanner = new FileStream(
                payload,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var releaseScanner = Task.Run(async () =>
            {
                await Task.Delay(900);
                heldByScanner.Dispose();
            });

            await FileSystemPublish.MoveDirectoryAsync(source, destination, CancellationToken.None);
            await releaseScanner;

            Assert.True(File.Exists(Path.Combine(destination, "payload.bin")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task MeasuredSessionSizesArePersistedBesideTheSessionsAndReusedByALaterProcess()
    {
        // A session written before sessionSizeBytes existed has to be measured, and a
        // finalized one is never rewritten, so without a persisted index the cold-start
        // screen re-walks the whole cache on every launch.
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-sizeindex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var index = Path.Combine(root, ".session-sizes.json");
        try
        {
            var legacy = CreateFakeSession(root, "legacy.vcat", now.AddHours(-1), 128);
            var declared = Path.Combine(root, "declared.vcat");
            Directory.CreateDirectory(declared);
            File.WriteAllBytes(Path.Combine(declared, "payload.bin"), new byte[64]);
            File.WriteAllText(
                Path.Combine(declared, "manifest.json"),
                $$"""{"updatedUtc":"{{now:O}}","finalized":true,"sessionSizeBytes":4242}""");

            var scanned = await TemporarySessionRetentionService.ScanAsync(root);
            Assert.Equal(2, scanned.Count);
            var legacySize = Assert.Single(
                scanned,
                session => string.Equals(session.Path, legacy, StringComparison.OrdinalIgnoreCase)).SizeBytes;
            Assert.True(legacySize >= 128);
            Assert.Equal(
                4242,
                Assert.Single(
                    scanned,
                    session => string.Equals(session.Path, declared, StringComparison.OrdinalIgnoreCase)).SizeBytes);

            // Only the measured session is worth remembering; one that declares its own
            // size costs nothing to read and would only go stale in the index.
            Assert.True(File.Exists(index));
            using (var document = JsonDocument.Parse(File.ReadAllText(index)))
            {
                Assert.Single(document.RootElement.EnumerateObject());
                var entry = document.RootElement.GetProperty("legacy.vcat");
                Assert.Equal(legacySize, entry.GetProperty("Size").GetInt64());
            }

            // A scan that learns nothing must not touch the disk again.
            var written = File.GetLastWriteTimeUtc(index);
            await TemporarySessionRetentionService.ScanAsync(root);
            Assert.Equal(written, File.GetLastWriteTimeUtc(index));

            // The index is an optimisation: a scan still works when it is unreadable.
            File.WriteAllText(index, "{ not json");
            var afterCorruption = await TemporarySessionRetentionService.ScanAsync(root);
            Assert.Equal(2, afterCorruption.Count);

            // A session that disappears is pruned rather than remembered forever. Here
            // it was the only measured one, so the index goes with it.
            Directory.Delete(legacy, true);
            await TemporarySessionRetentionService.ScanAsync(root);
            Assert.False(File.Exists(index));

            // With two measured sessions, losing one prunes just that entry.
            var first = CreateFakeSession(root, "first.vcat", now.AddHours(-2), 32);
            CreateFakeSession(root, "second.vcat", now.AddHours(-3), 48);
            await TemporarySessionRetentionService.ScanAsync(root);
            Directory.Delete(first, true);
            await TemporarySessionRetentionService.ScanAsync(root);
            using (var document = JsonDocument.Parse(File.ReadAllText(index)))
            {
                Assert.False(document.RootElement.TryGetProperty("first.vcat", out _));
                Assert.True(document.RootElement.TryGetProperty("second.vcat", out _));
            }
        }
        finally
        {
            DeleteIfPresent(root);
        }
    }

    private static string CreateFakeSession(string root, string name, DateTimeOffset updatedUtc, int bytes)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "payload.bin"), new byte[bytes]);
        File.WriteAllText(
            Path.Combine(path, "manifest.json"),
            $$"""{"updatedUtc":"{{updatedUtc:O}}","finalized":true}""");
        return path;
    }

    private static IngestSettings Settings() =>
        new(
            LogcatFormat.ThreadTime,
            "utf-8",
            new TimestampPolicy(2025, "UTC", new DateTimeOffset(2025, 5, 16, 0, 0, 0, TimeSpan.Zero)),
            new TemplateSettings(),
            BatchBytes: 64,
            ChannelCapacity: 2,
            ParseWorkers: 2,
            SegmentEntries: 2,
            PortableRaw: true);

    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
