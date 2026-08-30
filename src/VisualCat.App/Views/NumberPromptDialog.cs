using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VisualCat.App.Views;

/// <summary>
/// Asks for one number inside a stated range.
/// </summary>
/// <remarks>
/// <para>
/// Built for the search counter. `3,579 / 7,181` told the reader exactly where they were among
/// 7,181 matches and was not a control, so the only way to reach match 12 was to step to it —
/// and with the counter opening at the caret's position rather than at the first match, that
/// could be thousands of taps (V2-07). The stepper and the two edge buttons cover "next" and
/// "the ends"; this covers "that one".
/// </para>
/// <para>
/// A <see cref="NumericUpDown"/> rather than a free text box, because the answer is bounded on
/// both sides and the control can say so itself — and because
/// <see cref="SheetForm.PrepareSpinButtons"/> already gives its two spin buttons names and a
/// touch target, which is the part a phone gets wrong when this is built by hand.
/// </para>
/// </remarks>
internal sealed class NumberPromptDialog : DialogBody<long?>
{
    private readonly NumericUpDown _value;

    /// <param name="title">The dialog's own heading.</param>
    /// <param name="question">The one line above the field.</param>
    /// <param name="initial">Where the field starts, clamped into range.</param>
    /// <param name="minimum">The smallest acceptable answer.</param>
    /// <param name="maximum">The largest acceptable answer.</param>
    internal NumberPromptDialog(string title, string question, long initial, long minimum, long maximum)
        : base(title)
    {
        PreferredSize = new Size(420, 250);
        MinimumSize = new Size(340, 220);
        var mobile = OperatingSystem.IsAndroid();

        _value = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = 1,
            FormatString = "0",
            Value = Math.Clamp(initial, minimum, maximum),
            MinHeight = TouchTarget.SelfSized(mobile),
        };
        AutomationProperties.SetName(_value, question);
        // A short label: the spin buttons prefix it, and "Increase Which of the 7,181
        // matches?" is what a screen reader reads when the question is passed through.
        SheetForm.PrepareSpinButtons(_value, "match number");

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinHeight = TouchTarget.SelfSized(mobile),
            MinWidth = TouchTarget.SelfSized(mobile),
        };
        cancel.Click += (_, _) => Complete(null);

        // Two characters size themselves to about 36 dp, which the device measured. Height
        // alone is not the floor: both edges are.
        var confirm = new Button
        {
            Content = "Go",
            IsDefault = true,
            MinHeight = TouchTarget.SelfSized(mobile),
            MinWidth = TouchTarget.SelfSized(mobile),
        };
        confirm.Click += (_, _) => Complete(
            _value.Value is { } chosen
                ? (long)Math.Clamp(chosen, minimum, maximum)
                : null);

        Content = SheetForm.Build(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = question,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = TextScale.Of(13),
                    },
                    _value,
                    new TextBlock
                    {
                        Text = $"{minimum:N0} to {maximum:N0}",
                        FontSize = TextScale.Of(11),
                        Opacity = 0.75,
                    },
                },
            },
            SheetForm.Decision(null, cancel, confirm),
            new Thickness(16));
    }

    /// <inheritdoc />
    protected override void OnPresented() => _value.Focus();
}
