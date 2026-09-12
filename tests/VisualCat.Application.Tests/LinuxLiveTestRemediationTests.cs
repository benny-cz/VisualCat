using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

namespace VisualCat.Application.Tests;

/// <summary>
/// The source-layer half of the Linux live-test remediation
/// (<c>docs/LINUX-LIVE-TEST-REPORT.md</c> §20). One region per finding, each test named for the
/// behaviour the run found missing rather than for the method it exercises.
/// </summary>
public sealed class LinuxLiveTestRemediationTests
{
    /// <summary>Every instant a session can hold.</summary>
    private static readonly TimeRange Everything = new(new InstantUs(long.MinValue + 1), new InstantUs(long.MaxValue));

    // ---------------------------------------------------------------- F-06

    [Theory]
    [InlineData("/tmp/ordinary.txt", true)]
    [InlineData("/tmp/é-accented-but-valid.txt", true)]
    [InlineData("/tmp/emoji-\U0001F600.txt", true)]
    [InlineData("/tmp/latin1-name-�.bin", false)]
    public void APathIsAddressableOnlyWhenEveryByteOfItSurvivedDecoding(string path, bool addressable)
    {
        Assert.Equal(addressable, PathAddressability.IsAddressable(path));
    }

    [Fact]
    public void AnUndecodableNameIsExplainedRatherThanReportedMissing()
    {
        // "Log source was not found." sent the reader looking for a file that is present and
        // readable by every other tool; only its name cannot survive a text argument (F-06).
        var explanation = PathAddressability.Explain("/home/benny/corpus/latin1-name-�.bin");
        Assert.Contains("not valid UTF-8", explanation, StringComparison.Ordinal);
        Assert.Contains("<undecodable byte>", explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("not found", explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpeningAnUndecodableNameSaysWhyRatherThanThatItIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "vcat-f06-�.txt");
        var exception = Assert.Throws<FileNotFoundException>(() => new FileLogSource(path));
        Assert.Contains("not valid UTF-8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryMissingFileStillSaysItIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vcat-f06-absent-{Guid.NewGuid():N}.txt");
        var exception = Assert.Throws<FileNotFoundException>(() => new FileLogSource(path));
        Assert.Equal("Log source was not found.", exception.Message);
    }

    // ---------------------------------------------------------------- F-19

    [Fact]
    public void TheDataRootIsCreatedRatherThanReportedUnavailable()
    {
        // A fresh account has no ~/.local/share, and a profile that sets XDG_DATA_HOME without
        // creating it is ordinary. Failing on the lease directory reported a missing data root
        // as unavailable lease storage — a symptom three layers from its cause (F-19).
        Assert.True(Directory.Exists(ProductDataRoot.Path));
    }

    // ---------------------------------------------------------------- F-26

    [Fact]
    public void AZoneThatCannotBeResolvedIsReportedRatherThanGuessed()
    {
        var exception = Assert.Throws<TimeZoneUnavailableException>(
            () => TimeZoneResolution.Resolve("Definitely/Not_A_Zone"));
        Assert.Contains("Definitely/Not_A_Zone", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tzdata", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResolvableZoneStillResolves()
    {
        Assert.Equal(TimeSpan.Zero, TimeZoneResolution.Resolve("UTC").BaseUtcOffset);
    }

    // ---------------------------------------------------------------- F-29

    [Fact]
    public void ANewlineStyleIsTheCharactersItNames()
    {
        Assert.Equal("\n", Newline.Of(NewlineStyle.Lf));
        Assert.Equal("\r\n", Newline.Of(NewlineStyle.Crlf));
    }

    [Fact]
    public async Task ACsvExportUsesTheChosenNewlineAndNotTheHostsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vcat-f29-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "small.txt");
            await File.WriteAllTextAsync(
                log,
                string.Concat(Enumerable.Range(0, 8).Select(static i =>
                    $"05-15 14:13:{i:D2}.000  1073  1151 I VCat: line {i}\n")),
                TestContext.Current.CancellationToken);

            var session = Path.Combine(root, "session.vcat");
            await using (var source = new FileLogSource(log))
            {
                var result = await SessionCoordinator.ImportAsync(
                    source,
                    session,
                    new IngestSettings(
                        null,
                        "utf-8",
                        new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                        new TemplateSettings()),
                    cancellationToken: TestContext.Current.CancellationToken);
                result.Snapshot.Dispose();
            }

            using var snapshot = await SessionStore.OpenAsync(session, TestContext.Current.CancellationToken);
            var lf = Path.Combine(root, "lf.csv");
            var crlf = Path.Combine(root, "crlf.csv");
            await ExportService.ExportNormalizedCsvAsync(
                snapshot, lf, Everything, new FilterSpec(), EntryOrder.SourceSequence,
                includeUtf8Bom: true, progress: null, NewlineStyle.Lf,
                TestContext.Current.CancellationToken);
            await ExportService.ExportNormalizedCsvAsync(
                snapshot, crlf, Everything, new FilterSpec(), EntryOrder.SourceSequence,
                includeUtf8Bom: true, progress: null, NewlineStyle.Crlf,
                TestContext.Current.CancellationToken);

            var lfBytes = await File.ReadAllBytesAsync(lf, TestContext.Current.CancellationToken);
            var crlfBytes = await File.ReadAllBytesAsync(crlf, TestContext.Current.CancellationToken);

            // The whole point of the release gate this failed: the default is chosen, not
            // inherited, so the same session exports the same bytes on every platform (F-29).
            Assert.DoesNotContain((byte)'\r', lfBytes);
            Assert.Contains((byte)'\r', crlfBytes);
            Assert.Equal(
                Encoding.UTF8.GetString(crlfBytes).Replace("\r\n", "\n", StringComparison.Ordinal),
                Encoding.UTF8.GetString(lfBytes));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- F-28

    [Fact]
    public async Task VerificationDistinguishesCheckedEvidenceFromEvidenceItCouldNotCheckAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vcat-f28-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "small.txt");
            await File.WriteAllTextAsync(
                log,
                "05-15 14:13:37.000  1073  1151 I VCat: only line\n",
                TestContext.Current.CancellationToken);
            var session = Path.Combine(root, "session.vcat");
            await using (var source = new FileLogSource(log))
            {
                var result = await SessionCoordinator.ImportAsync(
                    source,
                    session,
                    new IngestSettings(
                        null,
                        "utf-8",
                        new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                        new TemplateSettings()),
                    cancellationToken: TestContext.Current.CancellationToken);
                result.Snapshot.Dispose();
            }

            var verified = await SessionVerifier.VerifyAsync(session, true, TestContext.Current.CancellationToken);
            Assert.True(verified.IsValid);
            Assert.True(verified.RawVerified);

            File.Delete(log);
            var unverifiable = await SessionVerifier.VerifyAsync(session, true, TestContext.Current.CancellationToken);

            // Still valid — a standard session never owned its source — but no longer verified,
            // which is the distinction a script reading $? could not make (F-28).
            Assert.True(unverifiable.IsValid);
            Assert.False(unverifiable.RawVerified);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- F-21

    [Theory]
    [InlineData("manifest.json", true)]
    [InlineData("raw.log", true)]
    [InlineData("view.json", true)]
    [InlineData("templates-final.jsonl", true)]
    [InlineData("source-order/records.bin", true)]
    [InlineData("source-order/index.bin", true)]
    [InlineData("segments/000001/timestamp.bin", true)]
    [InlineData("segments/000001/checksums.json", true)]
    [InlineData("segments/000001/bitmaps/level-255.rbm", true)]
    [InlineData("segments-final-00000005/000004/payload.bin", true)]
    [InlineData("diagnostics/session.jsonl", true)]
    [InlineData("bomb.bin", false)]
    [InlineData("a/b/c/d/e/f/g/h/i/j/k/deep.bin", false)]
    [InlineData("segments/000001/../../escape.bin", false)]
    [InlineData("source-order/unexpected.bin", false)]
    [InlineData("segments/1/timestamp.bin", false)]
    [InlineData("", false)]
    public void OnlyFilesTheSessionFormatDefinesAreMembersOfASession(string entry, bool known)
    {
        // The extractor wrote every member it was given, so a 1.1 MB archive declaring a 1 GiB
        // bomb.bin produced a 1.1 GiB directory in the product's own data root — and a
        // 200-deep path and a 240-character name for the same reason (finding F-21).
        Assert.Equal(known, SessionLayout.IsKnownMember(entry));
    }

    [Fact]
    public async Task APortableArchiveCarryingSomethingThatIsNotPartOfASessionIsRefusedAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vcat-f21-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "small.txt");
            await File.WriteAllTextAsync(
                log,
                "05-15 14:13:37.000  1073  1151 I VCat: only line\n",
                TestContext.Current.CancellationToken);
            var session = Path.Combine(root, "session.vcat");
            await using (var source = new FileLogSource(log))
            {
                var result = await SessionCoordinator.ImportAsync(
                    source,
                    session,
                    new IngestSettings(
                        null,
                        "utf-8",
                        new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                        new TemplateSettings(),
                        PortableRaw: true),
                    cancellationToken: TestContext.Current.CancellationToken);
                result.Snapshot.Dispose();
            }

