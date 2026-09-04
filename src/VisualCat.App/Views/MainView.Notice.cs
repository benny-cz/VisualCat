using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.App.Timeline;

namespace VisualCat.App.Views;

/// <summary>
/// User-visible application notices that remain readable while Android workspace status updates.
/// </summary>
public sealed partial class MainView
{
    private Border? _noticeHost;
    private TextBlock? _noticeText;
    private ScrollViewer? _noticeScroller;
    private Button? _noticeDismiss;
    private Button? _noticeAction;
    private Button? _noticeExpand;
    private bool _noticeExpanded;
    private DispatcherTimer? _noticeTimer;
    private NoticeKind _noticeKind;
    private long _noticeRevision;
    private bool _noticeActionInFlight;

    /// <summary>
    /// One thing the reader can do about the message, offered beside it.
    /// </summary>
    /// <remarks>
    /// A notice that names a command and then leaves the reader to transcribe it from a phone
    /// screen has told them what to do without helping them do it, which is exactly the shape
    /// the restricted-capture message had (finding F-13). Optional: most notices are reports,
    /// and a report does not need a button.
    /// </remarks>
    /// <param name="Label">The verb, short enough for a 48 dp button beside Dismiss.</param>
    /// <param name="Invoke">What the button does.</param>
    internal sealed record NoticeAction(string Label, Func<Task> Invoke);

    /// <summary>What a notice is, which decides how long it stays.</summary>
    internal enum NoticeKind
    {
        /// <summary>
        /// A confirmation of something that happened inside the app and can be repeated at
        /// no cost — a copy, a filter. It gets a reading window and then gets out of the way.
        /// </summary>
        Information,

        /// <summary>
        /// Work is still running and the lane is the reader's only progress indicator. It
        /// remains until the operation replaces it with a result.
        /// </summary>
        Progress,

        /// <summary>
        /// A record that something durable happened outside the app: a file written, a
        /// session handed to another app, a cache emptied.
        /// </summary>
        /// <remarks>
        /// These used to vanish after six seconds like any other confirmation, so a reader
        /// who looked away during an export came back to a screen with no evidence that it
        /// had run and nowhere to look for any (audit 2, E8). The lane holds them until they
        /// are dismissed or another action replaces them, which is the same rule failures
        /// follow and for the same reason: the reader, not a timer, decides when they have
        /// read it.
        /// </remarks>
        Completion,

        /// <summary>Something did not work. Stays until dismissed.</summary>
        Failure,
    }

    /// <summary>
    /// Builds the Android operation-feedback lane. It is docked independently of each
    /// workspace's rapidly changing capture status so confirmations and failures remain
    /// readable while a live session updates several times per second.
    /// </summary>
    /// <summary>
    /// How tall the notice's own text may get on a phone, in logical pixels.
    /// </summary>
    /// <remarks>
    /// About six wrapped lines at this lane's type size, which is the height the lane used to
    /// take before the message was allowed to scroll. The lane spends this out of the
    /// workspace's own height, so it is a budget rather than a preference. It is applied on
    /// both platforms: the lane is hidden on the desktop, so one behaviour is simpler than
    /// two and this one can be asserted without a device.
    /// </remarks>
    private const double NoticeTextMaximumHeight = 108;

    private Border BuildNotice()
    {
        var fontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 12.5 : 12);
        var text = _noticeText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = fontSize,

