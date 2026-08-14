using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    // The message marker deliberately reuses the timeline's search-tick color, so a hit in
    // the plot and a hit in the text read as one channel rather than two unrelated marks.
    // Its own foreground is set with it: a mark that inherited the theme foreground would
    // be unreadable in one of the two themes.
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush SearchHighlightFill =
        new(Color.Parse("#FF3FE0"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush SearchHighlightText =
        new(Color.Parse("#150411"));

    private void ConfigureEntryList()
    {
        _entries.SelectionMode = SelectionMode.Multiple;

        // A log table earns its keep by rows on screen: monospace for scanability and
        // compact item containers instead of Fluent's touch-sized defaults.
        _entries.FontFamily = MonoFont;
        _entries.FontSize = _mobile ? 12 : 12.5;
        if (!_mobile)
        {
            // The column header is a separate grid drawn flush to the list's left edge, so
            // the rows must sit flush too — a list or item inset slides every value out
            // from under its label.
            _entries.Padding = new Thickness(0);
            _entries.Styles.Add(CompactItemStyle());
        }
        else
        {
            _entries.Styles.Add(CompactItemStyle(64));
        }

        ApplyEntryTemplate();
        AutomationProperties.SetName(_entries, "Filtered log entries");
    }

    /// <summary>
    /// Installs the row template. A row reads mutable state when it is realized — the
    /// active search term and the current theme — and the search re-queries the entry
    /// list, so search changes re-realize rows on their own. A theme change does not, so
    /// it reassigns the template here, which rebuilds every container exactly once.
    /// </summary>
    private void ApplyEntryTemplate() =>
        _entries.ItemTemplate = _mobile ? BuildMobileEntryTemplate() : BuildDesktopEntryTemplate();

    private FuncDataTemplate<NormalizedEntry> BuildDesktopEntryTemplate()
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        return new FuncDataTemplate<NormalizedEntry>((entry, _) =>
        {
            if (entry is null)
            {
                return new Grid();
            }

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(EntryColumns),
                // Notable severities only: tinting every row would decorate the table
                // instead of ranking it, and the tint has to stay faint enough that the
                // selection highlight underneath still reads (§14.1 density).
                Background = RowTint(entry.Level, dark),
                Children =
                {
                    // The edge sits inside the 4px gutter the time cell's margin already
                    // leaves, so the severity ribbon costs no width and every value stays
                    // under its column header.
                    SeverityEdge(entry.Level),
                    Cell(FormatInstant(entry.Timestamp), 0),
                    Cell(entry.Level.ToLetter().ToString(), 1, LevelPalette.BrushOf(entry.Level)),
                    Cell(ProcessLabel(entry), 2),
                    Cell(entry.Tid.ToString(System.Globalization.CultureInfo.InvariantCulture), 3),
                    Cell(entry.Buffer, 4),
                    Cell(entry.Tag, 5),
                    Cell(entry.TemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture), 6),
                    MessageCell(entry, 7),
                },
            };
            return row;
        });
    }

    private FuncDataTemplate<NormalizedEntry> BuildMobileEntryTemplate()
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var separator = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        var muted = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        return new FuncDataTemplate<NormalizedEntry>((entry, _) =>
        {
            if (entry is null)
            {
                return new Border();
            }

            var body = new StackPanel
            {
                Margin = new Thickness(7, 5),
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{entry.Level.ToLetter()}  {entry.Tag}",
                        FontWeight = FontWeight.Bold,
                        Foreground = LevelPalette.BrushOf(entry.Level),
                    },
                    HighlightedText(
                        FirstLine(entry.Message),
                        _viewModel.Filter.Search,
                        static text => new TextBlock
                        {
                            Text = text,
                            TextWrapping = TextWrapping.Wrap,
                            MaxLines = 1,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        }),
                    new TextBlock
                    {
                        Text = $"{FormatInstant(entry.Timestamp)}  ·  {ProcessLabel(entry)}:{entry.Tid}  ·  {entry.Buffer}",
                        FontSize = 10,
                        Foreground = muted,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
            Grid.SetColumn(body, 1);

            var edge = SeverityEdge(entry.Level);
            Grid.SetColumn(edge, 0);
            return new Border
            {
                BorderBrush = separator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = RowTint(entry.Level, dark),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("3,*"),
                    Children = { edge, body },
                },
            };
        });
    }

    /// <summary>
    /// The plot teaches a color language; the table used to drop it, leaving an error row
    /// and a verbose row identical apart from one character. The letter column stays, so
    /// color is never the only carrier (ADR 0013).
    /// </summary>
    private static Border SeverityEdge(LogLevel level)
    {
        var edge = new Border
        {
            Background = LevelPalette.BrushOf(level),
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1),
        };
        Grid.SetColumn(edge, 0);
        return edge;
    }

    private static Avalonia.Media.Immutable.ImmutableSolidColorBrush? RowTint(LogLevel level, bool dark) =>
        level is LogLevel.Warn or LogLevel.Error or LogLevel.Fatal
            ? LevelPalette.Fill(level, dark ? (byte)14 : (byte)26)
            : null;

    private TextBlock MessageCell(NormalizedEntry entry, int column)
    {
        var cell = HighlightedText(
            FirstLine(entry.Message),
            _viewModel.Filter.Search,
            static text => new TextBlock
            {
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 2),
            });
        Grid.SetColumn(cell, column);
        return cell;
    }

    /// <summary>
    /// Builds a message block, marking the active search term inside it. Unmarked text —
    /// the common case, and the only case while no search is active — keeps the plain
    /// single-string block and never touches the inline machinery.
    /// </summary>
    private static TextBlock HighlightedText(
        string text,
        TextSearchSpec? search,
        Func<string, TextBlock> build)
    {
        var spans = EntryHighlight.Match(text, search);
        if (spans.Count == 0)
        {
            return build(text);
        }

        var block = build(string.Empty);
        var inlines = new InlineCollection();
        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start > cursor)
            {
                inlines.Add(new Run(text[cursor..span.Start]));
            }

            inlines.Add(new Run(text.Substring(span.Start, span.Length))
            {
                Background = SearchHighlightFill,
                Foreground = SearchHighlightText,
                FontWeight = FontWeight.Bold,
            });
            cursor = span.Start + span.Length;
        }

        if (cursor < text.Length)
        {
            inlines.Add(new Run(text[cursor..]));
        }

        block.Inlines = inlines;
        return block;
    }

    /// <summary>The one rendered line of a possibly multi-line message, without the array
    /// a per-row <c>Split</c> would allocate.</summary>
    private static string FirstLine(string message)
    {
        var end = message.AsSpan().IndexOfAny('\r', '\n');
        return end < 0 ? message : message[..end];
    }

    private static Grid EntryColumnHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(EntryColumns),
            Background = new SolidColorBrush(Color.Parse("#111C2D")),
        };
        foreach (var (text, column) in new[]
                 {
                     ("TIME", 0),
                     ("L", 1),
                     ("PROCESS / PID", 2),
                     ("TID", 3),
                     ("BUFFER", 4),
                     ("TAG", 5),
                     ("TPL", 6),
                     ("MESSAGE", 7),
                 })
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#8FA5C4")),
                Margin = new Thickness(4, 5),
            };
            Grid.SetColumn(label, column);
            header.Children.Add(label);
        }

        return header;
    }

    private static Avalonia.Styling.Style CompactItemStyle(double minimumHeight = 22)
    {
        var style = new Avalonia.Styling.Style(static selector => Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector));
        // No horizontal padding: the per-cell 4px margin is the only inset, so a row lines
        // up with the column header, which carries the same margin. Vertical 1px keeps rows
        // tight (§14.1 density) without disturbing the column geometry.
        style.Setters.Add(new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(0, 1)));
        style.Setters.Add(new Avalonia.Styling.Setter(Layoutable.MinHeightProperty, minimumHeight));
        return style;
    }

    private TimeZoneInfo ResolveSessionZone()
    {
        var zoneId = _viewModel.Snapshot?.Descriptor.TimestampPolicy.TimeZoneId;
        if (zoneId is null)
        {
            return TimeZoneInfo.Utc;
        }

        if (_sessionZone is { } cached && string.Equals(_sessionZoneId, zoneId, StringComparison.Ordinal))
        {
            return cached;
        }

        try
        {
            _sessionZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _sessionZone = TimeZoneInfo.Utc;
        }

        _sessionZoneId = zoneId;
        return _sessionZone;
    }

    /// <summary>Session-zone "MM-dd HH:mm:ss.ffffff" — the ISO round-trip form with its
    /// offset suffix overflowed every column it appeared in. Full precision lives in the
    /// raw context and the session pane.</summary>
    private string FormatInstant(InstantUs? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value.ToDateTimeOffset(), ResolveSessionZone())
                .ToString("MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)
            : "untimed";

    private string ProcessLabel(NormalizedEntry entry)
    {
        var name = entry.Timestamp is { } instant
            ? _viewModel.Snapshot?.ResolveProcessName(entry.Pid, instant)
            : null;
        return name is null
            ? entry.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{name} ({entry.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    private void WireInteractions()
    {
        // Moving the view releases any cell scope (SetViewportAsync), which in turn
        // clears the outline through the DetailRange notification below (§14.7).
        _timeline.ViewportChanged += (_, range) => _ = _viewModel.SetViewportAsync(range);
        _timeline.CellSelected += (_, cell) => _ = SelectTimelineCellAsync(cell);
        _timeline.HoverChanged += (_, cell) => _viewModel.RequestCellPattern(cell?.Range, cell?.Level);
        _timeline.RangeSelected += (_, range) =>
        {
            _selectedRange = range;
            _rangeText.Text = $"{FormatInstant(range.StartInclusive)} — {FormatInstant(range.EndExclusive)}";
            _rangeActions.IsVisible = true;
            UpdateChipBarVisibility();
            _ = _viewModel.RefreshCellAsync(range, null);
        };
        _timeline.FollowRequested += (_, _) => _ = _viewModel.ToggleFollowAsync();
        _timeline.SearchFocusRequested += (_, _) => _search.Focus();
        _timeline.ExportRequested += (_, _) => ExportRequested?.Invoke(null);
        _timeline.EntryNavigationRequested += (_, delta) => MoveEntrySelection(delta);
        _timeline.SizeChanged += (_, eventArgs) =>
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            _ = _viewModel.SetRenderWidthAsync(
                Math.Max(64, (int)Math.Round((eventArgs.NewSize.Width - 88) * scale)));
        };
        _minimap.ViewportChanged += (_, range) => _ = _viewModel.SetViewportAsync(range);
        _entries.SelectionChanged += (_, _) =>
        {
            if (_copyRaw is { } copyRaw)
            {
                copyRaw.IsEnabled = _entries.SelectedItems?.Count > 0 || _entries.SelectedItem is NormalizedEntry;
            }

            if (_entries.SelectedItem is NormalizedEntry entry)
            {
                // Reading the table no longer costs the user their place in the plot
                // (§14.9): the plot marks where this row sits, and says which way to look
                // when the row is outside the current viewport.
                _timeline.SetSelectedEntry(entry.Timestamp, entry.Level);
                var timelineCount = _selectingTimelineEntry ? _selectedTimelineCellCount : (long?)null;
                _selectingTimelineEntry = false;
                BeginRawContextLoad(entry, timelineCount);
            }
            else
            {
                _timeline.SetSelectedEntry(null, null);
            }
        };

        _viewModel.PropertyChanged += (_, eventArgs) => Dispatcher.UIThread.Post(() =>
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(SessionTabViewModel.HeatMap):
                case nameof(SessionTabViewModel.Overview):
                case nameof(SessionTabViewModel.Viewport):
                    UpdateTimelines();
                    break;
                case nameof(SessionTabViewModel.SearchText):
                    _search.Text = _viewModel.SearchText;
                    break;
                case nameof(SessionTabViewModel.EntryOrder):
                    _order.SelectedIndex = _viewModel.EntryOrder == EntryOrder.SourceSequence ? 1 : 0;
                    break;
                case nameof(SessionTabViewModel.SearchResult):
                    _timeline.SetSearchResult(_viewModel.SearchResult);
                    break;
                case nameof(SessionTabViewModel.Status):
                    _status.Text = _viewModel.Status;
                    UpdateCaptureActions();
                    break;
                case nameof(SessionTabViewModel.SearchStatus):
                    _searchStatus.Text = _viewModel.SearchStatus;
                    break;
                case nameof(SessionTabViewModel.DetailRange):
                    // The outline and the detail scope are one state: whoever releases
                    // the scope releases the outline with it, so the plot can never claim
                    // a selection the table is not listing.
                    if (_viewModel.DetailRange is null)
                    {
                        _timeline.ClearSelection();
                    }

                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.Statistics):
                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.Filter):
                    UpdateTimelineLevels();
                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.MatchesInView):
                    UpdateStatistics();
                    UpdateEntryLoadControls();
                    break;
                case nameof(SessionTabViewModel.HoverPattern):
                    _timeline.SetHoverInsight(_viewModel.HoverPattern is { } pattern
                        ? new TimelineHoverInsight(
                            pattern.Range,
                            pattern.Level,
                            pattern.TemplateText,
                            pattern.TemplateCount)
                        : null);
                    break;
                case nameof(SessionTabViewModel.RawContextText):
                    // Property changes are marshalled to this queue. A canceled source
                    // read can therefore leave a stale notification behind even after a
                    // newer timeline tap. The current request presents itself explicitly.
                    if (!_timelineEntryPending && !HasRawContextLoad())
                    {
                        PresentRawContext();
                    }

                    break;
                case nameof(SessionTabViewModel.CanLoadMore):
                case nameof(SessionTabViewModel.IsLoadingEntries):
                case nameof(SessionTabViewModel.LoadedEntryCount):
                case nameof(SessionTabViewModel.RemainingEntryCount):
                    UpdateEntryLoadControls();
                    break;
                case nameof(SessionTabViewModel.FollowLatest):
                    UpdateFollowButton();
                    break;
                case nameof(SessionTabViewModel.HasNewData):
                    UpdateCaptureActions();
                    break;
                case nameof(SessionTabViewModel.IsLiveCaptureActive):
                    UpdateCaptureActions();
                    break;
            }
        });
        _viewModel.SnapshotChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTimelines);
    }

    private async Task SelectTimelineCellAsync(TimelineCellSelection cell)
    {
        var generation = Interlocked.Increment(ref _timelineSelectionGeneration);
        _timelineEntryPending = true;
        CancelRawContextLoad();
        ShowRawLoadingState(cell.Count);

        try
        {
            await _viewModel.RefreshCellAsync(cell.Range, cell.Level);
        }
        catch (OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _timelineSelectionGeneration))
            {
                _timelineEntryPending = false;
            }

            return;
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == Volatile.Read(ref _timelineSelectionGeneration))
                {
                    _timelineEntryPending = false;
                    ShowRawErrorState(exception);
                }
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _timelineSelectionGeneration))
            {
                return;
            }

            if (_viewModel.Entries.Count == 0)
            {
                _timelineEntryPending = false;
                ShowRawNoMatchesState();
                return;
            }

            _selectingTimelineEntry = true;
            _selectedTimelineCellCount = _viewModel.MatchesInView ?? cell.Count;
            _entries.SelectedIndex = 0;
        });
    }


    private async Task ApplySearchAsync()
    {
        _viewModel.SearchText = _search.Text ?? string.Empty;
        await _viewModel.ApplySearchAsync(_regex.IsChecked == true, _caseSensitive.IsChecked == true);
    }

    private void QueueDebouncedSearch()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchDebounce, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        _ = DebounceAsync(cancellation);

        async Task DebounceAsync(CancellationTokenSource source)
        {
            try
            {
                await Task.Delay(320, source.Token).ConfigureAwait(false);
                if (!source.IsCancellationRequested)
                {
                    Dispatcher.UIThread.Post(() => _ = RunUiActionAsync(ApplySearchAsync));
                }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled";
        }
        catch (Exception exception)
        {
            _status.Text = $"Failed · {exception.GetBaseException().Message}";
        }
    }

    private async Task ToggleLoadAllEntriesAsync()
    {
        if (_loadAllEntriesCancellation is { } active)
        {
            active.Cancel();
            UpdateEntryLoadControls();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadAllEntriesCancellation = cancellation;
        UpdateEntryLoadControls();
        try
        {
            await _viewModel.LoadAllEntriesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is an ordinary user action: the already loaded rows remain
            // useful and the exact remainder stays visible beside the controls.
        }
        catch (Exception exception)
        {
            _status.Text = $"Failed · {exception.GetBaseException().Message}";
        }
        finally
        {
            if (ReferenceEquals(_loadAllEntriesCancellation, cancellation))
            {
                _loadAllEntriesCancellation = null;
            }

            cancellation.Dispose();
            UpdateEntryLoadControls();
        }
    }

    private void UpdateEntryLoadControls()
    {
        var loaded = _viewModel.LoadedEntryCount;
        var total = _viewModel.MatchesInView;
        var remaining = _viewModel.RemainingEntryCount;
        var loading = _viewModel.IsLoadingEntries;
        var loadingAll = _loadAllEntriesCancellation is { IsCancellationRequested: false };
        var stopping = _loadAllEntriesCancellation is { IsCancellationRequested: true };

        _loadMore.IsEnabled = _viewModel.CanLoadMore && !loading && _loadAllEntriesCancellation is null;
        _loadMore.IsVisible = !_mobile || _viewModel.CanLoadMore;
        if (!_mobile)
        {
            _entryLoadStatus.Text = total is { } count
                ? loading
                    ? $"{loaded:N0} / {count:N0} rows · loading…"
                    : loaded >= count
                        ? $"All {count:N0} rows loaded"
                        : $"{loaded:N0} / {count:N0} rows loaded"
                : $"{loaded:N0} rows loaded";
            var loadDescription = total is { } knownTotal
                ? $"{loaded:N0} of {knownTotal:N0} matching rows loaded; {remaining:N0} remaining"
                : $"{loaded:N0} matching rows loaded";
            ToolTip.SetTip(_entryLoadStatus, loadDescription);
            AutomationProperties.SetName(_entryLoadStatus, loadDescription);

            _loadAll.Content = stopping ? "Stopping…" : loadingAll ? "Cancel" : "Load all";
            _loadAll.IsEnabled = stopping ? false : loadingAll || _viewModel.CanLoadMore && !loading;
            var loadAllDescription = stopping
                ? "Stopping the all-rows load"
                : loadingAll
                    ? $"Cancel loading all rows; {remaining:N0} remain"
                    : remaining > 0
                        ? $"Load all {remaining:N0} remaining matching rows in batches"
                        : "All matching rows are loaded";
            ToolTip.SetTip(_loadAll, loadAllDescription);
            AutomationProperties.SetName(_loadAll, loadAllDescription);
        }
    }


    private void MoveEntrySelection(int delta)
    {
        if (_entries.ItemCount == 0)
        {
            return;
        }

        _entries.SelectedIndex = Math.Clamp(
            (_entries.SelectedIndex < 0 ? 0 : _entries.SelectedIndex) + delta,
            0,
            _entries.ItemCount - 1);
        _entries.ScrollIntoView(_entries.SelectedIndex);
    }

    private async Task CopySelectedRawAsync()
    {
        var selected = _entries.SelectedItems?.OfType<NormalizedEntry>().ToArray() ?? [];
        if (selected.Length == 0 && _entries.SelectedItem is NormalizedEntry entry)
        {
            selected = [entry];
        }

        if (selected.Length == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await _viewModel.ReadRawEntriesAsync(selected);
        await clipboard.SetTextAsync(text);
    }

}
