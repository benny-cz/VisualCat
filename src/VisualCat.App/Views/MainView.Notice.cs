using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    private Button? _noticeDismiss;
    private DispatcherTimer? _noticeTimer;
    private NoticeKind _noticeKind;
    private long _noticeRevision;

    /// <summary>What a notice is, which decides how long it stays.</summary>
    internal enum NoticeKind
    {
        /// <summary>
        /// A confirmation of something that happened inside the app and can be repeated at
        /// no cost — a copy, a filter. It gets a reading window and then gets out of the way.
        /// </summary>
        Information,

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
    private Border BuildNotice()
    {
        var text = _noticeText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 12.5 : 12),
        };
        AutomationProperties.SetName(text, "Application status message");

        var dismiss = _noticeDismiss = new Button
        {
            Content = "Dismiss",
            MinHeight = OperatingSystem.IsAndroid() ? 44 : 0,
            Padding = new Thickness(10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(dismiss, "Dismiss application status message");
        dismiss.Click += (_, _) => ShowNotice(string.Empty);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };
        content.Children.Add(text);
        Grid.SetColumn(dismiss, 1);
        content.Children.Add(dismiss);

        var host = _noticeHost = new Border
        {
            IsVisible = false,
            BorderThickness = new Thickness(1, 1, 1, 0),
            Padding = new Thickness(10, 6),
            Child = content,
        };
        AutomationProperties.SetName(host, "Application status");

        // The lane reports the result of something the reader started and then looked away
        // from, so a screen reader has to speak it without being asked. A failure interrupts;
        // a confirmation waits its turn. ShowNotice re-resolves this per message.
        AutomationProperties.SetLiveSetting(host, AutomationLiveSetting.Polite);
        ApplyNoticeTheme();
        return host;
    }

    /// <summary>
    /// Publishes an operation result. Desktop keeps the compact brand-row message; Android
    /// additionally receives the always-visible notice lane because the brand row is removed
    /// as soon as a session opens.
    /// </summary>
    internal void ShowNotice(string text, NoticeKind kind = NoticeKind.Information)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowNotice(text, kind));
            return;
        }

        text ??= string.Empty;
        _message.Text = text;
        _noticeKind = kind;
        var revision = ++_noticeRevision;

        if (_noticeText is not null)
        {
            _noticeText.Text = text;
        }

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

    private void ApplyNoticeTheme()
    {
        if (_noticeHost is not { } host || _noticeText is not { } text || _noticeDismiss is not { } dismiss)
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
