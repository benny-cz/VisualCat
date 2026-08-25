using VisualCat.Domain;

namespace VisualCat.Domain.Tests;

/// <summary>
/// The build's own identity, as the update rules read it.
/// </summary>
/// <remarks>
/// Two things are decided here that nothing else can check. The channel is derived from the
/// version string rather than a separate build property so it cannot drift from the tag that
/// produced the artifact — but that only helps if every version shape the pipeline can emit
/// lands where it should. And the version code is the mirror of the arithmetic in
/// <c>src/VisualCat.Android/VisualCat.Android.csproj</c>: Google Play reports the version code
/// of the build it would install and never a version name, so this decoder is the only reason
/// an update offer can be named to the reader at all.
/// </remarks>
public sealed class ReleaseChannelTests
{
    [Theory]
    // What Directory.Build.props stamps on anything the release pipeline did not build.
    [InlineData("2.1.0-dev", ReleaseChannel.Development)]
    [InlineData("2.1.0-dev+abc1234", ReleaseChannel.Development)]
    // What an untagged workflow_dispatch run produces. An internal build tests the pipeline
    // rather than the product, and must not prompt.
    [InlineData("2.1.0-preview.7", ReleaseChannel.Development)]
    [InlineData("2.1.0-alpha", ReleaseChannel.Alpha)]
    [InlineData("2.1.0-alpha.2", ReleaseChannel.Alpha)]
    [InlineData("2.1.0-ALPHA.2", ReleaseChannel.Alpha)]
    [InlineData("2.1.0-beta", ReleaseChannel.Beta)]
    [InlineData("2.1.0-beta.1", ReleaseChannel.Beta)]
    [InlineData("2.1.0", ReleaseChannel.Stable)]
    [InlineData("2.0.9+deadbeef", ReleaseChannel.Stable)]
    // Anything unrecognisable is Development, the conservative answer: a build nobody can
    // identify does not nag.
    [InlineData("", ReleaseChannel.Development)]
    [InlineData("nightly", ReleaseChannel.Development)]
    [InlineData(null, ReleaseChannel.Development)]
    public void TheChannelIsReadOffTheVersionString(string? version, ReleaseChannel expected) =>
        Assert.Equal(expected, ProductInfo.ChannelOf(version));

    [Theory]
    [InlineData("2.0.9", 2000900L)]
    [InlineData("2.1.0", 2010000L)]
    [InlineData("2.1.0-beta.1", 2010000L)]
    [InlineData("3.12.7", 3120700L)]
    public void AVersionMapsToTheAndroidVersionCode(string version, long expected) =>
        Assert.Equal(expected, ProductInfo.VersionCodeOf(version));

    [Theory]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("2.100.0")]
    [InlineData(null)]
    public void AVersionTheSchemeCannotExpressHasNoCode(string? version) =>
        Assert.Null(ProductInfo.VersionCodeOf(version));

    [Theory]
    [InlineData(2000900L, "2.0.9")]
    [InlineData(2010003L, "2.1.0")]
    [InlineData(2010099L, "2.1.0")]
    [InlineData(3120705L, "3.12.7")]
    public void AVersionCodeDecodesBackToItsVersionName(long code, string expected) =>
        Assert.Equal(expected, ProductInfo.VersionNameOf(code));

    /// <summary>
    /// Every build in the wild up to 2.0.9 carries a code from the old
    /// <c>major * 10000 + minor * 100 + patch</c> scheme. They have to keep decoding, or the
    /// first offer this feature ever makes is the one that cannot be named.
    /// </summary>
    [Theory]
    [InlineData(20009L, "2.0.9")]
    [InlineData(20100L, "2.1.0")]
    [InlineData(10000L, "1.0.0")]
    public void LegacyVersionCodesStillDecode(long code, string expected) =>
        Assert.Equal(expected, ProductInfo.VersionNameOf(code));

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(77L)]
    public void AnImplausibleCodeIsNotGivenAVersion(long code) =>
        Assert.Null(ProductInfo.VersionNameOf(code));

    /// <summary>
    /// The two directions agree, which is the property the Android formula depends on: a
    /// promoted build and the app's own idea of what it is running must not disagree.
    /// </summary>
    [Theory]
    [InlineData("2.0.9")]
    [InlineData("2.1.0")]
    [InlineData("2.99.99")]
    [InlineData("10.5.3")]
    public void TheSchemeRoundTrips(string version) =>
        Assert.Equal(version, ProductInfo.VersionNameOf(ProductInfo.VersionCodeOf(version)!.Value));

    /// <summary>
    /// The scheme change did not strand anyone: every code the new formula can produce is
    /// strictly greater than every code the old one could, so no installed build is ever
    /// refused an update for being numerically ahead of its successor.
    /// </summary>
    [Fact]
    public void EveryNewCodeOutranksEveryLegacyCode()
    {
        const long largestLegacyCodeForVersion2 = 29999;
        Assert.True(ProductInfo.VersionCodeOf("2.0.0") > largestLegacyCodeForVersion2);
    }
}