            var archive = Path.Combine(root, "session.vcat.zip");
            using (var snapshot = await SessionStore.OpenAsync(session, TestContext.Current.CancellationToken))
            {
                await PortableSessionArchiveService.CreateAsync(
                    snapshot, archive, TestContext.Current.CancellationToken);
            }

            // A legitimate archive still round-trips.
            var clean = Path.Combine(root, "clean.vcat");
            await PortableSessionArchiveService.ExtractAsync(
                archive, clean, TestContext.Current.CancellationToken);
            Assert.True(Directory.Exists(clean));

            using (var zip = System.IO.Compression.ZipFile.Open(
                       archive, System.IO.Compression.ZipArchiveMode.Update))
            {
                var bomb = zip.CreateEntry("bomb.bin", System.IO.Compression.CompressionLevel.Optimal);
                await using var stream = bomb.Open();
                await stream.WriteAsync(new byte[16 * 1024 * 1024], TestContext.Current.CancellationToken);
            }

            var refused = await Assert.ThrowsAsync<InvalidDataException>(() =>
                PortableSessionArchiveService.ExtractAsync(
                    archive, Path.Combine(root, "bombed.vcat"), TestContext.Current.CancellationToken));
            Assert.Contains("bomb.bin", refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    // ---------------------------------------------------------------- F-27

    [Fact]
    public async Task ASessionIsOwnerOnlyWhateverTheAccountsUmaskSaysAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"vcat-f27-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "small.txt");
            await File.WriteAllTextAsync(
                log,
                "05-15 14:13:37.000  1073  1151 I VCat: only line\n",
                TestContext.Current.CancellationToken);
            var session = Path.Combine(root, "session.vcat");
            await using (var source = new FileLogSource(log))
            {
                var result = await SessionCoordinator.ImportAsync(
                    source,
                    session,
                    new IngestSettings(
                        null,
                        "utf-8",
                        new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                        new TemplateSettings(),
                        PortableRaw: true),
                    cancellationToken: TestContext.Current.CancellationToken);
                result.Snapshot.Dispose();
            }

            // A session is somebody else's log. No group or other bit, whatever the umask is.
            var mode = File.GetUnixFileMode(session);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                mode);

            var raw = Path.Combine(session, "raw.log");
            if (File.Exists(raw))
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(raw));
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
