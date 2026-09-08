using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Views;

public sealed partial class MainView
{
    private readonly FileOperationOwner _fileOperations = new();
    private readonly Border _fileOperationBand = new() { IsVisible = false };
    private readonly TextBlock _fileOperationText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ProgressBar _fileOperationProgress = new()
    {
        IsIndeterminate = true,
        Height = 4,
        MinWidth = 80,
        VerticalAlignment = VerticalAlignment.Center,
    };
    /// <summary>Carries the spoken stage only, so counts cannot flood a live region.</summary>
    private readonly TextBlock _fileOperationAnnouncement = new()
    {
        Height = 0,
        IsHitTestVisible = false,
    };

    private readonly Button _fileOperationCancel = new()
    {
        Content = "Cancel",
        MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ScrollViewer? _fileOperationScroller;
    private bool _fileOperationCompactHeight;
    private Guid _fileOperationCounted;
    private bool _fileOperationShowsCounts;
    private bool _restoreFileOperationInvokerWhenDone;

    /// <summary>
    /// How long an operation runs before the card starts putting numbers in front of the
    /// reader.
    /// </summary>
    /// <remarks>
    /// The acknowledgement is immediate — a file command must never look ignored — but a copy
    /// that finishes in 40 ms should not flash "Copying file · 2.1 MB of 2.1 MB" on its way
    /// past. Work that is still going after this is work worth counting.
    /// </remarks>
    private static readonly TimeSpan CountedProgressDelay = TimeSpan.FromMilliseconds(150);
    private WeakReference<Control>? _fileOperationInvoker;

    /// <summary>
    /// Remembers the command the reader used, so focus can return to it when the card goes.
    /// Held weakly: a toolbar rebuild must not be kept alive by a finished operation.
    /// </summary>
    internal void RememberFileOperationInvoker(Control? invoker) =>
        _fileOperationInvoker = invoker is null ? null : new WeakReference<Control>(invoker);

    /// <summary>Reads the focused command before its operation changes enabled state.</summary>
    private Control? FocusedFileOperationInvoker() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;

    private Border BuildFileOperationBand()
    {
        // Two rows rather than three columns: at 320 dp a fixed progress column would push
        // Cancel off the edge, and the plan requires Cancel to stay outside the text's own
        // scroller. Stage text scrolls; Cancel never does; the bar spans the full width.
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 6,
        };
        var scroller = _fileOperationScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Center,
            Content = _fileOperationText,
        };
        content.Children.Add(scroller);
        Grid.SetColumn(_fileOperationCancel, 1);
        content.Children.Add(_fileOperationCancel);
        Grid.SetRow(_fileOperationProgress, 1);
        Grid.SetColumnSpan(_fileOperationProgress, 2);
        content.Children.Add(_fileOperationProgress);
        AutomationProperties.SetLiveSetting(_fileOperationAnnouncement, AutomationLiveSetting.Polite);
        Grid.SetRow(_fileOperationAnnouncement, 1);
        content.Children.Add(_fileOperationAnnouncement);

