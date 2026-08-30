using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

/// <summary>Which entries an export should write, and how many that is.</summary>
/// <param name="Range">The time range the export query is run over.</param>
/// <param name="Label">How the chosen scope is named back to the reader.</param>
/// <param name="EstimatedRows">Rows the workspace expects, or <c>null</c> when it cannot say.</param>
/// <param name="Detail">The one line that says what the scope means.</param>
/// <param name="IgnoresFilter">
/// Whether this scope writes the whole session rather than what the filter admits.
/// </param>
/// <remarks>
/// The filter used to be implicit and unconditional: every export ran through the workspace's
/// filter, so "everything in this session" was not a scope the product could produce at all —
/// and the one mistake B-14 is about is exporting a filtered view believing it was the whole
/// session (V2-15). The scope now carries that decision rather than the caller assuming it.
/// </remarks>
internal sealed record ExportScope(
    TimeRange Range,
    string Label,
    long? EstimatedRows,
    string Detail = "",
    bool IgnoresFilter = false);

/// <summary>
/// Asks what "export" means before the save dialog opens.
/// </summary>
/// <remarks>
/// The menu entry read "Write the filtered entries as CSV" and the save dialog was titled
/// "Export filtered entries", but the range the export ran over was the timeline viewport.
/// With the plot zoomed into a burst, a session of 50 156 matching entries wrote 32 510 rows
/// and nothing anywhere in the flow said so — the file looked complete (finding 10). The
/// workspace's own vocabulary already separates "in view" from "match the filter"; this asks
/// the question in those words and states the row count of each answer.
/// </remarks>
internal sealed class ExportScopeDialog : DialogBody<ExportScope>
{
    internal ExportScopeDialog(IReadOnlyList<ExportScope> scopes)
        : base("Export CSV")
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0)
        {
            throw new ArgumentException("An export needs at least one scope to offer.", nameof(scopes));
        }

        PreferredSize = new Size(520, 380);
        MinimumSize = new Size(400, 300);
        var mobile = OperatingSystem.IsAndroid();

        var options = new StackPanel { Spacing = 6 };
        var buttonsByScope = new List<(RadioButton Option, ExportScope Scope)>(scopes.Count);
        foreach (var scope in scopes)
        {
            var option = Option(scope, scope.Detail, mobile);
            buttonsByScope.Add((option, scope));
            options.Children.Add(option);
        }

        // The scope the product used to take silently is still the default — it is the first
        // one the caller offers — but it is a stated choice with its own row count beside it.
        buttonsByScope[0].Option.IsChecked = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        buttons.Children.Add(cancel);
        var confirm = new Button { Content = "Choose a file…", IsDefault = true, MinHeight = mobile ? 48 : 0 };
        confirm.Click += (_, _) =>
        {
            foreach (var (option, scope) in buttonsByScope)
            {
                if (option.IsChecked == true)
                {
                    Complete(scope);
                    return;
                }
            }

            Complete(buttonsByScope[0].Scope);
        };
        buttons.Children.Add(confirm);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "The sort order and the encoding come from Appearance & timeline.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = TextScale.Of(12),
                },
                options,
                buttons,
            },
        };
    }

    private static RadioButton Option(ExportScope scope, string detail, bool mobile)
    {
        var heading = scope.EstimatedRows is { } rows
            ? $"{scope.Label} — {rows:N0} rows"
            : scope.Label;
        var option = new RadioButton
        {
            GroupName = "ExportScope",
            MinHeight = mobile ? 56 : 0,
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = detail,
                        FontSize = TextScale.Of(11.5),
                        Opacity = 0.72,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        AutomationProperties.SetName(option, heading);
        AutomationProperties.SetHelpText(option, detail);
        return option;
    }
}
