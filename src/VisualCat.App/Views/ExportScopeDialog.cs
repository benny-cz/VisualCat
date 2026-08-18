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
internal sealed record ExportScope(TimeRange Range, string Label, long? EstimatedRows);

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
    internal ExportScopeDialog(ExportScope inView, ExportScope allMatching)
        : base("Export CSV")
    {
        ArgumentNullException.ThrowIfNull(inView);
        ArgumentNullException.ThrowIfNull(allMatching);
        PreferredSize = new Size(520, 340);
        MinimumSize = new Size(400, 280);
        var mobile = OperatingSystem.IsAndroid();

        var options = new StackPanel { Spacing = 6 };
        var viewOption = Option(inView, "Only the entries the plot is currently showing.", mobile);
        var allOption = Option(
            allMatching,
            "Everything the current filter matches, across the whole session.",
            mobile);

        // The viewport is the scope the product used to take silently, so it stays the
        // default — but now it is a stated choice with its own row count beside it.
        viewOption.IsChecked = true;
        options.Children.Add(viewOption);
        options.Children.Add(allOption);

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
        confirm.Click += (_, _) => Complete(viewOption.IsChecked == true ? inView : allMatching);
        buttons.Children.Add(confirm);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "The sort order and the encoding come from Appearance & timeline; "
                           + "the filter comes from this session's workspace.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12,
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
                        FontSize = 11.5,
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