        _fileOperationBand.Padding = new Thickness(12, 7);
        _fileOperationBand.BorderThickness = new Thickness(0, 1, 0, 0);
        _fileOperationBand.Child = content;
        AutomationProperties.SetName(_fileOperationBand, "File operation progress");
        _fileOperationCancel.Click += (_, _) => RequestFileOperationCancellation();
        _fileOperationBand.KeyDown += (_, args) =>
        {
            // Escape inside the card requests cancellation once (plan §7.3). A second press
            // while Cancelling is inert because RequestCancellation ignores it.
            if (args.Key == Key.Escape && _fileOperations.Current is { CanCancel: true })
            {
                RequestFileOperationCancellation();
                args.Handled = true;
            }
        };
        ApplyFileOperationLayout(_fileOperationCompactHeight);
        _fileOperations.PropertyChanged += OnFileOperationChanged;
        RenderFileOperation();
        return _fileOperationBand;
    }

    /// <summary>
    /// Caps the stage text the way the notice lane caps its own, so a long provider path
    /// cannot grow the band into the workspace on a short landscape phone.
    /// </summary>
    internal void ApplyFileOperationLayout(bool compactHeight)
    {
        _fileOperationCompactHeight = compactHeight;
        if (_fileOperationScroller is not { } scroller)
        {
            return;
        }

        scroller.MaxHeight = compactHeight ? CompactNoticeHeight : CollapsedNoticeHeight;
        _fileOperationBand.Padding = compactHeight ? new Thickness(12, 2) : new Thickness(12, 7);
    }

    /// <summary>
    /// Requests cancellation once and moves focus off a button that is about to be disabled.
    /// </summary>
    private void RequestFileOperationCancellation()
    {
        var wasFocused = _fileOperationCancel.IsFocused;
        _restoreFileOperationInvokerWhenDone |= IsFocusInsideFileOperationCard();
        _fileOperations.Cancel();
        if (wasFocused && !_fileOperationCancel.IsEnabled)
        {
            MoveFocusOffFileOperationCard();
        }
    }

    /// <summary>The shell's one file-operation owner, for tests that drive it directly.</summary>
    internal FileOperationOwner FileOperationsForTest => _fileOperations;

    /// <summary>The docked operation card, for tests that read what it shows.</summary>
    internal Border FileOperationBandForTest => _fileOperationBand;

    private void OnFileOperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        _ = sender;
        if (args.PropertyName is nameof(FileOperationOwner.Current) or nameof(FileOperationOwner.IsBusy))
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateSessionActionAvailability();
                RenderFileOperation();
            });
        }
    }

    private void RenderFileOperation()
    {
        var operation = _fileOperations.Current is { ShowsCard: true } showing ? showing : null;
        var hadFocus = _fileOperationBand.IsVisible && IsFocusInsideFileOperationCard();
        _fileOperationBand.IsVisible = operation is not null;
        if (operation is not null && operation.OperationId != _fileOperationCounted)
        {
            // A new operation starts uncounted, and stays uncounted unless it lasts.
            _fileOperationCounted = operation.OperationId;
            _fileOperationShowsCounts = false;
            var started = operation.OperationId;
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (_fileOperations.Current?.OperationId != started)
                    {
                        return;
                    }

                    _fileOperationShowsCounts = true;
                    RenderFileOperation();
                },
                CountedProgressDelay);
        }

        if (operation is null)
        {
            // A card that vanishes under the reader's focus must hand it somewhere real,
            // and must leave focus alone when it was never inside the card (plan §3.4).
            if (hadFocus || _restoreFileOperationInvokerWhenDone)
            {
                MoveFocusOffFileOperationCard();
            }

            _restoreFileOperationInvokerWhenDone = false;
            _fileOperationInvoker = null;
            return;
        }

        _fileOperationText.Text = Describe(operation, _fileOperationShowsCounts);
        AutomationProperties.SetName(_fileOperationBand, _fileOperationText.Text);

        // Stages and terminal outcomes are announced; a count moving five times a second is
        // not. The spoken line therefore changes only when the stage does, while the visible
        // text and the accessible progress value stay current for anyone who looks or focuses.
        var stage = Describe(operation, withCounts: false);
        if (!string.Equals(stage, _fileOperationAnnouncement.Text, StringComparison.Ordinal))
        {
            _fileOperationAnnouncement.Text = stage;
            AutomationProperties.SetName(_fileOperationAnnouncement, stage);
        }
        _fileOperationCancel.IsEnabled = operation.CanCancel;
        AutomationProperties.SetHelpText(
            _fileOperationCancel,
            operation.CanCancel ? $"Cancel {operation.Title}" : "This operation is finishing and can no longer be cancelled.");

        var progress = operation.Progress;
        var counted = _fileOperationShowsCounts && progress?.Total is > 0;
        _fileOperationProgress.IsIndeterminate = !counted;
        if (counted && progress!.Value.Total is { } total)
        {
            _fileOperationProgress.Minimum = 0;
            _fileOperationProgress.Maximum = total;
            _fileOperationProgress.Value = Math.Clamp(progress.Value.Completed, 0, total);
        }
    }

    private bool IsFocusInsideFileOperationCard()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused)
        {
            return false;
        }

        for (var visual = focused; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, _fileOperationBand))
            {
                return true;
            }
        }

        return false;
    }

    private void MoveFocusOffFileOperationCard()
    {
        if (_fileOperationInvoker is not null &&
            _fileOperationInvoker.TryGetTarget(out var invoker) &&
            invoker is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } and IInputElement)
        {
            invoker.Focus();
            return;
        }

        foreach (var child in _toolbar.Children)
        {
            if (child is Button { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } button)
            {
                button.Focus();
                return;
            }
        }
    }

    private static string Describe(FileOperationSnapshot operation, bool withCounts)
    {
        if (operation.Phase == FileOperationPhase.Cancelling)
        {
            return "Cancelling…";
        }

        if (operation.Phase == FileOperationPhase.Publishing)
        {
            return "Finishing…";
        }

        if (operation.Progress is not { } progress)
        {
            return operation.Title;
        }

        var stage = progress.Stage switch
        {
            FileWorkStage.Copying => "Copying file",
            FileWorkStage.WritingRows => "Writing CSV",
            FileWorkStage.Verifying => "Verifying session",
            FileWorkStage.CreatingArchive => "Creating archive",
            FileWorkStage.ExtractingArchive => "Extracting archive",
            FileWorkStage.SavingToProvider => "Saving to chosen location",
            FileWorkStage.Publishing => "Finishing",
            _ => operation.Title.TrimEnd('…'),
        };
        if (!withCounts || progress.Total is not > 0)
        {
            return $"{stage}…";
        }

        return progress.Unit == "bytes"
            ? $"{stage} · {FormatBytes(progress.Completed)} of {FormatBytes(progress.Total.Value)}"
            : $"{stage} · {progress.Completed:N0} of {progress.Total.Value:N0} {progress.Unit}";
    }

    private static string FormatBytes(long value)
    {
        const double megabyte = 1024d * 1024d;
        return value >= megabyte
            ? $"{value / megabyte:N1} MB"
            : $"{Math.Max(0, value) / 1024d:N1} KB";
    }

    private void ApplyFileOperationTheme()
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        _fileOperationBand.Background = new SolidColorBrush(Timeline.WorkspacePalette.SurfaceRaised(dark));
        _fileOperationBand.BorderBrush = new SolidColorBrush(Timeline.WorkspacePalette.BorderLine(dark));
        _fileOperationText.Foreground = new SolidColorBrush(Timeline.WorkspacePalette.TextPrimary(dark));
    }
}
