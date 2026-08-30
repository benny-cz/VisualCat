using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.App.Presentation;

namespace VisualCat.App.Views;

/// <summary>
/// The source lines that never became entries, where a reader can actually read them.
/// </summary>
/// <remarks>
/// <para>
/// <see href="../../../docs/adr/0009-continuations.md">ADR 0009</see> decides that an
/// unmatched line stays unknown unless a declared grammar proves it is a continuation, on the
/// grounds that attaching every unmatched line to the entry before it hides malformed
/// evidence. Under ThreadTime there is no such grammar for an indented stack frame, so every
/// frame of every Android crash log is an unknown line — which is correct, and was completely
/// invisible: a 1 800-line crash corpus reported <c>600 in view · 600 match · 600 in
/// session</c> and <c>Ready · 600 entries</c>, and said nothing at all about the 1 200 lines a
/// person opens a crash log to read (V2-14).
/// </para>
/// <para>
/// Nothing about the parse changes here. The bytes were always kept and always correct; this
/// is the route to them that the product did not have. It reads forward through the session's
/// physical source stream in bounded pages, so a million-line session costs a bounded read
/// rather than a walk.
/// </para>
/// </remarks>
internal sealed class UnparsedLinesDialog : DialogBody<bool>
{
    private const int PageSize = 500;

    private readonly SessionTabViewModel _tab;
    private readonly SelectableTextBlock _lines;
    private readonly TextBlock _status;
    private readonly TextBlock _legend;
    private readonly Button _more;
    private readonly bool _mobile;
    private long _next;
    private int _shown;
    private bool _busy;

    internal UnparsedLinesDialog(SessionTabViewModel tab)
        : base("Lines not on the timeline")
    {
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
        _mobile = OperatingSystem.IsAndroid();
        PreferredSize = new Size(760, 620);
        MinimumSize = new Size(380, 360);
        ScrollsInternally = true;

        var counters = tab.Snapshot?.Descriptor.Counters;
        var unknown = counters?.UnknownLines ?? 0;
        var rejected = counters?.RejectedCandidates ?? 0;
        var untimed = counters?.UntimedEntries ?? 0;
        var continuations = tab.Snapshot?.Descriptor.Defects.Continuations ?? 0;

        var explanation = new TextBlock
        {
            Text =
                "Two kinds of line end up here, and neither can appear on a time axis. Lines " +
                "that are not logcat records at all — most often the indented frames of a stack " +
                "trace — are kept byte for byte and deliberately not attached to the entry " +
                "above: a line the parser could not read is evidence, and hiding it inside a " +
                "neighbouring message would lose that. Records that parsed but carry no usable " +
                "timestamp are real entries with nowhere on the plot to be drawn. Both are " +
                "counted separately from the timed entries every other view shows.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = TextScale.Of(12),
            Opacity = 0.85,
        };

        var tally = new TextBlock
        {
            Text = string.Join(
                "  ·  ",
                new[]
                {
                    unknown > 0 ? $"{unknown:N0} unknown" : null,
                    continuations > 0 ? $"{continuations:N0} continuation" : null,
                    rejected > 0 ? $"{rejected:N0} rejected" : null,
                    untimed > 0 ? $"{untimed:N0} untimed" : null,
                }.Where(static part => part is { Length: > 0 })),
            TextWrapping = TextWrapping.Wrap,
            FontSize = TextScale.Of(12),
            FontWeight = FontWeight.SemiBold,
        };

        _legend = ParseOutcomeLegend.Caption(TextScale.Of(10));
        _legend.IsVisible = true;

        _lines = new SelectableTextBlock
        {
            FontFamily = SessionWorkspaceView.MonoFont,
            FontSize = TextScale.Of(_mobile ? 11 : 12),
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(_lines, "Lines not on the timeline");

        _status = new TextBlock
        {
            Text = "Reading…",
            FontSize = TextScale.Of(11),
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };

        _more = new Button
        {
            Content = $"Load {PageSize:N0} more",
            MinHeight = TouchTarget.SelfSized(_mobile),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = false,
        };
        _more.Click += (_, _) => _ = LoadAsync();

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                tally,
                explanation,
                _legend,
                new ScrollViewer
                {
                    Content = _lines,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
                _status,
                _more,
            },
        };

        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            IsDefault = true,
            MinHeight = TouchTarget.SelfSized(_mobile),
        };
        close.Click += (_, _) => Complete(true);

        var copy = new Button
        {
            Content = "Copy",
            MinHeight = TouchTarget.SelfSized(_mobile),
        };
        AutomationProperties.SetName(copy, "Copy these lines");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && _lines.Text is { Length: > 0 } text)
            {
                await clipboard.SetTextAsync(text);
            }
        };

        Content = SheetForm.Build(
            body,
            SheetForm.Decision(null, copy, close),
            new Thickness(16));
    }

    /// <inheritdoc />
    protected override void OnPresented() => Dispatcher.UIThread.Post(() => _ = LoadAsync());

    private async Task LoadAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _more.IsEnabled = false;
        try
        {
            var page = await _tab.LoadUnparsedLinesAsync(_next, PageSize);
            _next = page.NextSequence;
            _shown += page.Count;
            if (page.Text.Length > 0)
            {
                _lines.Text += page.Text;
            }

            // A page that stops on the scan bound has found nothing yet and is not finished;
            // saying "none" there would be a lie, and saying nothing would look like a hang.
            _status.Text = page.Completed
                ? _shown == 0
                    ? "Every line in this file is a timed logcat record."
                    : $"{_shown:N0} shown · end of file."
                : $"{_shown:N0} shown · more of the file remains to be scanned.";
            _more.IsVisible = !page.Completed;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Reading was cancelled.";
            _more.IsVisible = false;
        }
        catch (IOException failure)
        {
            // Never the framework's own sentence: WorkspaceViewModel.FriendlyMessage is the
            // one place a thrown thing becomes something a reader is shown (finding F-04).
            _status.Text = $"The source bytes could not be read. {WorkspaceViewModel.FriendlyMessage(failure)}";
            _more.IsVisible = false;
        }
        finally
        {
            _busy = false;
            _more.IsEnabled = true;
        }
    }
}
