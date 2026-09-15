using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Application.UseCases;
using VisualCat.Core.Parsing;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Files;
using VisualCat.Infrastructure.Configuration;

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

    // ---------------------------------------------------------------- P-09

    [Fact]
    public async Task ASessionThatIsMissingAndOneThatIsRefusedSayDifferentThingsAsync()
    {
        // FileInfo.Exists answers false for "you may not look" exactly as it does for "it is
        // not there", so a session whose directory an ACL denied was reported as a missing
        // manifest — sending the reader after a file that is present and intact (P-09).
        var root = Path.Combine(Path.GetTempPath(), $"vcat-p09-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var absent = Path.Combine(root, "absent.vcat");
            Directory.CreateDirectory(absent);
            var missing = await Assert.ThrowsAsync<FileNotFoundException>(
                () => SessionStore.OpenAsync(absent, TestContext.Current.CancellationToken));
            Assert.Equal("Session manifest was not found.", missing.Message);

            if (OperatingSystem.IsWindows())
            {
                return;
            }

            // A directory with no execute bit cannot be traversed, which is the shape an ACL
            // denial takes at the filesystem layer. Root ignores the mode, so a test running
            // as root would prove nothing and is skipped.
            if (Environment.GetEnvironmentVariable("USER") == "root")
            {
                return;
            }

            var denied = Path.Combine(root, "denied.vcat");
            Directory.CreateDirectory(denied);
            await File.WriteAllTextAsync(
                Path.Combine(denied, "manifest.json"),
                "{}",
                TestContext.Current.CancellationToken);
            File.SetUnixFileMode(denied, UnixFileMode.UserRead);
            try
            {
                var refused = await Record.ExceptionAsync(
                    () => SessionStore.OpenAsync(denied, TestContext.Current.CancellationToken));
                Assert.NotNull(refused);
                Assert.IsNotType<FileNotFoundException>(refused);
                Assert.Contains("denied", refused!.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.SetUnixFileMode(
                    denied,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ---------------------------------------------------------------- A-16

    [Fact]
    public void EveryTransportStateADeviceCanBeInIsRecognised()
    {
        // "no permissions" is two words followed by advisory prose, so a parser that took the
        // second whitespace token read "no" and reported Unknown — losing the one state with a
        // specific remedy — and every colon-bearing word of the advice became a property, so the
        // URL arrived as a key called "[http" (A-16).
        const string Output = """
            List of devices attached
            RFCRC0A9GND            device product:r9qxeea model:SM_G990B device:r9q transport_id:1
            RFCRC0A9GNE            unauthorized usb:1-1 transport_id:3
            RFCRC0A9GNF            offline usb:1-1 transport_id:4
            RFCRC0A9GNG            no permissions (user in plugdev group; are your udev rules wrong?); see [http://developer.android.com/tools/device.html]
            RFCRC0A9GNH            recovery transport_id:9
            """;

        var devices = AdbDeviceParser.Parse(Output);
        Assert.Equal(5, devices.Count);

        Assert.Equal(AdbDeviceState.Device, devices[0].State);
        Assert.Equal("SM_G990B", devices[0].Model);
        Assert.Equal("1", devices[0].TransportId);

        Assert.Equal(AdbDeviceState.Unauthorized, devices[1].State);
        Assert.Equal(AdbDeviceState.Offline, devices[2].State);

        Assert.Equal(AdbDeviceState.NoPermissions, devices[3].State);
        Assert.StartsWith("no permissions", devices[3].StateText, StringComparison.Ordinal);
        Assert.DoesNotContain(devices[3].Properties.Keys, static key => key.StartsWith('['));
        Assert.Empty(devices[3].Properties);

        // A state this product does not model is still named rather than flattened to "Unknown".
        Assert.Equal(AdbDeviceState.Unknown, devices[4].State);
        Assert.Equal("recovery", devices[4].StateText);
        Assert.Equal("9", devices[4].TransportId);
    }

    // ------------------------------------------------- A-16, second half (F-34)

    [Fact]
    public void TheNoPermissionsLineARealDeviceProducesSurvivesItsAdvisoryUrl()
    {
        // Measured on a Samsung SM-G990B passed through to the Linux guest with the android
        // udev rule masked: adb names the account and the group it is missing, prints the
        // advisory URL, and only then the real properties. Taking the first colon-bearing word
        // as a property turned the URL into a key called "[http" and lost the state entirely.
        const string Output = """
            List of devices attached
            RFCRC0A9GND            no permissions (user vcatadb is not in the plugdev group); see [http://developer.android.com/tools/device.html] usb:2-1 transport_id:1
            """;

        var device = Assert.Single(AdbDeviceParser.Parse(Output));
        Assert.Equal(AdbDeviceState.NoPermissions, device.State);
        Assert.Contains("not in the plugdev group", device.StateText, StringComparison.Ordinal);
        Assert.Equal("2-1", device.Properties["usb"]);
        Assert.Equal("1", device.TransportId);
        Assert.Equal(2, device.Properties.Count);
    }

    [Fact]
    public void TheUsbProbeClaimsAPermissionProblemOnlyWhenItMeasuredOne()
    {
        // The probe exists to correct one message, so a false positive is worse than the defect
        // it fixes: every ordinary machine must come back with nothing, it must never throw
        // where /sys is absent or unreadable, and off Linux it must not look at all.
        var hidden = UsbDeviceAccess.UnopenableAdbDevices();
        var explanation = UsbDeviceAccess.MissingDeviceExplanation();

        Assert.Equal(hidden.Count == 0, explanation is null);
        Assert.All(hidden, entry => Assert.StartsWith("/dev/bus/usb/", entry, StringComparison.Ordinal));
        if (!OperatingSystem.IsLinux())
        {
            Assert.Empty(hidden);
        }
    }

    // ------------------------------------------------- lone-CR framing (F-36)

    [Fact]
    public void OnlyAProbedPrefixThatIsGenuinelyCarriageReturnFramedIsRefused()
    {
        // A false positive here refuses a good log, so the rule is deliberately narrow: exactly
        // one sample — which means the probed prefix held no line feed at all — and at least
        // three of its carriage-return-separated parts reading as logcat lines.
        static ReadOnlyMemory<byte> Bytes(string value) => Encoding.UTF8.GetBytes(value);
        const string Record = "05-15 14:13:37.000  1234  5678 I Tag : message";

        Assert.True(CarriageReturnFraming.IsCarriageReturnFramed(
            [Bytes($"{Record}\r{Record}\r{Record}\r{Record}")]));

        // Two records is a stray byte, not a framing convention.
        Assert.False(CarriageReturnFraming.IsCarriageReturnFramed([Bytes($"{Record}\r{Record}")]));

        // An LF-framed source: more than one sample, so a record quoted inside a message cannot
        // trip it however many carriage returns it carries.
        Assert.False(CarriageReturnFraming.IsCarriageReturnFramed(
            [Bytes($"{Record}\r{Record}\r{Record}\r{Record}"), Bytes(Record), Bytes(Record)]));

        // One enormous line of prose, and a CRLF line, are both ordinary.
        Assert.False(CarriageReturnFraming.IsCarriageReturnFramed([Bytes(new string('x', 100_000))]));
        Assert.False(CarriageReturnFraming.IsCarriageReturnFramed([Bytes($"{Record}\r")]));
        Assert.False(CarriageReturnFraming.IsCarriageReturnFramed([]));
    }

    /// <summary>
    /// F-08 — <c>settings.json</c> is owner-only, whatever the account's <c>umask</c> says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PRIVACY.md</c> promises that VisualCat "does not leave it to the account's umask", and
    /// the sessions honoured that. <c>settings.json</c> did not: it was <c>0644</c> on a stock
    /// macOS, and it holds the configured session directory, the configured ADB path, and a list
    /// of every recently open session's absolute path (finding F-08).
    /// </para>
    /// <para>
    /// Run under two umasks, because <c>022</c> is the macOS default while many Linux
    /// distributions use <c>002</c> or <c>077</c> — a test that only ever runs under one can
    /// pass while the promise is broken on the platform that matters. The mode is checked after
    /// the write, which is the only thing that proves the umask was overridden rather than
    /// happening to agree.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(0b000_010_010)]   // umask 022, the macOS and Windows-subsystem default
    [InlineData(0b000_000_010)]   // umask 002, the default on several Linux distributions
    public async Task SettingsAreOwnerOnlyWhateverTheUmaskIs(int umask)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var previous = Umask(umask);
        var root = Path.Combine(Path.GetTempPath(), $"vcat-f08-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            await new SettingsStore(path).SaveAsync(
                new ApplicationSettings(AdbPath: "/opt/homebrew/bin/adb"),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            _ = Umask(previous);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "umask", SetLastError = true)]
    private static extern int Umask(int mask);
}
