using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using VisualCat.Domain;

namespace VisualCat.App.Views;

/// <summary>
/// What the product is, which build this is, and where to take a question about it.
/// </summary>
/// <remarks>
/// <para>
/// macOS puts an <em>About</em> item at the top of every application menu and users expect the
/// build number to be behind it. VisualCat had none: the stock menu Avalonia synthesises offered
/// <c>About Avalonia</c>, which opens the framework's about box, so the product's own version —
/// the string a bug report needs — was reachable only from the empty state, and not at all once a
/// session was open (finding F-03).
/// </para>
/// <para>
/// The version shown is the full informational version, commit and all. Seven characters of
/// commit are what <c>git show</c> wants and what makes a screenshot of a crash actionable; the
/// empty state's footer trims it for width, and this dialog is the place that does not have to.
/// </para>
/// </remarks>
internal sealed class AboutDialog : DialogBody<bool>
{
    public AboutDialog()
        : base("About VisualCat")
    {
        PreferredSize = new Size(460, 420);
        MinimumSize = new Size(360, 160);
        SizesToContent = !DialogComposition.Mobile;

        var heading = new TextBlock
        {
            Text = "VisualCat",
            FontSize = TextScale.Of(22),
            FontWeight = FontWeight.SemiBold,
        };

        var version = new SelectableTextBlock
        {
            Text = ProductInfo.InformationalVersion,
            FontSize = TextScale.Of(12),
            TextWrapping = TextWrapping.Wrap,
        };

        // Selectable, because the one thing a reader does with a version string is copy it into
        // a bug report.
        AutomationProperties.SetName(version, $"Version {ProductInfo.InformationalVersion}");

        var strapline = new TextBlock
        {
            Text = "Interactive, local-first Android logcat analysis with a severity-by-time heat map.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            FontSize = TextScale.Of(12),
        };

        var privacy = new TextBlock
        {
            Text = "Processing is local. Nothing is uploaded and there is no telemetry.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            FontSize = TextScale.Of(12),
        };

        var licence = new TextBlock
        {
            Text = "MIT licensed. Bundled third-party components are listed in THIRD-PARTY-NOTICES.md.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.62,
            FontSize = TextScale.Of(11),
        };

        var close = SheetForm.PrimaryAction(new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinHeight = DialogComposition.Mobile ? 48 : 0,
        });
        close.Click += (_, _) => Complete(true);

        var stack = new StackPanel
        {
            Spacing = 10,
            Children = { heading, version, strapline, privacy, licence },
        };

        // Composed once, in one place. Adding the stack to the layout and then moving it into a
        // row is how a control ends up with two parents, which Avalonia refuses — and the
        // refusal reached the reader as "The control StackPanel already has a visual parent".
        Control body = stack;
        if (TryLoadIcon() is { } icon)
        {
            icon.Margin = new Thickness(0, 0, 14, 0);
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(stack);
            body = row;
        }

        var layout = new DockPanel { LastChildFill = true, Margin = new Thickness(4) };
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        layout.Children.Add(body);
        Content = layout;
    }

    /// <summary>
    /// The product icon, or nothing. An about box that fails to open because an asset moved
    /// would be a poor trade for a picture.
    /// </summary>
    private static Image? TryLoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://VisualCat.App/Assets/visualcat-icon.png"));
            return new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(stream),
                Width = 64,
                Height = 64,
                VerticalAlignment = VerticalAlignment.Top,
            };
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            return null;
        }
    }
}