            // The collapsed lane is two lines, and it was sized as FontSize * 1.4 * 2 while
            // the font's own line box drew nearer 1.2x. Two lines of budget therefore held
            // 2.33 drawn lines, and the third was painted into the box and sliced along its
            // x-height: on the device the stop notice read "VisualCat closed its Wireless /
            // debugging connection and / discarded the decrypted key" with that last line cut
            // in half lengthwise, which reads as broken rendering rather than as more text.
            // Stating the line box makes "two lines" a fact about the layout instead of a
            // guess about the font, so the same arithmetic sizes the lane on any device, at
            // any text scale, and for whatever face the platform resolves.
            LineHeight = NoticeLineBox(fontSize),
        };
        AutomationProperties.SetName(text, "Application status message");

        // Not MaxLines. The lane has to stay short - it takes its height out of the
        // workspace, and a tall one clips the workspace's own controls (finding F-32) - but
        // "short" was being bought by throwing the end of the message away. On a 360 dp
        // phone the declined-consent notice needs about eleven lines, so six of them were
        // drawn, with no ellipsis, and the sentence that fell off the end was the remedy the
        // notice exists to deliver: "Tap Live again and choose the option that allows
        // access." A screen reader heard it, because the accessible name carries the whole
        // string; the eye never reached it (finding F-33). The height is the same as six
        // lines bought before, and now the rest of the message is a scroll away instead of
        // gone.
        var scroller = _noticeScroller = new ScrollViewer
        {
            Content = text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = NoticeTextMaximumHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 48 dp, like every other actionable control on this platform. Both of these were
        // written as the below-plan literal 44 the touch-target audit named (finding F-26);
        // the dismiss target kept its 44 as a dead initialiser that a later edit could have
        // resurrected, so it goes through the same seam as everything else.
        var dismiss = _noticeDismiss = new Button
        {
            Content = "Dismiss",
            MinHeight = TouchTarget.Here(),
            Padding = new Thickness(10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(dismiss, "Dismiss application status message");
        dismiss.Click += (_, _) => DismissNotice();

        var action = _noticeAction = new Button
        {
            IsVisible = false,
            MinHeight = TouchTarget.Here(),
            Padding = new Thickness(10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        action.Click += async (_, _) =>
        {
            if (_noticeActionInFlight || _noticeActionHandler is not { } handler)
            {
                return;
            }

            _noticeActionInFlight = true;
            action.IsEnabled = false;
            try
            {
                await handler();
            }
            finally
            {
                _noticeActionInFlight = false;
                action.IsEnabled = _noticeActionHandler is not null;
            }
        };

        // A four-line scroll container is the worst of both: it spends the workspace's height
        // on a message and still cuts the message off, and the reader has to scroll inside a
        // notice to finish reading it (V2-11). Two lines is a lane; the rest is a disclosure.
        var expand = _noticeExpand = new Button
        {
            Content = "More",
            IsVisible = false,
            MinHeight = TouchTarget.Here(),
            Padding = new Thickness(10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(expand, "Show the whole message");
        expand.Click += (_, _) =>
        {
            _noticeExpanded = !_noticeExpanded;
            ApplyNoticeLayout(_noticeCompactHeight);
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 8,
        };
        content.Children.Add(scroller);
        Grid.SetColumn(expand, 1);
        content.Children.Add(expand);
        Grid.SetColumn(action, 2);
        content.Children.Add(action);
        Grid.SetColumn(dismiss, 3);
        content.Children.Add(dismiss);

        // Whether the message overflows is only knowable once it has been laid out, and it
        // changes with the text size and the viewport as well as with the message.
        scroller.LayoutUpdated += (_, _) => UpdateNoticeDisclosure();

        var host = _noticeHost = new Border
        {
            IsVisible = false,
            // The lane is docked at the application edge, but it is still a card the reader
            // has to recognise as scrollable. Omitting its bottom edge made the Motorola
            // landscape rendering look physically cut off even though every control was in
            // bounds. Draw the complete boundary; the border is inside the same height and
            // costs the workspace no additional row.
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            Child = content,
        };
        AutomationProperties.SetName(host, "Application status");

        // The lane is docked below the workspace, so its height comes out of the workspace's
        // band. Every open workspace is told how much, so the plot can give it up instead of
        // the analysis pane sliding 140 px up the screen under the reader's finger (V2-20).
        // Both signals are needed: the lane changes height with the message, and it appears
        // and disappears without changing size.
        host.SizeChanged += (_, _) => PublishNoticeReserve();
        host.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.Property == IsVisibleProperty)
            {
                PublishNoticeReserve();
            }
        };

        // The lane reports the result of something the reader started and then looked away
        // from, so a screen reader has to speak it without being asked. A failure interrupts;
        // a confirmation waits its turn. ShowNotice re-resolves this per message.
        AutomationProperties.SetLiveSetting(host, AutomationLiveSetting.Polite);
        ApplyNoticeTheme();
        return host;
    }

    /// <summary>
    /// Gives a compact-height workspace back enough vertical room for its analysis controls
    /// and at least one log row, while keeping every word of the notice scroll-reachable.
    /// </summary>
    /// <remarks>
    /// On the 360 dp-tall Samsung landscape viewport, the ordinary notice consumed 86 dp.
    /// The Entries pane was left 106 dp: 48 for its tabs, 48 for Time/Copy/Entry, and only
    /// 10 for the log. The first entry was consequently painted through the controls above
    /// it. In compact height the notice uses the same 48 dp floor as its buttons and removes
    /// only exterior vertical padding; the text remains uncapped inside its scroller.
    /// </remarks>
    internal void ApplyNoticeLayout(bool compactHeight)
    {
        if (_noticeHost is not { } host || _noticeScroller is not { } scroller)
        {
            return;
        }

        _noticeCompactHeight = compactHeight;
        scroller.MaxHeight = compactHeight
            ? CompactNoticeHeight
            : _noticeExpanded
                ? ExpandedNoticeHeight
                : CollapsedNoticeHeight;
        host.Padding = compactHeight ? new Thickness(10, 0) : new Thickness(10, 6);
        UpdateNoticeDisclosure();
    }

    private bool _noticeCompactHeight;

    /// <summary>
    /// What the lane may grow to once the reader has asked to see the whole message.
    /// </summary>
    /// <remarks>
    /// <see cref="NoticeTextMaximumHeight"/> was a budget for a lane that was always that tall,
    /// and it still cut the restricted-capture notice's last line in half when expanded. A cap
    /// is not a height: a short message takes what it needs and nothing more, so raising it
    /// costs the workspace nothing except in the one state the reader explicitly asked for.
    /// A quarter of the shell keeps two thirds of the screen for the log even then.
    /// </remarks>
    private double ExpandedNoticeHeight
    {
        get
        {
            var line = CurrentNoticeLineBox;
            var budget = Math.Max(NoticeTextMaximumHeight, Bounds.Height * 0.25);

            // Rounded up, not down: this is the state the reader explicitly asked for, so
            // spending the remainder of one line is the right way to pay for never slicing
            // one, and the lane still clears the old budget rather than dropping below it.
            return Math.Max(2, Math.Ceiling(budget / line)) * line;
        }
    }

    /// <summary>
    /// Two lines of the lane's own type, which is what it costs the workspace by default.
    /// </summary>
    /// <remarks>
    /// Derived from the drawn line box rather than written as a constant, so a reader who has
    /// raised their text size gets two lines of <em>their</em> text rather than a fixed band
    /// that holds one and a half of it.
    /// </remarks>
    private double CollapsedNoticeHeight => CurrentNoticeLineBox * 2;

    /// <summary>
    /// What the lane's text may take where the workspace is too short to spend two lines on it.
    /// </summary>
    /// <remarks>
    /// Rounded down into the touch floor rather than up out of it. The floor is what the row is
    /// already worth — Dismiss and the action button set its height, not the text — so taking
    /// whole lines out of it costs the workspace nothing and stops the lane from slicing the
    /// line at its edge. This is the state a split-screen pane and a landscape phone both land
    /// in, which makes it the one the slicing was most often seen in: stopping a wireless
    /// capture in a split-screen pane drew 2.67 lines of the closing notice, the last of them
    /// cut lengthwise, and compact height withholds the <em>More</em> disclosure as well, so the
    /// sliced line was the only sign that the sentence continued.
    /// </remarks>
    private double CompactNoticeHeight =>
        Math.Max(1, Math.Floor(TouchTarget.Minimum / CurrentNoticeLineBox)) * CurrentNoticeLineBox;

    /// <summary>
    /// The height of one drawn line of the lane's text, in logical pixels.
    /// </summary>
    /// <remarks>
    /// Read back from the control so the viewport and the text it holds cannot disagree, with
    /// the same arithmetic as a fallback for the window between construction and the first
    /// layout pass.
    /// </remarks>
    private double CurrentNoticeLineBox =>
        _noticeText is { LineHeight: > 0 } text
            ? text.LineHeight
            : NoticeLineBox(TextScale.Of(OperatingSystem.IsAndroid() ? 12.5 : 12));

    /// <summary>
    /// The line box the lane draws at, for a given drawn font size.
    /// </summary>
    /// <remarks>
    /// Whole logical pixels, so that a whole number of lines is also a whole number of pixels
    /// and the last one cannot be left a fraction short of the viewport that holds it.
    /// </remarks>
    private static double NoticeLineBox(double fontSize) => Math.Ceiling(fontSize * 1.4);

    /// <summary>
    /// Offers <em>More</em> exactly when there is more, and takes it away again when there is
    /// not — a message that fits must not carry a control that reveals nothing.
    /// </summary>
    private void UpdateNoticeDisclosure()
    {
        if (_noticeExpand is not { } expand || _noticeScroller is not { } scroller)
        {
            return;
        }

        var overflows = scroller.Extent.Height > scroller.Viewport.Height + 0.5;
        var offer = !_noticeCompactHeight && (overflows || _noticeExpanded);
        if (expand.IsVisible != offer)
        {
            expand.IsVisible = offer;
        }

        var label = _noticeExpanded ? "Less" : "More";
        if (expand.Content as string != label)
        {
            expand.Content = label;
            AutomationProperties.SetName(
                expand,
                _noticeExpanded ? "Show less of the message" : "Show the whole message");
        }
    }

    /// <summary>
    /// Publishes an operation result. Desktop keeps the compact brand-row message; Android
    /// additionally receives the always-visible notice lane because the brand row is removed
    /// as soon as a session opens.
    /// </summary>
    /// <param name="text">The message, or empty to clear the lane.</param>
    /// <param name="kind">What the message is, which decides how long the lane holds it.</param>
    /// <param name="action">One thing the reader can do about it, offered beside Dismiss.</param>
    /// <param name="dismissed">
    /// Run when the reader clears <em>this</em> message with Dismiss — not when another message
    /// replaces it, and not when a timer retires it.
    /// </param>
    /// <remarks>
    /// The dismissal callback exists because the Dismiss button is generic and the lane is
    /// shared, so a caller had no way to learn that its own notice was the one refused.
    /// Watching <see cref="NoticeRevision"/> from outside cannot substitute for it: it would
    /// fire every time an unrelated message took the lane. The update offer uses it to record
    /// that this version was declined, which is the difference between an offer and a nag.
    /// </remarks>
    internal void ShowNotice(
        string text,
        NoticeKind kind = NoticeKind.Information,
        NoticeAction? action = null,
        Action? dismissed = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowNotice(text, kind, action, dismissed));
            return;
        }

        _noticeDismissedHandler = dismissed;
        if (_noticeAction is { } actionButton)
        {
            actionButton.Content = action?.Label;
            actionButton.IsVisible = action is not null;
            actionButton.IsEnabled = action is not null && !_noticeActionInFlight;
            AutomationProperties.SetName(actionButton, action?.Label ?? string.Empty);
            _noticeActionHandler = action?.Invoke;
        }

        text ??= string.Empty;

        // The brand row's compact message is the desktop's only notice surface. On Android the
        // lane below carries the same words at full length and is always visible, so echoing
        // them into the brand row put the identical sentence on screen twice — obviously so
        // once the update offer made a long message the first thing a cold start shows.
        _message.Text = OperatingSystem.IsAndroid() ? string.Empty : text;
        _noticeKind = kind;
        var revision = ++_noticeRevision;

        if (_noticeText is not null)
        {
            _noticeText.Text = text;
        }

        // A new message starts collapsed. Carrying the previous one's expansion over would
        // give an unrelated one-line confirmation a four-line lane and a Less button.
        _noticeExpanded = false;
        ApplyNoticeLayout(_noticeCompactHeight);

        if (_noticeHost is { } host)
        {
            host.IsVisible = OperatingSystem.IsAndroid() && !string.IsNullOrWhiteSpace(text);
            AutomationProperties.SetLiveSetting(
                host,
                kind == NoticeKind.Failure ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        }

        ApplyNoticeTheme();
        StopNoticeTimer();

        // A repeatable in-app confirmation gets a guaranteed reading window and then gets out
        // of the way. Anything that recorded a durable result, and anything that failed,
        // stays until the reader dismisses it or begins another action.
        if (OperatingSystem.IsAndroid() && kind == NoticeKind.Information && !string.IsNullOrWhiteSpace(text))
        {
            _noticeTimer = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Background, (_, _) =>
            {
                if (_noticeRevision == revision)
                {
                    ShowNotice(string.Empty);
                }
                else
                {
                    StopNoticeTimer();
                }
            });
            _noticeTimer.Start();
        }
    }

    /// <summary>
    /// Takes down a notice this caller raised, and only if it is still the one showing.
    /// </summary>
    /// <remarks>
    /// A notice that turns out to have been wrong has to be able to retract itself, and the
    /// scope of an on-device capture is exactly that case: it is reported from an absence of
    /// evidence and revised when the evidence arrives (audit 3, A1). The revision check is
    /// what stops a retraction from clearing an unrelated message the reader has not read yet
    /// — a failed export raised in the meantime keeps the lane.
    /// </remarks>
    internal void RetractNotice(long revision)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RetractNotice(revision));
            return;
        }

        if (_noticeRevision == revision)
        {
            ShowNotice(string.Empty);
        }
    }

    /// <summary>Which message the lane is currently carrying.</summary>
    internal long NoticeRevision => _noticeRevision;

    /// <summary>
    /// What the lane is holding, or null when it is empty.
    /// </summary>
    /// <remarks>
    /// There is one notice lane in this product and there should stay one, so anything that
    /// wants to raise a message the reader did not ask for has to be able to see what it would
    /// be erasing. <see cref="ShowNotice"/> replaces whatever is there unconditionally, and the
    /// lane is where a failed export, a failed cleanup and — the one capture outcome the reader
    /// cannot see for themselves — a recording that stopped without being asked to are reported.
    /// An update offer arriving on resume must not be what takes those off the screen.
    /// </remarks>
    internal NoticeKind? HoldingNoticeKind =>
        string.IsNullOrWhiteSpace(_noticeText?.Text) ? null : _noticeKind;

    /// <summary>Whether the lane is holding a message that begins with these words.</summary>
    /// <remarks>
    /// Revision bookkeeping is the right instrument for retracting a message and the wrong one
    /// for rewriting one at the end of a long operation: anything that raised a notice in
    /// between — a cancellation report, a completion — advances the revision without
    /// necessarily leaving its own message on screen. What the caller actually needs to know
    /// is whether the words it wrote are still the words the reader is looking at (V2-11).
    /// </remarks>
    internal bool IsHoldingNoticeStartingWith(string prefix) =>
        _noticeText?.Text is { Length: > 0 } showing &&
        showing.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>
    /// Clears the lane the way the reader does, running the notice's own dismissal callback.
    /// </summary>
    internal void DismissNotice()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(DismissNotice);
            return;
        }

        var dismissed = _noticeDismissedHandler;
        ShowNotice(string.Empty);
        dismissed?.Invoke();

        // Ordered after the dismissal callback so anything the reader has just refused is
        // already remembered, and a message that was waiting for the lane can take it now
        // rather than at the next unrelated event.
        NoticeLaneFreed();
    }

    private Func<Task>? _noticeActionHandler;
    private Action? _noticeDismissedHandler;

    private void ApplyNoticeTheme()
    {
        if (_noticeHost is not { } host || _noticeText is not { } text ||
            _noticeDismiss is not { } dismiss || _noticeAction is not { } action)
        {
            return;
        }

        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var failure = _noticeKind == NoticeKind.Failure;
        var accent = failure
            ? Color.Parse("#FF6B70")
            : _noticeKind == NoticeKind.Completion
                ? Color.Parse(dark ? "#3FD69B" : "#0E7A52")
                : WorkspacePalette.Accent(dark);
        var surface = WorkspacePalette.SurfaceRaised(dark);
        host.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)248 : (byte)252, surface.R, surface.G, surface.B));
        host.BorderBrush = new SolidColorBrush(accent);
        text.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        dismiss.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        dismiss.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        dismiss.Background = new SolidColorBrush(WorkspacePalette.Surface(dark));
        action.Foreground = new SolidColorBrush(accent);
        action.BorderBrush = new SolidColorBrush(accent);
        action.Background = new SolidColorBrush(WorkspacePalette.Surface(dark));
    }

    /// <summary>Tells every open workspace how much height the lane is taking from it.</summary>
    private void PublishNoticeReserve()
    {
        var reserve = _noticeHost is { IsVisible: true } host ? host.Bounds.Height : 0;
        foreach (var item in _tabItems.Values)
        {
            if (item.Content is SessionWorkspaceView workspace)
            {
                workspace.SetNoticeReserve(reserve);
            }
        }
    }

    private void StopNoticeTimer()
    {
        if (_noticeTimer is { } timer)
        {
            timer.Stop();
            _noticeTimer = null;
        }
    }
}
