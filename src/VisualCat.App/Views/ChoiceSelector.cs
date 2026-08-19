using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using VisualCat.App.Theme;

namespace VisualCat.App.Views;

/// <summary>
/// A short settings choice shown as segments instead of as a dropdown.
/// </summary>
/// <remarks>
/// A <see cref="ComboBox"/> answers a question by opening a popup over the page, and on the
/// in-page sheet presentation — the only one Android ever gets — that popup went somewhere
/// nobody could use. Tapping <em>Default export order</em> scrolled the form back to the top
/// and drew its list floating over <em>Timeline normalization</em>, four fields away, while
/// the control it belonged to had scrolled off the screen entirely (audit 2, A4).
///
/// Every choice in the settings sheet has two or three options with short labels, so there
/// was never anything for a popup to do: the options fit on the page. Showing them costs one
/// row of a surface that already scrolls, removes a tap, removes the popup and the placement
/// question with it, and makes the current value readable without opening anything.
///
/// The appearance is resolved by key rather than captured, so a theme change repaints it
/// without the selector being rebuilt (see <see cref="ProductTheme"/>).
/// </remarks>
internal sealed class ChoiceSelector : ContentControl
{
    private readonly List<Segment> _segments = [];

    private sealed record Segment(SettingChoice Choice, ToggleButton Button);

    internal ChoiceSelector(string name, SettingChoice[] choices, string? value)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Length == 0)
        {
            throw new ArgumentException("A choice selector needs at least one option.", nameof(choices));
        }

        // Wrapping rather than an equal-width strip: "Follow the system" and "Square root"
        // are real labels, and a phone row that cannot hold three of them should take a
        // second line rather than truncate every option to four characters.
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
        };
        var selected = SettingChoice.Resolve(choices, value);
        Value = selected.Value;
        foreach (var choice in choices)
        {
            var button = new ToggleButton
            {
                Content = choice.Label,
                IsChecked = ReferenceEquals(choice, selected),
                MinHeight = 48,
                Padding = new Thickness(14, 0),
                CornerRadius = new CornerRadius(7),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, $"{name}: {choice.Label}");
            var segment = new Segment(choice, button);
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    Select(segment);
                }
                else if (string.Equals(Value, choice.Value, StringComparison.Ordinal))
                {
                    // A segmented control is a choice, not a set of switches: the chosen
                    // segment cannot be turned off, only replaced.
                    button.IsChecked = true;
                }
            };
            _segments.Add(segment);
            panel.Children.Add(button);
        }

        ApplyAppearance();
        AutomationProperties.SetName(this, name);
        Content = panel;
    }

    /// <summary>The value of the chosen option, in the form settings.json stores.</summary>
    internal string Value { get; private set; }

    private void Select(Segment chosen)
    {
        Value = chosen.Choice.Value;
        foreach (var segment in _segments)
        {
            if (!ReferenceEquals(segment, chosen))
            {
                segment.Button.IsChecked = false;
            }
        }

        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        foreach (var (choice, button) in _segments)
        {
            var chosen = string.Equals(choice.Value, Value, StringComparison.Ordinal);
            button.FontWeight = chosen ? FontWeight.SemiBold : FontWeight.Normal;
            button[!TemplatedControl.BackgroundProperty] =
                new DynamicResourceExtension(chosen ? ProductTheme.SelectionFillKey : ProductTheme.ControlFillKey);
            button[!TemplatedControl.BorderBrushProperty] =
                new DynamicResourceExtension(chosen ? ProductTheme.SelectionEdgeKey : ProductTheme.ControlEdgeKey);
            button[!TemplatedControl.ForegroundProperty] =
                new DynamicResourceExtension(chosen ? ProductTheme.TextPrimaryKey : ProductTheme.TextMutedKey);
            button.BorderThickness = new Thickness(1);
        }
    }
}
