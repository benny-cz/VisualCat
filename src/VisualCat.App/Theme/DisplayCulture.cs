using System.Globalization;

namespace VisualCat.App;

/// <summary>
/// The one culture every number and date in the product is formatted with.
/// </summary>
/// <remarks>
/// The interface is written in English and every string in it is a literal, but the values
/// inside those strings were being formatted with whatever culture the device was set to. On
/// a Czech phone that produced <c>19,76 min</c>, <c>1,0 µs/px</c>, <c>34,63 KiB</c>,
/// <c>18.08.2026</c> and <c>59 640</c> with a non-breaking-space group separator, sitting
/// inside English sentences — two conventions in one line, which makes both look accidental
/// rather than either look chosen (audit 2, E1). Until the interface itself is translated,
/// the honest answer is to format in the interface's own culture.
///
/// Invariant is the base, so the group separator is a comma and the decimal point a period —
/// what the surrounding English expects. The date and time patterns are replaced with ISO
/// ones, because that is already the product's own style: the timeline axis draws
/// <c>yyyy-MM-dd HH:mm</c>, entry rows draw <c>HH:mm:ss.ffffff</c>, and a session list that
/// said <c>08/18/2026</c> beside them would be a third convention rather than a second.
/// </remarks>
internal static class DisplayCulture
{
    /// <summary>The formatting culture, shared by every thread the application starts.</summary>
    internal static CultureInfo Current { get; } = Build();

    /// <summary>
    /// Installs the display culture on this thread and on every thread started afterwards.
    /// </summary>
    internal static void Install()
    {
        CultureInfo.DefaultThreadCurrentCulture = Current;
        CultureInfo.CurrentCulture = Current;
    }

    private static CultureInfo Build()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.LongDatePattern = "yyyy-MM-dd";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        culture.DateTimeFormat.LongTimePattern = "HH:mm:ss";
        return CultureInfo.ReadOnly(culture);
    }
}
