using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace VisualCat.App.Views;

/// <summary>
/// The one way this product declares and updates an accessibility live region.
/// </summary>
/// <remarks>
/// <para>
/// A live region is a control that announces itself when its content changes, so a reader who is
/// not looking at it still hears "Capturing…", "12 of 40 imported", or "That regular expression
/// is not valid". Every such control in the product goes through here.
/// </para>
/// <para>
/// It exists because of a crash. On macOS, Avalonia's bridge answers a live-region change by
/// building an <c>NSDictionary</c> for <c>NSAccessibilityPostNotificationWithUserInfo</c> from
/// the element's accessible name. <c>+[NSDictionary dictionaryWithObjects:forKeys:count:]</c>
/// raises <c>NSInvalidArgumentException</c> when any key or value is nil, nothing catches it, and
/// the Objective-C runtime aborts the process: no product message, no managed stack, no chance to
/// save. It was observed live after two and three quarter hours of ordinary use, with an
/// accessibility client attached, inside
/// <c>-[AvnAccessibilityElement raiseLiveRegionChanged]</c> (finding F-14).
/// </para>
/// <para>
/// The product's half of that is an ordering bug that was easy to write and impossible to see:
/// the announcement element was created with no text and no name, given a live setting, and only
/// named <em>after</em> the assignment that raised the change. For the duration of that raise its
/// accessible name was empty. The two rules below remove the whole shape of the defect rather
/// than one instance of it — a live region is never without a name, and a name is always in place
/// before the text that announces it.
/// </para>
/// <para>
/// It is also the right thing for the platforms that do not crash. An unnamed live region is one
/// a screen reader introduces as "text" with no idea what it belongs to, so naming it first is
/// what makes the announcement usable as well as safe.
/// </para>
/// </remarks>
internal static class LiveRegion
{
    /// <summary>
    /// Declares <paramref name="element"/> a live region with a name it can never lose.
    /// </summary>
    /// <param name="element">The control that announces.</param>
    /// <param name="name">
    /// What this region is — "Capture status", "Validation message". Never empty: it is the value
    /// the platform reads while the region has no content of its own.
    /// </param>
    /// <param name="setting">
    /// <see cref="AutomationLiveSetting.Polite"/> for progress and status,
    /// <see cref="AutomationLiveSetting.Assertive"/> for something the reader must act on.
    /// </param>
    public static void Attach(Control element, string name, AutomationLiveSetting setting)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Name first, then the live setting: an element is never a live region without a name,
        // not even for the instant between these two statements.
        AutomationProperties.SetName(element, name);
        AutomationProperties.SetLiveSetting(element, setting);
    }

    /// <summary>
    /// Announces <paramref name="text"/> from a live region, leaving it visible and named.
    /// </summary>
    /// <param name="element">The live region to announce from.</param>
    /// <param name="text">What it should now say; empty clears it.</param>
    /// <param name="fallbackName">
    /// What to answer with when <paramref name="text"/> is empty — the region's own name from
    /// <see cref="Attach"/>. Supplying it is what keeps a cleared region from announcing nothing
    /// under a nil name.
    /// </param>
    /// <returns>Whether the text changed, for callers that only act on a change.</returns>
    public static bool Announce(TextBlock element, string? text, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);

        var value = text ?? string.Empty;
        if (string.Equals(value, element.Text, StringComparison.Ordinal))
        {
            return false;
        }

        // The assignment below is what raises the live-region change, so the name it will be
        // announced under has to be in place before it, not after.
        AutomationProperties.SetName(element, string.IsNullOrWhiteSpace(value) ? fallbackName : value);
        element.Text = value;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="element"/> would announce under an empty name, for a debug
    /// assertion at the point a live region is built.
    /// </summary>
    internal static bool HasUsableName(Control element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return !string.IsNullOrWhiteSpace(AutomationProperties.GetName(element));
    }
}
