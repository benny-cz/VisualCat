using VisualCat.Core.Store;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.Application.Tests;

[Collection("Capture deletion filesystem")]
public sealed class CaptureDeletionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VisualCat.Delete.Tests", Guid.NewGuid().ToString("N"));
    public CaptureDeletionTests() => Directory.CreateDirectory(_root);

    private async Task<TemporarySessionInfo> Capture(string name = "sample")
    {
        var path = Path.Combine(_root, name + ".vcat");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "manifest.json"), "{\"updatedUtc\":\"2026-09-05T10:00:00Z\",\"finalized\":true,\"sessionSizeBytes\":4096}", TestContext.Current.CancellationToken);
        return new(path, DateTimeOffset.UtcNow, 4096, true);
    }

    /// <summary>T-I6. One ordered result per distinct target, even for a request cancelled before it began.</summary>
    [Fact]
    public async Task DistinctTargetsHaveOrderedResultsEvenWhenAlreadyCancelled()
    {
        var a = await Capture("a"); var b = await Capture("b");
        var targets = new[] { await CaptureDeletionService.PrepareAsync(_root, a), await CaptureDeletionService.PrepareAsync(_root, b) };
        using var stop = new CancellationTokenSource(); await stop.CancelAsync();
        var result = await CaptureDeletionService.DeleteAsync(_root, [targets[0], targets[0], targets[1]], stop.Token);
        Assert.Equal(targets, result.Select(item => item.Target));
        Assert.All(result, item => Assert.Equal(CaptureDeleteOutcome.Cancelled, item.Outcome));
        Assert.True(Directory.Exists(a.Path)); Assert.True(Directory.Exists(b.Path));
    }

    /// <summary>T-I5. A verified absent source is AlreadyMissing, and it is not a deletion.</summary>
    [Fact]
    public async Task DeletionAndAlreadyMissingHaveDifferentAccounting()
    {
        var a = await Capture("a"); var b = await Capture("b");
        var targets = new[] { await CaptureDeletionService.PrepareAsync(_root, a), await CaptureDeletionService.PrepareAsync(_root, b) };
        Directory.Delete(b.Path, true);
        var result = await CaptureDeletionService.DeleteAsync(_root, targets);
        Assert.Equal(CaptureDeleteOutcome.Deleted, result[0].Outcome);
        Assert.Equal(CaptureDeleteOutcome.AlreadyMissing, result[1].Outcome);
        Assert.False(result[1].Committed);
        Assert.Empty(await TemporarySessionRetentionService.ScanAsync(_root));
    }

    /// <summary>T-I5. An unavailable root, or a file where the capture was, is never missing-success.</summary>
    [Fact]
    public async Task MissingRootAndFileOccupyingTargetAreNeverMissingSuccess()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        Directory.Delete(a.Path, true); await File.WriteAllTextAsync(a.Path, "survivor", TestContext.Current.CancellationToken);
        Assert.Equal(CaptureDeleteOutcome.Refused, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
        File.Delete(a.Path); Directory.Delete(_root, true);
        Assert.Equal(CaptureDeleteOutcome.IoFailure, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
    }

    /// <summary>T-I18. A replacement at the confirmed path is Changed, and is left alone.</summary>
    [Fact]
    public async Task RecreatedPathCannotReplaceTheConfirmedCapture()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        Directory.Delete(a.Path, true); await Capture();
        await CaptureDeletionService.PrepareAsync(_root, a);
        Assert.Equal(CaptureDeleteOutcome.Changed, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
        Assert.True(Directory.Exists(a.Path));
    }

    /// <summary>T-I18. Identity is checked before anything destructive happens.</summary>
    [Fact]
    public async Task AReplacementWithoutAnIdentityIsChangedBeforeAnyDestructiveWork()
    {
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        Directory.Delete(capture.Path, true);
        await Capture();
        var before = await File.ReadAllBytesAsync(Path.Combine(capture.Path, "manifest.json"), TestContext.Current.CancellationToken);
        var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
        Assert.Equal(CaptureDeleteOutcome.Changed, result.Outcome);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(capture.Path, "manifest.json"), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
    }

    /// <summary>T-I2, T-I19. A writer or a foreign reader is refused, and no rename happens.</summary>
    [Fact]
    public async Task ReservationBlocksWritersAndNewReadersWhileExistingReadersDrain()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        using (SessionAccess.Write(a.Path))
            Assert.Equal(CaptureDeleteOutcome.Protected, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
        var reader = SessionAccess.Read(a.Path);
        using var reservation = SessionAccess.ReserveDeletion(a.Path);
        Assert.Throws<SessionInUseException>(() => SessionAccess.Read(a.Path));
        Assert.Throws<SessionInUseException>(() => SessionAccess.Write(a.Path));
        Assert.Throws<SessionInUseException>(reservation.RequireExclusive);
        reader.Dispose(); reservation.RequireExclusive();
        Assert.True((await CaptureDeletionService.DeleteAsync(_root, target, reservation)).Committed);
    }

    /// <summary>T-I14. Stop between publication and rename: the capture stays, the record is retired.</summary>
    [Fact]
    public async Task StopAfterPublicationLeavesOriginalAndRecoveryOnlyRemovesMetadata()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        using var stop = new CancellationTokenSource();
        CaptureDeletionService.TestPhase = phase => { if (phase == "published") stop.Cancel(); };
        var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target], stop.Token));
        Assert.Equal(CaptureDeleteOutcome.Cancelled, result.Outcome);
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.True(Directory.Exists(a.Path));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.vcat-deleting.json"));
    }

    /// <summary>
    /// A rename that never commits leaves a record behind, and ordinary recovery retires it
    /// even while the capture it names is being read.
    /// </summary>
    /// <remarks>
    /// Observed on the desktop: a capture whose payload a second program held open failed to
    /// rename, and the ownership record published before the attempt stayed in the root
    /// through refresh after refresh. Recovery was asking for the pre-rename gate, which also
    /// refuses while any local reader holds the source — and listing the root reads every
    /// capture in it, one at a time. Deletion intent is all recovery needs: it reclaims a
    /// detached payload and retires obsolete metadata, and it never renames the source.
    /// </remarks>
    [Fact]
    public async Task ARecordLeftByAFailedRenameIsRetiredWhileTheSourceIsBeingRead()
    {
        if (!OperatingSystem.IsWindows()) return;
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        using (new FileStream(
            Path.Combine(capture.Path, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
            Assert.False(result.Committed);
            Assert.True(Directory.Exists(capture.Path));
            Assert.Single(Directory.EnumerateFiles(_root, "*.vcat-deleting.json"));
        }

        // A reader of the source is exactly the state every inventory of the root passes
        // through, so recovery has to make progress in it rather than wait for it to end.
        using (SessionAccess.Read(capture.Path))
        {
            await CaptureDeletionService.RetryCleanupAsync(_root);
            Assert.Empty(Directory.EnumerateFiles(_root, "*.vcat-deleting.json"));
        }

        // The capture the record named is untouched, and nothing is left claiming to be
        // outstanding storage work.
        Assert.True(Directory.Exists(capture.Path));
        var after = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(0, after.PendingCleanup);
        Assert.Equal(0, after.UnresolvedCleanup);
        Assert.Single(after.Sessions);
    }

    /// <summary>T-I8. A failure after the rename keeps the removal, the ownership record and the retry.</summary>
    [Fact]
    public async Task FailureAfterCommitPreservesTheRemovalAndDurableCleanupOwnership()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        using var reservation = SessionAccess.ReserveDeletion(a.Path);
        CaptureDeletionService.TestPhase = phase => { if (phase == "committed") throw new IOException("injected after rename"); };
        var result = await CaptureDeletionService.DeleteAsync(_root, target, reservation);
        Assert.Equal(CaptureDeleteOutcome.DeletedPendingReclaim, result.Outcome);
        Assert.False(Directory.Exists(a.Path));
        Assert.Single(Directory.EnumerateFiles(_root, "*.vcat-deleting.json"));
        reservation.Dispose(); CaptureDeletionService.TestPhase = null;
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
    }

    /// <summary>
    /// T-I4. A root reached through a linked ancestor still works; a linked root does not.
    /// </summary>
    /// <remarks>
    /// macOS reaches its standard temporary directory through <c>/var</c>, which is a symlink,
    /// so refusing every reparse point up to the volume made the whole feature report
    /// unavailable storage on that platform and failed every filesystem test with it. Above the
    /// root is the reader's own layout; the guarantee is that the root itself is real and that
    /// nothing inside it is followed.
    /// </remarks>
    [Fact]
    public async Task ARootBehindALinkedAncestorIsUsableAndALinkedRootIsNot()
    {
        var linked = Path.Combine(Path.GetTempPath(), "VisualCat.Delete.Tests", Guid.NewGuid().ToString("N"));
        var real = Path.Combine(linked, "real");
        Directory.CreateDirectory(real);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(linked, "via"), real);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Creating links needs a privilege this account may not hold. Explicit skip:
                // the check is real, the fixture is not always available.
                return;
            }

            // The root is a real directory that happens to be reached through a link.
            var root = Path.Combine(linked, "via", "root");
            Directory.CreateDirectory(root);
            var capture = Path.Combine(root, "sample.vcat");
            Directory.CreateDirectory(capture);
            await File.WriteAllTextAsync(
                Path.Combine(capture, "manifest.json"),
                "{\"updatedUtc\":\"2026-09-06T10:00:00Z\",\"finalized\":true,\"sessionSizeBytes\":4096}",
                TestContext.Current.CancellationToken);

            var inventory = await CaptureDeletionService.InventoryAsync(root);
            Assert.True(inventory.Available, $"storage reported unavailable: {inventory.Error?.GetType().Name}");
            var target = await CaptureDeletionService.PrepareAsync(
                root, new TemporarySessionInfo(capture, DateTimeOffset.UtcNow, 4096, true));
            Assert.True(Assert.Single(await CaptureDeletionService.DeleteAsync(root, [target])).Committed);
            Assert.False(Directory.Exists(capture));

            // The root being the link itself is still refused: that is the boundary this
            // operation owns, and it has to be a real directory.
            var throughLink = Path.Combine(linked, "via");
            var other = Path.Combine(real, "root", "other.vcat");
            Directory.CreateDirectory(other);
            var refused = Assert.Single(await CaptureDeletionService.DeleteAsync(
                throughLink, [new CaptureDeleteTarget(Path.Combine(throughLink, "other.vcat"), "id", null)]));
            Assert.Equal(CaptureDeleteOutcome.Refused, refused.Outcome);
            Assert.True(Directory.Exists(other));
        }
        finally
        {
            try { Directory.Delete(linked, true); } catch (IOException) { }
        }
    }

    /// <summary>T-I14. A staging suffix with no valid record is refused, not swept.</summary>
    [Fact]
    public async Task SuffixAloneNeverAuthorisesRecovery()
    {
        var stage = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "keep"), "keep", TestContext.Current.CancellationToken);
        await CaptureDeletionService.RetryCleanupAsync(_root);
        var snapshot = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(1, snapshot.UnresolvedCleanup);
        Assert.True(File.Exists(Path.Combine(stage, "keep")));
    }

    [Fact]
    public async Task AFileOccupyingAnOwnedStageIsReportedAndNeverErased()
    {
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        using var reservation = SessionAccess.ReserveDeletion(capture.Path);
        CaptureDeletionService.TestPhase = phase => { if (phase == "committed") throw new IOException("pause cleanup"); };
        Assert.Equal(CaptureDeleteOutcome.DeletedPendingReclaim,
            (await CaptureDeletionService.DeleteAsync(_root, target, reservation)).Outcome);
        var stage = Assert.Single(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
        Directory.Delete(stage, true);
        await File.WriteAllTextAsync(stage, "unowned replacement", TestContext.Current.CancellationToken);
        var inventory = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(0, inventory.PendingCleanup);
        Assert.Equal(1, inventory.UnresolvedCleanup);
        reservation.Dispose();
        CaptureDeletionService.TestPhase = null;
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.Equal("unowned replacement", await File.ReadAllTextAsync(stage, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await CaptureDeletionService.InventoryAsync(_root)).UnresolvedCleanup);
    }

    [Fact]
    public async Task SharingViolationsAreTypedAndDoNotRename()
    {
        if (!OperatingSystem.IsWindows()) return;
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        using var handle = new FileStream(Path.Combine(a.Path, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
        // Directory.Move may report generic access denied for a sharing-denying child handle.
        // Only positively identified sharing codes may be labelled Locked.
        Assert.Contains(result.Outcome, new[] { CaptureDeleteOutcome.Locked, CaptureDeleteOutcome.IoFailure, CaptureDeleteOutcome.Denied });
        Assert.True(Directory.Exists(a.Path));
    }

    [Fact]
    public async Task RelativeNestedAndSiblingPathsAreRefused()
    {
        var a = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, a);
        var result = await CaptureDeletionService.DeleteAsync(_root,
            [target with { Path = "sample.vcat" }, target with { Path = Path.Combine(a.Path, "nested.vcat") }, target with { Path = _root + ".vcat" }]);
        Assert.All(result, item => Assert.Equal(CaptureDeleteOutcome.Refused, item.Outcome));
        Assert.True(Directory.Exists(a.Path));
    }

    [Fact]
    public async Task PartialInventoryDoesNotClaimHealthyEmpty()
    {
        await Capture(); Directory.CreateDirectory(Path.Combine(_root, "unreadable.vcat"));
        var snapshot = await CaptureDeletionService.InventoryAsync(_root);
        Assert.True(snapshot.Available); Assert.Single(snapshot.Sessions); Assert.Equal(1, snapshot.InspectionIssues);
    }

    /// <summary>
    /// The second process every cross-process test talks to. It takes a real lease through the
    /// store protocol, so "reserved" means the protocol owns the path in another process.
    /// </summary>
    private static System.Diagnostics.ProcessStartInfo Probe(string path, string? mode = null)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "VisualCat.Desktop.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        var probe = Path.Combine(repo.FullName, "tools", "VisualCat.StorageProbe", "bin", configuration, "net10.0", "VisualCat.StorageProbe.dll");

        // A missing probe is a build problem, not a protection failure. Say which.
        Assert.True(File.Exists(probe), "the storage probe was not built at " + probe);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(probe); start.ArgumentList.Add(path);
        if (mode is not null) start.ArgumentList.Add(mode);
        return start;
    }

    /// <summary>T-I2, T-I19. Another process's writer is protected, and its death releases the lease.</summary>
    [Fact]
    public async Task ASecondProcessProtectsItsWriterAndProcessDeathReleasesOwnership()
    {
        var capture = await Capture(); var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        var start = Probe(capture.Path);
        using var process = System.Diagnostics.Process.Start(start)!;
        try
        {
            Assert.Equal("reserved", await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            Assert.Equal(CaptureDeleteOutcome.Protected, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
            process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            // WaitForExit means the process is gone, not that Windows has finished closing the
            // handles it held. Until it has, the honest answer is still "in use", so poll for
            // the release rather than asserting that it is instantaneous.
            CaptureDeleteResult after;
            for (var attempt = 0; ; attempt++)
            {
                after = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
                if (after.Committed || attempt >= 50)
                {
                    break;
                }

                Assert.Equal(CaptureDeleteOutcome.Protected, after.Outcome);
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            Assert.True(after.Committed, after.Outcome + ": " + after.Error);
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(TestContext.Current.CancellationToken); } }
    }

    // --------------------------------------------------------------- added coverage ---

    /// <summary>T-I1. Only the prepared capture goes; its siblings are byte-identical after.</summary>
    [Fact]
    public async Task OnlyThePreparedCaptureIsRemoved()
    {
        var doomed = await Capture("doomed");
        var keeper = await Capture("keeper");
        var outsideRoot = _root + "-outside";
        Directory.CreateDirectory(outsideRoot);
        var outside = Path.Combine(outsideRoot, "outside.vcat");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "payload"), "outside", TestContext.Current.CancellationToken);
        try
        {
            var before = await File.ReadAllBytesAsync(Path.Combine(keeper.Path, "manifest.json"), TestContext.Current.CancellationToken);
            var target = await CaptureDeletionService.PrepareAsync(_root, doomed);
            Assert.Equal(CaptureDeleteOutcome.Deleted, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
            Assert.False(Directory.Exists(doomed.Path));
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(keeper.Path, "manifest.json"), TestContext.Current.CancellationToken));
            Assert.Equal("outside", await File.ReadAllTextAsync(Path.Combine(outside, "payload"), TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(outsideRoot, true);
        }
    }

    /// <summary>T-I3. A trailing separator, the root itself and a volume root are all refused.</summary>
    [Fact]
    public async Task TrailingSeparatorsRootsAndVolumeRootsAreRefused()
    {
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        var volume = Path.GetPathRoot(_root)!;
        var results = await CaptureDeletionService.DeleteAsync(_root,
        [
            target with { Path = _root },
            target with { Path = volume },
            target with { Path = Path.Combine(_root, "sample.vcat.other") },
        ]);
        Assert.All(results, item => Assert.Equal(CaptureDeleteOutcome.Refused, item.Outcome));
        Assert.True(Directory.Exists(capture.Path));

        // A trailing separator names the same capture and must still resolve to it.
        Assert.True(Assert.Single(await CaptureDeletionService.DeleteAsync(_root,
            [target with { Path = capture.Path + Path.DirectorySeparatorChar }])).Committed);
    }

    /// <summary>T-I20. A request frozen against one root cannot be redirected at another.</summary>
    [Fact]
    public async Task ARequestCannotBeRedirectedAtAnotherRoot()
    {
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        var other = Path.Combine(Path.GetTempPath(), "VisualCat.Delete.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            Assert.Equal(CaptureDeleteOutcome.Refused, Assert.Single(await CaptureDeletionService.DeleteAsync(other, [target])).Outcome);
            Assert.True(Directory.Exists(capture.Path));
        }
        finally
        {
            Directory.Delete(other, true);
        }
    }

    /// <summary>T-I4. A link inside the tree refuses removal and is never followed.</summary>
    [Fact]
    public async Task LinkedDescendantsAreRefusedAndNeverFollowed()
    {
        var capture = await Capture();
        var elsewhere = Path.Combine(Path.GetTempPath(), "VisualCat.Delete.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        await File.WriteAllTextAsync(Path.Combine(elsewhere, "keep"), "keep", TestContext.Current.CancellationToken);
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(capture.Path, "linked"), elsewhere);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Creating links needs a privilege this account may not hold. Explicit skip:
                // the check is real, the fixture is not always available.
                return;
            }

            var target = await CaptureDeletionService.PrepareAsync(_root, capture);
            Assert.Equal(CaptureDeleteOutcome.Refused, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
            Assert.True(Directory.Exists(capture.Path));
            Assert.True(File.Exists(Path.Combine(elsewhere, "keep")));
        }
        finally
        {
            Directory.Delete(elsewhere, true);
        }
    }

    /// <summary>T-I7. A staged folder is excluded from listings even when it holds a manifest.</summary>
    [Fact]
    public async Task StagedFoldersAreNeverListedAsCaptures()
    {
        var capture = await Capture();
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        CaptureDeletionService.TestPhase = phase => { if (phase == "committed") throw new IOException("injected after rename"); };

        // Held for the whole test, not just the deletion. A failure after the rename schedules
        // its own recovery pass, and recovery takes this very reservation per record — so
        // releasing it here let the sweep reclaim the payload the assertions below are looking
        // at, and the test failed on whichever assertion the race happened to reach first.
        using var reservation = SessionAccess.ReserveDeletion(capture.Path);
        Assert.Equal(CaptureDeleteOutcome.DeletedPendingReclaim, (await CaptureDeletionService.DeleteAsync(_root, target, reservation)).Outcome);

        CaptureDeletionService.TestPhase = null;
        var staged = Assert.Single(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
        Assert.True(File.Exists(Path.Combine(staged, "manifest.json")));
        Assert.Empty(await TemporarySessionRetentionService.ScanAsync(_root));
        var inventory = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Empty(inventory.Sessions);
        Assert.Equal(0, inventory.InspectionIssues);
        Assert.Equal(1, inventory.PendingCleanup);
    }

    /// <summary>T-I15, T-I16. A large tree drains across slices without blocking a scan.</summary>
    [Fact]
    public async Task ALargeTreeDrainsAcrossSlicesWithoutBlockingScans()
    {
        var capture = await Capture("large");
        for (var i = 0; i < 40; i++)
        {
            var nested = Path.Combine(capture.Path, "segment" + i);
            Directory.CreateDirectory(nested);
            for (var j = 0; j < 10; j++)
            {
                await File.WriteAllTextAsync(Path.Combine(nested, "part" + j + ".bin"), new string('x', 64), TestContext.Current.CancellationToken);
            }
        }

        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
        Assert.True(result.Committed);
        Assert.False(Directory.Exists(capture.Path));

        // A scan answers whether or not the sweep has finished.
        Assert.Empty(await TemporarySessionRetentionService.ScanAsync(_root));

        // Repeated passes in this same process drain it: there is no once-per-process latch.
        for (var attempt = 0; attempt < 30 && Directory.EnumerateDirectories(_root, "*.vcat-deleting").Any(); attempt++)
        {
            await CaptureDeletionService.RetryCleanupAsync(_root);
        }

        Assert.Empty(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.vcat-deleting.json"));
        Assert.Equal(0, (await CaptureDeletionService.InventoryAsync(_root)).PendingCleanup);
    }

    /// <summary>T-I11. The compatibility wrapper keeps the exception contract it always had.</summary>
    [Fact]
    public async Task TheCompatibilityWrapperKeepsItsExceptionContract()
    {
        var capture = await Capture();
        Assert.Throws<IOException>(() => TemporarySessionRetentionService.DeleteExactSession(_root, Path.Combine(_root, "not-a-capture")));
        Assert.Throws<DirectoryNotFoundException>(() =>
            TemporarySessionRetentionService.DeleteExactSession(_root, Path.Combine(_root, "absent.vcat")));
        TemporarySessionRetentionService.DeleteExactSession(_root, capture.Path);
        Assert.False(Directory.Exists(capture.Path));
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
    }

    /// <summary>T-I13. A generic storage failure is never reported as a sharing lock.</summary>
    [Fact]
    public void TheExceptionTaxonomyDoesNotInventLocks()
    {
        Assert.Equal(CaptureDeleteOutcome.IoFailure, CaptureDeletionService.Classify(new IOException("generic")));
        Assert.Equal(CaptureDeleteOutcome.IoFailure, CaptureDeletionService.Classify(new DirectoryNotFoundException()));
        Assert.Equal(CaptureDeleteOutcome.Denied, CaptureDeletionService.Classify(new UnauthorizedAccessException()));
        Assert.Equal(CaptureDeleteOutcome.Protected, CaptureDeletionService.Classify(new SessionInUseException()));
        Assert.Equal(CaptureDeleteOutcome.Changed, CaptureDeletionService.Classify(new CaptureChangedException()));
        Assert.Equal(CaptureDeleteOutcome.Refused, CaptureDeletionService.Classify(new CaptureRefusedException()));
        Assert.Equal(CaptureDeleteOutcome.Cancelled, CaptureDeletionService.Classify(new OperationCanceledException()));
        Assert.Equal(CaptureDeleteOutcome.Unknown, CaptureDeletionService.Classify(new InvalidProgramException()));
    }

    /// <summary>T-I9. The size index cannot keep answering for a path this batch removed.</summary>
    [Fact]
    public async Task TheSizeIndexForgetsARemovedCapture()
    {
        var capture = await Capture();
        await File.WriteAllTextAsync(Path.Combine(capture.Path, "payload.bin"), new string('y', 4096), TestContext.Current.CancellationToken);

        // A manifest with no declared size makes the scan measure the walk and cache it.
        await File.WriteAllTextAsync(Path.Combine(capture.Path, "manifest.json"),
            "{\"updatedUtc\":\"2026-09-05T10:00:00Z\",\"finalized\":true}", TestContext.Current.CancellationToken);
        var listed = Assert.Single(await TemporarySessionRetentionService.ScanAsync(_root));
        Assert.True(listed.SizeBytes > 4000);

        var target = await CaptureDeletionService.PrepareAsync(_root, listed);
        Assert.True(Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Committed);

        // Recreated at the same path with different contents: the old measurement is gone.
        Directory.CreateDirectory(capture.Path);
        await File.WriteAllTextAsync(Path.Combine(capture.Path, "manifest.json"),
            "{\"updatedUtc\":\"2026-09-05T10:00:00Z\",\"finalized\":true}", TestContext.Current.CancellationToken);
        var again = Assert.Single(await TemporarySessionRetentionService.ScanAsync(_root));
        Assert.True(again.SizeBytes < listed.SizeBytes);
    }

    /// <summary>T-I10. One canonical spelling answers for a request, a lease and a size.</summary>
    [Fact]
    public async Task PathSpellingsNormaliseConsistently()
    {
        var capture = await Capture();
        var awkward = Path.Combine(_root, ".", "sample.vcat") + Path.DirectorySeparatorChar;
        var target = await CaptureDeletionService.PrepareAsync(_root, capture with { Path = awkward });
        Assert.Equal(SessionPath.Canonical(capture.Path), target.Path);
        using (SessionAccess.Write(awkward))
        {
            Assert.True(SessionAccess.IsWriting(capture.Path));
            Assert.Equal(CaptureDeleteOutcome.Protected, Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Outcome);
        }

        Assert.True(Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Committed);
    }

    /// <summary>Section 5.5. Readers share; a writer and a committing deletion do not.</summary>
    [Fact]
    public async Task ReadersShareAndDeletionIntentBlocksNewWriters()
    {
        var capture = await Capture();
        using (SessionAccess.Read(capture.Path))
        using (SessionAccess.Read(capture.Path))
        {
            // Deletion intent may be taken while readers are still open; only the commit
            // upgrade requires that they have gone.
            using var reservation = SessionAccess.ReserveDeletion(capture.Path);
            Assert.Throws<SessionInUseException>(reservation.RequireExclusive);
            Assert.Throws<SessionInUseException>(() => SessionAccess.Write(capture.Path));
            Assert.Throws<SessionInUseException>(() => SessionAccess.Read(capture.Path));
        }

        using var afterwards = SessionAccess.ReserveDeletion(capture.Path);
        afterwards.RequireExclusive();
        Assert.True(Directory.Exists(capture.Path));
    }

    /// <summary>T-I19. Foreign readers are seen before any tab closes, whatever TEMP each process has.</summary>
    [Fact]
    public async Task ForeignReadersAreDetectedBeforeClosingAndIntentBlocksNewReadersAcrossTempDirectories()
    {
        var capture = await Capture();
        using var local = SessionAccess.Read(capture.Path);
        var start = Probe(capture.Path, "read");
        var otherTemp = Path.Combine(_root, "different-temp"); Directory.CreateDirectory(otherTemp);
        start.Environment["TEMP"] = otherTemp; start.Environment["TMP"] = otherTemp;
        using (var reader = System.Diagnostics.Process.Start(start)!)
        {
            try
            {
                Assert.Equal("reserved", await reader.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken));
                Assert.Throws<SessionInUseException>(() => SessionAccess.ReserveDeletion(capture.Path));
                await reader.StandardInput.WriteLineAsync("release");
                await reader.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            finally { if (!reader.HasExited) { reader.Kill(); await reader.WaitForExitAsync(TestContext.Current.CancellationToken); } }
        }
        using var deletion = SessionAccess.ReserveDeletion(capture.Path);
        using (var reader = System.Diagnostics.Process.Start(start)!)
        {
            try
            {
                Assert.Equal("blocked", await reader.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken));
                await reader.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            finally { if (!reader.HasExited) { reader.Kill(); await reader.WaitForExitAsync(TestContext.Current.CancellationToken); } }
        }
        Assert.Throws<SessionInUseException>(deletion.RequireExclusive);
        local.Dispose(); deletion.RequireExclusive();
    }

    /// <summary>
    /// Section 5.5. A deletion that ends without removing the capture hands sole use back. It
    /// took the exclusive lease to close its own tabs under; keeping it after a refused close
    /// left this process the sole owner of a capture it had merely open, and every other
    /// cooperating process was refused until that tab happened to close.
    /// </summary>
    [Fact]
    public async Task AnAbandonedDeletionHandsSoleUseBackToOtherProcesses()
    {
        var capture = await Capture();

        // A tab this process has open, and a deletion whose close never completes.
        using var open = SessionAccess.Read(capture.Path);
        using (var deletion = SessionAccess.ReserveDeletion(capture.Path))
        {
            Assert.Throws<SessionInUseException>(deletion.RequireExclusive);
        }

        var start = Probe(capture.Path, "read");
        var otherTemp = Path.Combine(_root, "abandoned-temp"); Directory.CreateDirectory(otherTemp);
        start.Environment["TEMP"] = otherTemp; start.Environment["TMP"] = otherTemp;
        using var reader = System.Diagnostics.Process.Start(start)!;
        try
        {
            Assert.Equal(
                "reserved",
                await reader.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            // Handing sole use back is not giving protection up: with a foreign reader holding
            // the capture again, a new deletion is refused before it can touch anything.
            Assert.Throws<SessionInUseException>(() => SessionAccess.ReserveDeletion(capture.Path));
        }
        finally
        {
            if (!reader.HasExited) { reader.Kill(); await reader.WaitForExitAsync(TestContext.Current.CancellationToken); }
        }
    }

    [Fact]
    public async Task DeepTreesResumeDescentInsteadOfSpendingEverySliceAtTheRoot()
    {
        var capture = await Capture("deep"); var path = capture.Path;
        for (var depth = 0; depth < 150; depth++) { path = Path.Combine(path, "d"); Directory.CreateDirectory(path); }
        await File.WriteAllTextAsync(Path.Combine(path, "payload"), "owned", TestContext.Current.CancellationToken);
        var target = await CaptureDeletionService.PrepareAsync(_root, capture);
        var result = Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target]));
        Assert.True(result.Committed, $"{result.Outcome}: {result.Error}");
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.vcat-deleting"));
    }

    /// <summary>
    /// T-I14. A publication killed before its record was in place is an obligation recovery
    /// meets, not a permanent "cleanup could not be verified" a reader can do nothing about.
    /// </summary>
    /// <remarks>
    /// The temporary carries a name this operation issues and holds a record rather than a
    /// payload, so retiring it can never reach a capture. Left behind it was permanent: the
    /// recovery loop reads only published <c>.json</c> names, so every launch reported it and
    /// every <b>Retry storage cleanup</b> left it exactly where it was.
    /// <para>
    /// It is held here the way a publication holds it, which is what makes the classification
    /// observable at all: an inventory schedules recovery through its own scan before it counts
    /// anything, so an unheld temporary is usually retired before the count is taken — the
    /// outcome a reader wants, and one that cannot be asserted. Holding it also fixes the other
    /// half of the contract in the same test: recovery leaves a publication running in another
    /// process to its owner.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAbandonedPublicationIsPendingAndThenRetired()
    {
        var abandoned = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json.tmp");
        var junk = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.other");
        await File.WriteAllTextAsync(junk, "not ours", TestContext.Current.CancellationToken);

        using (new FileStream(abandoned, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var held = await CaptureDeletionService.InventoryAsync(_root);
            Assert.Equal(1, held.PendingCleanup);
            Assert.Equal(1, held.UnresolvedCleanup);

            await CaptureDeletionService.RetryCleanupAsync(_root);
            Assert.True(File.Exists(abandoned));
        }

        await CaptureDeletionService.RetryCleanupAsync(_root);
        var after = await CaptureDeletionService.InventoryAsync(_root);
        Assert.False(File.Exists(abandoned));
        Assert.Equal(0, after.PendingCleanup);

        // Anything whose name this operation does not issue is still refused and still visible.
        Assert.Equal(1, after.UnresolvedCleanup);
        Assert.Equal("not ours", await File.ReadAllTextAsync(junk, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublicationCleanupReportsDirectoriesAsUnresolvedAndLeavesThemUntouched()
    {
        var directory = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json.tmp");
        Directory.CreateDirectory(directory);
        var payload = Path.Combine(directory, "survivor");
        await File.WriteAllTextAsync(payload, "not metadata", TestContext.Current.CancellationToken);

        await CaptureDeletionService.RetryCleanupAsync(_root);
        var inventory = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(0, inventory.PendingCleanup);
        Assert.Equal(1, inventory.UnresolvedCleanup);
        Assert.Equal("not metadata", await File.ReadAllTextAsync(payload, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublicationCleanupLeavesLinkedFilesAndTheirTargetsUntouched()
    {
        var capture = await Capture();
        var target = Path.Combine(capture.Path, "manifest.json");
        var before = await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken);
        var link = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json.tmp");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // The Android acceptance pass also covers this on storage that permits links.
            return;
        }

        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(before, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        var inventory = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(0, inventory.PendingCleanup);
        Assert.Equal(1, inventory.UnresolvedCleanup);
    }

    [Fact]
    public async Task PublicationCleanupRefusesFilesLargerThanItsOwnRecords()
    {
        var path = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json.tmp");
        var bytes = new byte[4097];
        Random.Shared.NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        var inventory = await CaptureDeletionService.InventoryAsync(_root);
        Assert.Equal(0, inventory.PendingCleanup);
        Assert.Equal(1, inventory.UnresolvedCleanup);
    }

    [Fact]
    public async Task BusyPublicationsDoNotStarveLaterAbandonedMetadataOrSpinThroughTheDrainBudget()
    {
        for (var index = 0; index < 129; index++)
            await File.WriteAllTextAsync(
                Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json.tmp"),
                "{", TestContext.Current.CancellationToken);

        // Fix the fixture to this filesystem's enumeration order: the first slice is all
        // busy and the next contains work recovery can finish. No filename-order assumption.
        var paths = Directory.EnumerateFiles(_root, "*.vcat-deleting.json.tmp").ToArray();
        var held = paths.Take(128).Select(path => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)).ToArray();
        try
        {
            await CaptureDeletionService.RetryCleanupAsync(_root).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(paths[^1]));
            Assert.All(paths[..^1], path => Assert.True(File.Exists(path)));
        }
        finally
        {
            foreach (var stream in held) stream.Dispose();
            await CaptureDeletionService.RetryCleanupAsync(_root);
        }
    }

    /// <summary>T-I14. An unreadable record is surfaced and left exactly as it was.</summary>
    [Fact]
    public async Task CorruptOwnershipWithoutAPayloadIsVisibleAndNeverModified()
    {
        var metadata = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".vcat-deleting.json");
        await File.WriteAllTextAsync(metadata, "{broken", TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(metadata);
        await CaptureDeletionService.RetryCleanupAsync(_root);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(metadata));
        Assert.Equal("{broken", await File.ReadAllTextAsync(metadata, TestContext.Current.CancellationToken));
        Assert.Equal(1, (await CaptureDeletionService.InventoryAsync(_root)).UnresolvedCleanup);
    }

    [Fact]
    public async Task ADelayedIndexPublicationCannotRestoreADeletedSize()
    {
        var capture = await Capture();
        await File.WriteAllTextAsync(Path.Combine(capture.Path, "manifest.json"), "{}", TestContext.Current.CancellationToken);
        using var reached = new ManualResetEventSlim(); using var proceed = new ManualResetEventSlim();
        TemporarySessionRetentionService.BeforeIndexPublicationForTest = () => { reached.Set(); Assert.True(proceed.Wait(TimeSpan.FromSeconds(10))); };
        var scanning = TemporarySessionRetentionService.ScanAsync(_root);
        try
        {
            Assert.True(reached.Wait(TimeSpan.FromSeconds(10)));
            var target = await CaptureDeletionService.PrepareAsync(_root, capture);
            Assert.True(Assert.Single(await CaptureDeletionService.DeleteAsync(_root, [target])).Committed);
        }
        finally { proceed.Set(); await scanning; TemporarySessionRetentionService.BeforeIndexPublicationForTest = null; }
        Assert.False(File.Exists(Path.Combine(_root, ".session-sizes.json")));
    }


    public void Dispose()
    {
        CaptureDeletionService.TestPhase = null;

        // A scheduled cleanup worker may still be enumerating the root, which is exactly what
        // it is meant to do. Retry rather than racing it.
        for (var attempt = 0; attempt < 20 && Directory.Exists(_root); attempt++)
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}

[CollectionDefinition("Capture deletion filesystem", DisableParallelization = true)]
public sealed class CaptureDeletionTestGroup;
