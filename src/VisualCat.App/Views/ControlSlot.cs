using Avalonia.Automation;
using Avalonia.Controls;

namespace VisualCat.App.Views;

/// <summary>
/// Taking a control out of use without taking it out of the layout.
/// </summary>
/// <remarks>
/// Avalonia has no "hidden but arranged" visibility, and a control removed from a row of touch
/// targets moves every other target in that row: hiding Fit moved the Details segment's right
/// edge 158 px on the tap that hid it, so a second tap where Details had just been hit Split
/// and undid the switch (audit 3, C4).
///
/// A held control is invisible, untappable, disabled and out of the accessibility tree, while
/// still measuring and arranging as itself — so the slot it holds is exactly the slot it will
/// occupy when it comes back, at any text scale.
/// </remarks>
internal static class ControlSlot
{
    /// <remarks>
    /// Callers include layout callbacks that run on every pass, so a state that has not changed
    /// costs one comparison: five property writes per control per layout pass would be five
    /// property writes' worth of work and one boxed bool of garbage for nothing.
    /// </remarks>
    internal static void Hold(Control control, bool available)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsEnabled == available && control.Opacity == (available ? 1 : 0))
        {
            return;
        }

        control.Opacity = available ? 1 : 0;
        control.IsHitTestVisible = available;
        control.IsEnabled = available;
        AutomationProperties.SetAccessibilityView(
            control,
            available ? AccessibilityView.Default : AccessibilityView.Raw);
        AutomationProperties.SetIsControlElementOverride(control, available ? null : false);
    }
}
