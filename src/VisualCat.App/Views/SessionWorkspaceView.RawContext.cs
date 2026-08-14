using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

public sealed partial class SessionWorkspaceView : UserControl
{
    /// <summary>
    /// The source inspector: a labelled, collapsible panel rather than the old full-height
    /// read-only text box. Collapsed it costs one header row, so an unused inspector no
    /// longer holds a screenful of empty space under the table; selecting a row opens it,
    /// and it sizes to its content up to a modest cap (§14.1 density, §14.7).
    /// </summary>
    private Control BuildRawContextPane()
    {
        _rawContext.FontFamily = MonoFont;
        _rawContext.FontSize = 12;
        var scroller = _rawScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = _mobile ? double.PositiveInfinity : 150,
            Content = _rawContext,
        };
        _rawPlaceholder = new TextBlock
        {
            Text = "Select a row to load the exact source bytes behind it, with a few lines on each side.",
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new Grid();
        if (_mobile)
        {
            var panToggle = _rawPanToggle = new Button
            {
                MinHeight = 48,
                Width = 76,
                Padding = new Thickness(6, 0),
            };
            panToggle.Click += (_, _) => SetRawPanMode(!_rawPanMode);
            ToolTip.SetTip(panToggle, "Select mode: drag selects text. Pan mode: drag scrolls in both directions.");

            var wrapToggle = _rawWrapToggle = new Button
            {
                Content = "Wrap",
                MinHeight = 48,
                Width = 64,
                Padding = new Thickness(6, 0),
            };
            wrapToggle.Click += (_, _) =>
            {
                _rawWrapPreferenceSet = true;
                SetRawWrap(!_rawWrapEnabled);
            };
            ToolTip.SetTip(wrapToggle, "Wrap long source lines to the available width.");
            AutomationProperties.SetName(wrapToggle, "Wrap long source lines");

            var panLeft = _rawPanLeft = new Button
            {
                Content = "←",
                MinHeight = 48,
                Width = 48,
            };
            panLeft.Click += (_, _) => PanRawContext(-1);
            ToolTip.SetTip(panLeft, "Pan source left by one page");
            AutomationProperties.SetName(panLeft, "Pan source left by one page");

            var panRight = _rawPanRight = new Button
            {
                Content = "→",
                MinHeight = 48,
                Width = 48,
            };
            panRight.Click += (_, _) => PanRawContext(1);
            ToolTip.SetTip(panRight, "Pan source right by one page");
            AutomationProperties.SetName(panRight, "Pan source right by one page");

            var copySelection = _rawCopySelection = new Button
            {
                Content = "Copy",
                MinHeight = 48,
                Width = 64,
                Padding = new Thickness(6, 0),
                IsEnabled = false,
            };
            copySelection.Click += (_, _) => _rawContext.Copy();
            ToolTip.SetTip(copySelection, "Copy selected source text");
            AutomationProperties.SetName(copySelection, "Copy selected source text");

            var sourceTools = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 4,
                LineSpacing = 6,
                Margin = new Thickness(0, 0, 0, 7),
                Children =
                {
                    panToggle,
                    wrapToggle,
                    panLeft,
                    panRight,
                    copySelection,
                },
            };
            AutomationProperties.SetName(sourceTools, "Source navigation and selection controls");

            _rawSelectionHint = new TextBlock
            {
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7),
            };
            var codeGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                Children =
                {
                    _rawSelectionHint,
                    sourceTools,
                    scroller,
                },
            };
            Grid.SetRow(sourceTools, 1);
            Grid.SetRow(scroller, 2);
            scroller.ScrollChanged += (_, _) => UpdateRawNavigationButtons();
            _rawContext.PointerReleased += (_, _) => Dispatcher.UIThread.Post(CompleteRawTextSelection);
            SetRawPanMode(false);
            SetRawWrap(false);
            _rawCodeSurface = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = codeGrid,
                IsVisible = false,
            };
            _rawDataSurface = _rawCodeSurface;
            content.Children.Add(_rawCodeSurface);

            var chooseEntry = _rawChooseEntry = new Button
            {
                Content = "Choose an entry",
                HorizontalAlignment = HorizontalAlignment.Center,
                MinHeight = 48,
            };
            chooseEntry.Click += (_, _) =>
            {
                if (_mobileAnalysisTabs is { } tabs)
                {
                    tabs.SelectedIndex = 0;
                }
            };
            AutomationProperties.SetName(chooseEntry, "Open Entries to choose a source row");

            _rawPlaceholder.Text =
                "Choose a log entry to inspect its exact source bytes and the surrounding lines.";
            _rawPlaceholder.FontStyle = FontStyle.Normal;
            _rawPlaceholder.TextAlignment = TextAlignment.Center;
            _rawPlaceholder.MaxWidth = 320;
            _rawEmptyTitle = new TextBlock
            {
                Text = "No source selected",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var emptyPanel = new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "{  }",
                        FontFamily = MonoFont,
                        FontSize = 24,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    _rawEmptyTitle,
                    _rawPlaceholder,
                    chooseEntry,
                },
            };
            _rawEmptyCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18),
                Margin = new Thickness(14),
                MaxWidth = 390,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = emptyPanel,
            };
            _rawEmptyState = _rawEmptyCard;
            AutomationProperties.SetName(_rawEmptyCard, "No source selected");
            content.Children.Add(_rawEmptyCard);
        }
        else
        {
            _rawDataSurface = scroller;
            _rawEmptyState = _rawPlaceholder;
            content.Children.Add(scroller);
            content.Children.Add(_rawPlaceholder);
        }
        _rawContentBorder = new Border
        {
            BorderThickness = new Thickness(_mobile ? 0 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = _mobile ? new Thickness(0) : new Thickness(8, 6),
            Child = content,
        };

        if (_mobile)
        {
            // Its own tab, so it is always open and fills the available height.
            _rawContentBorder.Margin = new Thickness(6);
            _rawExpanded = true;
            return _rawContentBorder;
        }

        _rawContentBorder.Margin = new Thickness(0, 4, 0, 0);
        _rawChevron = new TextBlock { Text = "▸", Width = 14, VerticalAlignment = VerticalAlignment.Center };
        _rawHeaderLabel = new TextBlock
        {
            Text = "SOURCE CONTEXT",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rawHeaderHint = new TextBlock
        {
            Text = "raw bytes behind the selected row",
            FontSize = 10,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _rawChevron, _rawHeaderLabel, _rawHeaderHint },
            },
        };
        ToolTip.SetTip(header, "Show or hide the exact source bytes behind the selected row.");
        AutomationProperties.SetName(header, "Toggle source context");
        header.Click += (_, _) => SetRawExpanded(!_rawExpanded);

        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        pane.Children.Add(header);
        Grid.SetRow(_rawContentBorder, 1);
        pane.Children.Add(_rawContentBorder);
        SetRawExpanded(false);
        return pane;
    }

    private void SetRawExpanded(bool expanded)
    {
        _rawExpanded = expanded;
        if (_rawContentBorder is { } border)
        {
            border.IsVisible = expanded;
        }

        if (_rawChevron is { } chevron)
        {
            chevron.Text = expanded ? "▾" : "▸";
        }
    }


    private void BeginRawContextLoad(NormalizedEntry entry, long? timelineCount)
    {
        _timelineEntryPending = false;
        _rawLoadEntry = entry;
        _rawLoadTimelineCount = timelineCount;
        _rawLoadInterrupted = false;
        var cancellation = new CancellationTokenSource();
        lock (_rawLoadSync)
        {
            _rawLoadCancellation?.Cancel();
            _rawLoadCancellation = cancellation;
        }

        ShowRawLoadingState(timelineCount);
        _ = LoadRawContextForSelectionAsync(entry, cancellation);
    }

    private async Task LoadRawContextForSelectionAsync(
        NormalizedEntry entry,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _viewModel.LoadRawContextAsync(entry, cancellationToken: cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_rawLoadSync)
                {
                    if (ReferenceEquals(_rawLoadCancellation, cancellation))
                    {
                        _rawLoadInterrupted = false;
                        PresentRawContext();
                    }
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_rawLoadSync)
                {
                    if (ReferenceEquals(_rawLoadCancellation, cancellation))
                    {
                        ShowRawErrorState(exception);
                    }
                }
            });
        }
        finally
        {
            lock (_rawLoadSync)
            {
                if (ReferenceEquals(_rawLoadCancellation, cancellation))
                {
                    _rawLoadCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelRawContextLoad(bool resumeOnAttach = false)
    {
        var interrupted = false;
        lock (_rawLoadSync)
        {
            if (_rawLoadCancellation is { } active)
            {
                active.Cancel();
                interrupted = true;
            }

            _rawLoadCancellation = null;
        }

        _rawLoadInterrupted = resumeOnAttach && interrupted;
        if (!resumeOnAttach)
        {
            _rawLoadEntry = null;
            _rawLoadTimelineCount = null;
        }
    }

    private void ResumeInterruptedRawContextLoad()
    {
        if (_rawLoadInterrupted && _rawLoadEntry is { } entry && !HasRawContextLoad())
        {
            BeginRawContextLoad(entry, _rawLoadTimelineCount);
        }
    }

    private bool HasRawContextLoad()
    {
        lock (_rawLoadSync)
        {
            return _rawLoadCancellation is not null;
        }
    }

    private void CompleteRawTextSelection()
    {
        var hasSelection = !string.IsNullOrEmpty(_rawContext.SelectedText);
        if (_rawCopySelection is { } copySelection)
        {
            copySelection.IsEnabled = hasSelection;
        }

        // A completed touch selection automatically releases the text surface. The next
        // drag therefore pans the ScrollViewer instead of extending a hidden selection.
        if (hasSelection)
        {
            SetRawPanMode(true);
        }
    }

    private void SetRawPanMode(bool pan)
    {
        _rawPanMode = pan;
        _rawContext.IsHitTestVisible = !pan;
        if (_rawPanToggle is not { } panToggle)
        {
            return;
        }

        panToggle.Content = pan ? "Pan" : "Select";
        ApplyMobileChoiceAppearance(panToggle, pan);
        AutomationProperties.SetName(
            panToggle,
            pan ? "Pan mode; tap to select text" : "Select mode; tap to pan source");
    }

    private void SetRawWrap(bool wrap)
    {
        _rawWrapEnabled = wrap;
        _rawContext.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (_rawScroller is { } scroller)
        {
            scroller.HorizontalScrollBarVisibility = wrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;
            if (wrap)
            {
                scroller.Offset = new Vector(0, scroller.Offset.Y);
            }
        }

        if (_rawWrapToggle is { } wrapToggle)
        {
            wrapToggle.Content = wrap ? "Wrap ✓" : "Wrap";
            ApplyMobileChoiceAppearance(wrapToggle, wrap);
            AutomationProperties.SetName(
                wrapToggle,
                wrap ? "Line wrapping on; tap to show full lines" : "Line wrapping off; tap to wrap long lines");
        }

        UpdateRawNavigationButtons();
    }

    private void PanRawContext(int direction)
    {
        if (_rawScroller is not { } scroller || _rawWrapEnabled)
        {
            return;
        }

        var maximum = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        var page = Math.Max(96, scroller.Viewport.Width * 0.8);
        var next = Math.Clamp(scroller.Offset.X + (direction * page), 0, maximum);
        scroller.Offset = new Vector(next, scroller.Offset.Y);
        UpdateRawNavigationButtons();
    }

    private void UpdateRawNavigationButtons()
    {
        if (_rawScroller is not { } scroller)
        {
            return;
        }

        var wrapped = _rawWrapEnabled;
        var maximum = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        if (_rawPanLeft is { } panLeft)
        {
            panLeft.IsVisible = !wrapped;
            panLeft.IsEnabled = !wrapped && scroller.Offset.X > 0.5;
        }

        if (_rawPanRight is { } panRight)
        {
            panRight.IsVisible = !wrapped;
            panRight.IsEnabled = !wrapped && scroller.Offset.X < maximum - 0.5;
        }
    }

    private void PresentRawContext()
    {
        var raw = _viewModel.RawContextText;
        if (!string.Equals(_rawContext.Text, raw, StringComparison.Ordinal))
        {
            _rawContext.ClearSelection();
            _rawContext.Text = raw;
            if (_rawCopySelection is { } copySelection)
            {
                copySelection.IsEnabled = false;
            }
        }

        var hasRaw = !string.IsNullOrEmpty(raw);
        if (!hasRaw)
        {
            ShowRawUnavailableState();
            return;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = !hasRaw;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = hasRaw;
        }

        // Selecting a row is the request to read its source, so the panel opens
        // itself the moment content arrives; the user can still collapse it.
        if (hasRaw && !_mobile && !_rawExpanded)
        {
            SetRawExpanded(true);
        }
    }

    private void ShowRawUnavailableState()
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "Source unavailable";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = "No source context was returned for this entry. Choose it again to retry.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source context unavailable");
        }
    }

    private void ShowRawLoadingState(long? timelineCount = null)
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = timelineCount is > 0 ? "Loading first entry…" : "Loading source…";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = timelineCount is > 0
                ? $"Reading the first of {timelineCount:N0} entries in the selected timeline bar."
                : "Reading the selected entry's exact bytes and surrounding lines.";
        }

        if (_rawSelectionHint is { } selectionHint)
        {
            selectionHint.Text = timelineCount is > 0
                ? $"First of {timelineCount:N0} in selected bar · choose another row in Entries"
                : "Selected entry · exact source bytes with surrounding lines";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = false;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(
                emptyCard,
                timelineCount is > 0
                    ? "Loading first entry from selected timeline bar"
                    : "Loading source context");
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }
    }

    private void ShowRawNoMatchesState()
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "No matching entries";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = "This timeline bar has no entries after the current filters.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "No matching entries in selected timeline bar");
        }
    }

    private void ShowRawErrorState(Exception exception)
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "Source unavailable";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = "VisualCat could not read this entry's source context. Choose another entry to retry.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source context unavailable");
        }

        _status.Text = $"Source unavailable · {exception.Message}";
    }


}
