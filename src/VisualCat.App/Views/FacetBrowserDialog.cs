using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Queries;

namespace VisualCat.App.Views;

/// <summary>Searches and edits the complete value set for one facet dimension.</summary>
internal sealed class FacetBrowserDialog : DialogBody<bool>, IDisposable
{
    private const int PageSize = 100;

    /// <summary>Below this row width the two actions move under the value they act on.</summary>
    private const double StackedRowWidth = 260;
    private readonly SessionTabViewModel _tab;
    private readonly FacetDimension _dimension;
    private readonly FacetQueryDimension _queryDimension;
    private readonly string _singular;
    private readonly string _plural;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _lookupSerial = new(1, 1);
    private readonly object _backgroundGate = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private readonly TextBox _search = new();
    private readonly TextBlock _scope = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _asOf = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.IndianRed,
        IsVisible = false,
    };
    private readonly TextBlock _activeHeading = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBox _selectedDetail = new()
    {
        TextWrapping = TextWrapping.Wrap,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
    };
    private readonly Button _selectedInclude = new() { Content = "Include selected", IsEnabled = false };
    private readonly Button _selectedExclude = new() { Content = "Exclude selected", IsEnabled = false };
    private readonly ListBox _active = new() { MaxHeight = 180 };
    private readonly ListBox _neutral = new();
    private readonly TextBlock _range = new() { VerticalAlignment = VerticalAlignment.Center };
    private const string RangeLabelName = "Available values on this page";
    private readonly Button _previous = new() { Content = "Previous 100" };
    private readonly Button _next = new() { Content = "Next 100" };
    private readonly Button _refresh = new() { Content = "Refresh counts", IsVisible = false };
    private readonly Button _firstPage = new() { Content = "First page", IsVisible = false };
    private readonly Button _clearListSearch = new() { Content = "Clear list search", IsVisible = false };
    private bool _historyTruncated;
    private readonly Stack<FacetPageCursor?> _history = new();
    private CancellationTokenSource? _lookupCancellation;
    private Task? _initializeTask;
    private Task? _lookupTask;
    private Task? _refreshTask;
    private Task? _releaseTask;
    private IDisposable? _lease;
    private SessionSnapshot? _snapshot;
    private FacetPageCursor? _cursor;
    private FacetPageCursor? _nextCursor;
    private long _pageOffset;
    private int _generation;
    private bool _released;
    private bool _refreshing;
    private bool _newerSnapshotAvailable;

    internal FacetBrowserDialog(SessionTabViewModel tab, FacetDimension dimension)
        : base(TitleFor(dimension))
    {
        _tab = tab ?? throw new ArgumentNullException(nameof(tab));
        _dimension = dimension;
        (_queryDimension, _singular, _plural) = dimension switch
        {
            FacetDimension.Tag => (FacetQueryDimension.Tag, "tag", "tags"),
            FacetDimension.Process => (FacetQueryDimension.Process, "process", "processes"),
            FacetDimension.Pid => (FacetQueryDimension.Pid, "PID", "PIDs"),
            FacetDimension.Tid => (FacetQueryDimension.Tid, "thread", "threads"),
            FacetDimension.Buffer => (FacetQueryDimension.Buffer, "buffer", "buffers"),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), "Templates use their active-values group rather than a browser."),
        };

        var mobile = OperatingSystem.IsAndroid();
        PreferredSize = new Size(680, 680);
        MinimumSize = mobile ? new Size(300, 320) : new Size(400, 360);
        ScrollsInternally = true;
        _search.PlaceholderText = dimension is FacetDimension.Pid or FacetDimension.Tid
            ? "Filter this list by number"
            : "Filter this list by name";
        AutomationProperties.SetName(_search, $"Filter {_plural} list");
        AutomationProperties.SetHelpText(
            _search,
            "Literal, case-insensitive text. This does not change the message search or the workspace filter.");
        _search.TextChanged += (_, _) => QueueLookup(resetPage: true);
        _search.KeyDown += (_, args) =>
        {
            // Enter is "take me to the results", not "apply a filter": the list is already
            // current, and the reader still has to choose include or exclude for a value.
            if (args.Key != Avalonia.Input.Key.Enter)
            {
                return;
            }

            args.Handled = true;
            if (_neutral.ItemCount > 0)
            {
                _neutral.SelectedIndex = 0;
                _neutral.Focus();
                return;
            }

            _hint.Focus();
        };

        _active.ItemTemplate = RowTemplate();
        _neutral.ItemTemplate = RowTemplate();

        // The row already sets its own touch height; the container's default padding was
        // adding half a row again on top of it, so a 480 dp phone showed two and a half
        // values where it has room for five.
        foreach (var list in new[] { _active, _neutral })
        {
            // One tab stop enters a virtualized list; arrows choose a row and its realised
            // actions remain reachable within that stop. Tab then continues to paging and
            // Done instead of visiting two buttons for every one of up to 100 rows.
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(
                list,
                Avalonia.Input.KeyboardNavigationMode.Once);
            list.Padding = new Thickness(0);
            list.Styles.Add(new Avalonia.Styling.Style(selector => Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector))
            {
                Setters =
                {
                    new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                    new Avalonia.Styling.Setter(Layoutable.MinHeightProperty, 0d),
                },
            });
        }
        _active.SelectionChanged += (_, _) => SelectRow(_active, _neutral);
        _neutral.SelectionChanged += (_, _) => SelectRow(_neutral, _active);

        _previous.Click += (_, _) =>
        {
            if (_history.TryPop(out var previous))
            {
                _cursor = previous;
                _pageOffset = Math.Max(0, _pageOffset - PageSize);
                StartLookup();
            }
        };
        _next.Click += (_, _) =>
        {
            if (_nextCursor is null)
            {
                return;
            }

            if (_history.Count >= 100)
            {
                // A bounded history: 100 cursors is far past any list a reader pages
                // through by hand, and the oldest are replaced by one honest route back.
                var retained = _history.Take(99).Reverse().ToArray();
                _history.Clear();
                foreach (var item in retained)
                {
                    _history.Push(item);
                }

                _historyTruncated = true;
            }

            _history.Push(_cursor);
            _cursor = _nextCursor;
            _pageOffset += PageSize;
            StartLookup();
        };
        _refresh.Click += (_, _) =>
        {
            if (_refreshTask is null || _refreshTask.IsCompleted)
            {
                _refreshTask = RefreshSnapshotAsync();
            }
        };
        _clearListSearch.Click += (_, _) =>
        {
            _search.Text = string.Empty;
            _search.Focus();
        };
        _firstPage.Click += (_, _) =>
        {
            _history.Clear();
            _historyTruncated = false;
            _cursor = null;
            _pageOffset = 0;
            StartLookup();
        };
        _selectedInclude.Click += async (_, _) => await ToggleSelectedAsync(exclude: false);
        _selectedExclude.Click += async (_, _) => await ToggleSelectedAsync(exclude: true);

        var done = new Button { Content = "Done", IsDefault = true, MinHeight = mobile ? 48 : 0 };
        done.Click += (_, _) =>
        {
            Complete(true);
            _ = DrainAsync();
        };
        // The range is a sentence, not a control: at 480 dp it cannot share a row with five
        // buttons without being ellipsized into "1-8 of 8 available value...". It takes its
        // own line, and the decision row keeps every action.
        _range.TextWrapping = TextWrapping.Wrap;
        _range.Margin = new Thickness(0, 8, 0, 4);
        var footerActions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var action in new[] { _firstPage, _previous, _next, _refresh, done })
        {
            action.Margin = new Thickness(3);
            footerActions.Children.Add(action);
        }

        var footer = new StackPanel { Children = { _range, footerActions } };

        if (mobile)
        {
            foreach (var control in new Control[]
                     {
                         _search, _selectedInclude, _selectedExclude, _firstPage,
                         _clearListSearch, _previous, _next, _refresh, done,
                     })
            {
                control.MinHeight = Math.Max(48, control.MinHeight);
            }
        }

        var selectedActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in new[] { _selectedInclude, _selectedExclude })
        {
            action.Margin = new Thickness(3);
            selectedActions.Children.Add(action);
        }

        var lists = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
        lists.Children.Add(_activeHeading);
        Grid.SetRow(_active, 1);
        lists.Children.Add(_active);
        var available = new TextBlock
        {
            Text = "Available values",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 2),
        };
        Grid.SetRow(available, 2);
        lists.Children.Add(available);
        Grid.SetRow(_neutral, 3);
        lists.Children.Add(_neutral);
        Grid.SetRow(_selectedDetail, 4);
        lists.Children.Add(_selectedDetail);
        Grid.SetRow(selectedActions, 5);
        lists.Children.Add(selectedActions);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(16),
        };
        layout.Children.Add(_search);
        Grid.SetRow(_scope, 1);
        layout.Children.Add(_scope);
        Grid.SetRow(_asOf, 2);
        layout.Children.Add(_asOf);
        Grid.SetRow(_status, 3);
        layout.Children.Add(_status);
        var hintRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        hintRow.Children.Add(_hint);
        Grid.SetColumn(_clearListSearch, 1);
        hintRow.Children.Add(_clearListSearch);
        Grid.SetRow(hintRow, 4);
        layout.Children.Add(hintRow);
        Grid.SetRow(_error, 5);
        layout.Children.Add(_error);
        Grid.SetRow(lists, 6);
        layout.Children.Add(lists);
        Grid.SetRow(footer, 7);
        layout.Children.Add(footer);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(_hint, AutomationLiveSetting.Polite);
        Content = layout;
    }

    protected override void OnPresented()
    {
        _tab.SnapshotChanged += OnWorkspaceSnapshotChanged;
        _tab.PropertyChanged += OnTabPropertyChanged;
        _initializeTask = InitializeAsync();
        Dispatcher.UIThread.Post(() => _search.Focus(), DispatcherPriority.Input);
    }

    internal override void Dismiss()
    {
        base.Dismiss();
        _ = DrainAsync();
    }

    internal override void ForceDismiss()
    {
        base.ForceDismiss();
        _ = DrainAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _status.Text = "Opening values…";
            _lease = SessionAccess.ReadForWork(_tab.SessionPath);
            _snapshot = await SessionStore.OpenAsync(_tab.SessionPath, cancellationToken: _lifetime.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_released)
                {
                    return;
                }

                UpdateSnapshotDisclosure();
                StartLookup();
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_released)
                {
                    return;
                }

                ShowError($"Could not open {_plural}. {Friendly(exception)} Try again.");
                _status.Text = string.Empty;
                _refresh.Content = "Retry";
                _refresh.IsVisible = true;
                _refresh.IsEnabled = true;
            });
        }
    }

    private void QueueLookup(bool resetPage)
    {
        var generation = ++_generation;
        _lookupCancellation?.Cancel();
        var debounce = DebounceAsync();
        TrackBackground(debounce);

        async Task DebounceAsync()
        {
            try
            {
                await Task.Delay(200, _lifetime.Token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }

                    if (resetPage)
                    {
                        _cursor = null;
                        _history.Clear();
                        _historyTruncated = false;
                        _pageOffset = 0;
                    }

                    StartLookup();
                });
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void StartLookup()
    {
        if (_snapshot is null || _released || _refreshing)
        {
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var previous = Interlocked.Exchange(ref _lookupCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var generation = ++_generation;
        var snapshot = _snapshot;
        var filter = _tab.AppliedFilter;
        var search = _search.Text ?? string.Empty;
        var cursor = _cursor;
        _status.Text = "Updating values…";
        ShowError(null);
        _previous.IsEnabled = false;
        _next.IsEnabled = false;
        _lookupTask = RunAsync();
        TrackBackground(_lookupTask);

        async Task RunAsync()
        {
            try
            {
                // Cancellation is cooperative while a high-cardinality tally is scanning.
                // Serializing the CPU section prevents a newer search from running beside
                // that superseded scan and multiplying memory/CPU pressure on a live tab.
                await _lookupSerial.WaitAsync(cancellation.Token).ConfigureAwait(false);
                FacetValuesResult result;
                try
                {
                    result = await Task.Run(
                        () => SessionQueryEngine.QueryFacetValues(
                            snapshot,
                            filter,
                            _queryDimension,
                            search,
                            cursor,
                            PageSize,
                            generation,
                            cancellation.Token),
                        cancellation.Token).ConfigureAwait(false);
                }
                finally
                {
                    _lookupSerial.Release();
                }

                await Dispatcher.UIThread.InvokeAsync(() => Publish(result, generation));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_released && generation == _generation)
                    {
                        ShowError($"Could not refresh {_plural}. {Friendly(exception)} Try again.");
                        _status.Text = string.Empty;
                        _refresh.Content = _newerSnapshotAvailable ? "Refresh counts" : "Retry";
                        _refresh.IsVisible = true;
                        _refresh.IsEnabled = true;
                    }
                });
            }
        }
    }

    private void Publish(FacetValuesResult result, int generation)
    {
        if (_released || generation != _generation || _snapshot?.Generation != result.Identity.SnapshotGeneration)
        {
            return;
        }

        var active = result.ActiveValues.Select(value => new FacetBrowserRow(value, _singular)).ToArray();
        var neutral = result.NeutralValues.Select(value => new FacetBrowserRow(value, _singular)).ToArray();
        _active.ItemsSource = active;
        _active.IsVisible = active.Length > 0;
        _activeHeading.Text = active.Length == 0
            ? "No active filters"
            : $"Active filters · {active.Length:N0}";
        _neutral.ItemsSource = neutral;
        _nextCursor = result.NextCursor;
        _previous.IsEnabled = _history.Count > 0;
        _next.IsEnabled = _nextCursor is not null;
        _firstPage.IsVisible = _historyTruncated && _pageOffset > 0;

        var first = neutral.Length == 0 ? 0 : _pageOffset + 1;
        var last = neutral.Length == 0 ? 0 : first + neutral.Length - 1;
        _range.Text = neutral.Length == 0
            ? $"0 of {result.NeutralMatchCount:N0} available values"
            : $"{first:N0}–{last:N0} of {result.NeutralMatchCount:N0} available values";
        _scope.Text = $"{result.NeutralMatchCount:N0} available {_plural} match this list search. " +
                      "Counts use the whole session and your other filters. This group ignores its own " +
                      "filters so you can add alternatives; an active time filter still applies.";
        _status.Text = string.Empty;
        if (!_newerSnapshotAvailable)
        {
            _refresh.Content = "Refresh counts";
            _refresh.IsVisible = false;
        }

        FollowMovedValue(active, neutral);
        // Three different empty states, because the reader's next move differs in each:
        // clear the list search, change the workspace filters, or nothing is there at all.
        var noListMatch = neutral.Length == 0 && result.SearchText.Length > 0;
        _hint.Text = neutral.Length > 0
            ? string.Empty
            : noListMatch
                ? $"No {_plural} match “{result.SearchText}”."
                : active.Length > 0
                    ? $"No additional {_plural} are available with the other filters."
                    : $"No {_plural} are available with the other filters.";
        _clearListSearch.IsVisible = noListMatch;
        if (_hint.Text.Length > 0)
        {
            AutomationProperties.SetName(_hint, _hint.Text);
        }
    }

    /// <summary>
    /// Keeps the reader on the value they just changed, in whichever section now holds it.
    /// </summary>
    /// <remarks>
    /// A value that becomes active leaves the available list, and a value that becomes
    /// neutral again may not come back to the current page at all. Following the key rather
    /// than the row index is what stops the next Enter from acting on an unrelated value;
    /// when the key is nowhere to be found, focus returns to the list search rather than
    /// to an arbitrary neighbour, and says what happened.
    /// </remarks>
    private void FollowMovedValue(FacetBrowserRow[] active, FacetBrowserRow[] neutral)
    {
        if (_followKey is not { } key)
        {
            return;
        }

        _followKey = null;
        var activeIndex = Array.FindIndex(active, row => row.Value.Key.Equals(key));
        if (activeIndex >= 0)
        {
            _active.SelectedIndex = activeIndex;
            _active.ScrollIntoView(activeIndex);
            Dispatcher.UIThread.Post(() => _active.Focus(), DispatcherPriority.Input);
            return;
        }

        var neutralIndex = Array.FindIndex(neutral, row => row.Value.Key.Equals(key));
        if (neutralIndex >= 0)
        {
            _neutral.SelectedIndex = neutralIndex;
            _neutral.ScrollIntoView(neutralIndex);
            Dispatcher.UIThread.Post(() => _neutral.Focus(), DispatcherPriority.Input);
            return;
        }

        _status.Text = "Filter removed.";
        Dispatcher.UIThread.Post(() => _search.Focus(), DispatcherPriority.Input);
    }

    private FacetQueryKey? _followKey;

    private FuncDataTemplate<FacetBrowserRow> RowTemplate() => new((row, _) =>
    {
        if (row is null)
        {
            return new TextBlock();
        }

        var valueText = new TextBlock
        {
            Text = row.Label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var count = new TextBlock
        {
            Text = row.Value.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        var include = ActionButton("+", row, exclude: false);
        var exclude = ActionButton("−", row, exclude: true);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            MinHeight = OperatingSystem.IsAndroid() ? 52 : 34,
        };
        grid.Children.Add(valueText);
        Grid.SetColumn(count, 1);
        grid.Children.Add(count);
        Grid.SetColumn(include, 2);
        grid.Children.Add(include);
        Grid.SetColumn(exclude, 3);
        grid.Children.Add(exclude);

        // Below a width that holds a readable value beside two touch targets, the actions
        // move under the value instead of squeezing it into an ellipsis. A tag name is the
        // reason the row exists; the buttons are the same size either way.
        var stacked = false;
        grid.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < StackedRowWidth;
            if (narrow == stacked)
            {
                return;
            }

            stacked = narrow;
            Grid.SetRow(include, narrow ? 1 : 0);
            Grid.SetRow(exclude, narrow ? 1 : 0);
            Grid.SetColumn(include, narrow ? 2 : 2);
            Grid.SetColumn(exclude, narrow ? 3 : 3);
            Grid.SetColumnSpan(valueText, narrow ? 4 : 1);
            // Count owns the left half of the second row; spanning all four columns
            // would paint it underneath the two right-aligned actions at phone widths.
            Grid.SetColumnSpan(count, narrow ? 2 : 1);
            count.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            count.Margin = narrow ? new Thickness(0, 0, 0, 2) : new Thickness(8, 0);
            Grid.SetRow(count, narrow ? 1 : 0);
            Grid.SetColumn(count, narrow ? 0 : 1);
            include.HorizontalAlignment = narrow ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            exclude.HorizontalAlignment = narrow ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        };
        AutomationProperties.SetName(grid, $"{_singular} {row.Label}, {row.Value.Count:N0} entries");
        return grid;
    }, supportsRecycling: false);

    private Button ActionButton(string glyph, FacetBrowserRow row, bool exclude)
    {
        var active = exclude ? row.Value.Excluded : row.Value.Included;
        var button = new Button
        {
            Content = glyph,
            IsTabStop = false,
            MinWidth = OperatingSystem.IsAndroid() ? 48 : 34,
            MinHeight = OperatingSystem.IsAndroid() ? 48 : 30,
            Margin = new Thickness(2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (active)
        {
            // Assigned only in the active state, for the same reason the facet pane's own
            // buttons are: a local null brush overrides the theme instead of falling back to
            // it, and a Border with no brush is not hit-tested over its fill. On the device
            // that made a 48 dp target respond only where its glyph was drawn — a tap on the
            // rest of it fell through to the row and merely selected it.
            button.Background = new SolidColorBrush(
                WorkspacePalette.Accent(ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light));
            button.Foreground = Brushes.White;
        }

        AutomationProperties.SetName(
            button,
            active
                ? $"Stop {(exclude ? "excluding" : "including")} {_singular} {row.Value.Key.DisplayText}"
                : $"{(exclude ? "Exclude" : "Include")} {_singular} {row.Value.Key.DisplayText}");
        button.Click += async (_, _) => await ToggleAsync(row.Value.Key, exclude);
        return button;
    }

    private async Task ToggleAsync(FacetQueryKey key, bool exclude)
    {
        var appKey = key.Number is { } number ? FacetKey.OfNumber(number) : FacetKey.OfText(key.Text ?? string.Empty);
        try
        {
            await _tab.ToggleFacetAsync(_dimension, appKey, exclude).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_released)
                {
                    // The value the reader pressed keeps their focus wherever it lands, so
                    // a refreshed page cannot move it onto whichever value now occupies the
                    // same row.
                    _followKey = key;
                    _cursor = null;
                    _history.Clear();
                    _historyTruncated = false;
                    _pageOffset = 0;
                    StartLookup();
                }
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() => ShowError($"Could not apply the filter. {Friendly(exception)} Try again."));
        }
    }

    private FacetBrowserRow? _selectedRow;

    /// <summary>
    /// Keeps one list selection and one stable pair of keyboard actions for it. Pointer/touch
    /// buttons stay in every realized row, but none of those make a 100-row page a 200-stop
    /// keyboard obstacle.
    /// </summary>
    private void SelectRow(ListBox source, ListBox other)
    {
        if (source.SelectedItem is not FacetBrowserRow row)
        {
            if (other.SelectedItem is null)
            {
                ShowSelectedValue(null);
            }

            return;
        }

        if (other.SelectedItem is not null)
        {
            other.SelectedItem = null;
        }

        ShowSelectedValue(row);
    }

    private void ShowSelectedValue(FacetBrowserRow? row)
    {
        _selectedRow = row;
        _selectedDetail.Text = row is null ? string.Empty : $"Selected {_singular}: {row.Label}";
        _selectedInclude.IsEnabled = row is not null;
        _selectedExclude.IsEnabled = row is not null;
        if (row is null)
        {
            _selectedInclude.Content = "Include selected";
            _selectedExclude.Content = "Exclude selected";
            return;
        }

        _selectedInclude.Content = row.Value.Included ? "Stop including" : "Include";
        _selectedExclude.Content = row.Value.Excluded ? "Stop excluding" : "Exclude";
        AutomationProperties.SetName(_selectedInclude, $"{_selectedInclude.Content} {_singular} {row.Label}");
        AutomationProperties.SetName(_selectedExclude, $"{_selectedExclude.Content} {_singular} {row.Label}");
    }

    private async Task ToggleSelectedAsync(bool exclude)
    {
        if (_selectedRow is { } row)
        {
            await ToggleAsync(row.Value.Key, exclude);
        }
    }

    private async Task RefreshSnapshotAsync()
    {
        _refreshing = true;
        _refresh.IsEnabled = false;
        _status.Text = "Refreshing counts…";
        SessionSnapshot? replacement = null;
        try
        {
            ++_generation;
            _lookupCancellation?.Cancel();
            await DrainBackgroundTasksAsync().ConfigureAwait(false);

            _lease ??= SessionAccess.ReadForWork(_tab.SessionPath);
            replacement = await SessionStore.OpenAsync(
                _tab.SessionPath,
                cancellationToken: _lifetime.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_released)
                {
                    return;
                }

                var previous = _snapshot;
                _snapshot = replacement;
                replacement = null;
                previous?.Dispose();
                _cursor = null;
                _history.Clear();
                _historyTruncated = false;
                _pageOffset = 0;
                _refresh.Content = "Refresh counts";
                _refresh.IsVisible = false;
                _newerSnapshotAvailable = false;
                _refreshing = false;
                UpdateSnapshotDisclosure();
                StartLookup();
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_released)
                {
                    return;
                }

                _status.Text = string.Empty;
                _refreshing = false;
                _refresh.Content = _snapshot is null ? "Retry" : "Refresh counts";
                _refresh.IsVisible = true;
                _refresh.IsEnabled = true;
                if (_snapshot is not null)
                {
                    // Search/filter edits made while the replacement was opening are still
                    // meaningful against the retained snapshot and must not be dropped.
                    StartLookup();
                }

                // StartLookup clears stale lookup failures. Publish deliberately leaves this
                // snapshot-refresh failure in place, so edits can update the retained page
                // without concealing that its counts are still from the older snapshot.
                ShowError("Could not refresh counts. Try again.");
            });
        }
        finally
        {
            replacement?.Dispose();
        }
    }

    private void OnWorkspaceSnapshotChanged(object? sender, EventArgs eventArgs) => Dispatcher.UIThread.Post(() =>
    {
        if (!_released && _snapshot?.Generation != _tab.Snapshot?.Generation)
        {
            _newerSnapshotAvailable = true;
            _refresh.IsVisible = true;
            _refresh.IsEnabled = true;
            _refresh.Content = "Refresh counts";
            _status.Text = "New records available";
        }
    });

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SessionTabViewModel.AppliedFilter) && !_tab.IsQueryPending)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_released)
                {
                    _cursor = null;
                    _history.Clear();
                    _historyTruncated = false;
                    _pageOffset = 0;
                    StartLookup();
                }
            });
        }
    }

    private void UpdateSnapshotDisclosure()
    {
        if (_snapshot is null)
        {
            return;
        }

        var local = TimeZoneInfo.ConvertTime(_snapshot.Manifest.UpdatedUtc, TimeZoneInfo.Local);
        _asOf.Text = $"As of {local:HH:mm:ss} · " + (_tab.IsLiveCaptureActive ? "capture continues" : "saved session");
        AutomationProperties.SetHelpText(
            _asOf,
            $"Counts are from the session snapshot published {local:yyyy-MM-dd HH:mm:ss} {TimeZoneInfo.Local.DisplayName}.");
    }

    private void ShowError(string? message)
    {
        _error.Text = message ?? string.Empty;
        _error.IsVisible = message is not null;
        if (message is not null)
        {
            AutomationProperties.SetName(_error, message);
        }
    }

    /// <summary>Stops and drains every query before releasing the pinned snapshot and lease.</summary>
    internal Task DrainAsync() => _releaseTask ??= ReleaseCoreAsync();

    private async Task ReleaseCoreAsync()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _tab.SnapshotChanged -= OnWorkspaceSnapshotChanged;
        _tab.PropertyChanged -= OnTabPropertyChanged;
        ++_generation;
        _lifetime.Cancel();
        _lookupCancellation?.Cancel();
        try
        {
            if (_initializeTask is not null)
            {
                await _initializeTask.ConfigureAwait(false);
            }

            if (_refreshTask is not null)
            {
                await _refreshTask.ConfigureAwait(false);
            }

            await DrainBackgroundTasksAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _lookupCancellation?.Dispose();
            _snapshot?.Dispose();
            _lease?.Dispose();
            _lookupSerial.Dispose();
            _lifetime.Dispose();
        }
    }

    public void Dispose()
    {
        _ = DrainAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Keeps every superseded lookup alive in the lifetime ledger until it has really stopped.
    /// </summary>
    /// <remarks>
    /// Merely replacing <c>_lookupTask</c> loses the previous task while its cancellation is
    /// still cooperative. A refresh could then dispose the pinned snapshot underneath that
    /// older query, and closing the sheet could release its deletion lease while managed work
    /// was still reading it. The bounded page/search UI can create several superseded tasks,
    /// so all of them are drained before either resource is released.
    /// </remarks>
    private void TrackBackground(Task task)
    {
        lock (_backgroundGate)
        {
            _backgroundTasks.Add(task);
        }

        _ = ForgetBackgroundAsync(task);
    }

    private async Task ForgetBackgroundAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_backgroundGate)
            {
                _backgroundTasks.Remove(task);
            }
        }
    }

    private async Task DrainBackgroundTasksAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_backgroundGate)
            {
                pending = [.. _backgroundTasks];
            }

            if (pending.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Lookup workers publish their own actionable error. Draining owns only their
                // lifetime and must still release the pinned snapshot after that publication.
            }
            finally
            {
                lock (_backgroundGate)
                {
                    foreach (var completed in pending.Where(static task => task.IsCompleted))
                    {
                        _backgroundTasks.Remove(completed);
                    }
                }
            }
        }
    }

    /// <summary>
    /// What the browser for a dimension is called, wherever it is named.
    /// </summary>
    /// <remarks>
    /// The facet pane's own Find button announces itself with this, rather than lower-casing
    /// its group heading: that turned <c>PIDs</c> into <c>pids</c> and made the button and
    /// the dialog it opens disagree about the name of the same thing.
    /// </remarks>
    internal static string TitleFor(FacetDimension dimension) => dimension switch
    {
        FacetDimension.Tag => "Find tags",
        FacetDimension.Process => "Find processes",
        FacetDimension.Pid => "Find PIDs",
        FacetDimension.Tid => "Find threads",
        FacetDimension.Buffer => "Find buffers",
        _ => "Find values",
    };

    private static string Friendly(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Access was denied.",
        IOException => "The session could not be read.",
        _ => "The values could not be read.",
    };

    private sealed record FacetBrowserRow(FacetQueryValue Value, string Singular)
    {
        /// <summary>
        /// What the value reads as. A dimension can hold a genuinely empty value — a format
        /// that carries no buffer name — and a blank row with two buttons beside it says
        /// nothing to anyone. The key stays empty; only the label says the value is absent.
        /// </summary>
        internal string Label => Value.Key.DisplayText.Length == 0 ? "(none)" : Value.Key.DisplayText;

        /// <summary>
        /// The row's own sentence, matching the name its content carries.
        /// </summary>
        /// <remarks>
        /// A ListBoxItem with no name of its own falls back to the item's <c>ToString()</c>,
        /// and a record's generated one is its C# declaration. On the device TalkBack reached
        /// the container first and read <c>FacetBrowserRow { Value = FacetQueryValue { Key =
        /// …</c> where the value's name belonged.
        /// </remarks>
        public override string ToString() => $"{Singular} {Label}, {Value.Count:N0} entries";
    }
}
