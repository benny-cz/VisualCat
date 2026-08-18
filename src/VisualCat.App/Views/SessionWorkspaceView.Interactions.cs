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

    /// <summary>Name given to the message block of every row, so the two styles below can
    /// address it without the row template knowing anything about selection.</summary>
    private const string MessageBlockName = "EntryMessage";

    /// <summary>Name of the mobile row's "there is more of this" affordance.</summary>
    private const string ExpandGlyphName = "EntryExpand";

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

        // One clipped line is the right density for a table being scanned and the wrong
        // answer for the one row the reader has actually picked (§14.9). Exactly one row
        // ever differs, so the table stays scannable and virtualization is untouched.
        // Both states come from styles because a local value in the template would
        // outrank the selected-state setter and never yield.
        _entries.Styles.Add(MessageLineStyle(collapsed: true, maximumLines: 1));
        _entries.Styles.Add(MessageLineStyle(collapsed: false, _mobile ? 4 : 2));
        if (_mobile)
        {
            _entries.Styles.Add(ExpandGlyphStyle(onSelectedRow: false));
            _entries.Styles.Add(ExpandGlyphStyle(onSelectedRow: true));
        }

        // A row's automation name has to be set on the container, not inside the template:
        // a ListBoxItem with no name of its own falls back to its content's ToString(), and
        // the content here is the NormalizedEntry record, whose generated ToString() is a
        // ~400-character dump of every field including the session guid and the raw span.
        // TalkBack read that, in full, for every row. ContainerPrepared is the hook that
        // survives virtualization: a recycled container is prepared again for its new item.
        _entries.ContainerPrepared += (_, eventArgs) =>
        {
            if (eventArgs.Container.DataContext is NormalizedEntry entry)
            {
                AutomationProperties.SetName(eventArgs.Container, EntryAutomationName(entry));
            }
        };
        _entries.ContainerClearing += (_, eventArgs) =>
            AutomationProperties.SetName(eventArgs.Container, string.Empty);

        ApplyEntryTemplate();
        AutomationProperties.SetName(_entries, "Filtered log entries");
    }

    /// <summary>Characters of a message a screen reader is given for one row.</summary>
    private const int RowSpokenMessageLength = 320;

    /// <summary>
    /// What a screen reader should hear for one row: which severity, which tag, when, and
    /// what it said — the same shape the inspector's message block already uses, and in the
    /// order the questions arrive. The message is capped because a row is a table cell, not
    /// the record: the inspector reads the rest.
    /// </summary>
    private string EntryAutomationName(NormalizedEntry entry)
    {
        var message = entry.Message.Length > RowSpokenMessageLength
            ? entry.Message[..RowSpokenMessageLength] + "…"
            : entry.Message;
        return $"{entry.Level} {entry.Tag} at {FormatInstant(entry.Timestamp)}: " +
               message.ReplaceLineEndings(" ");
    }

    /// <summary>
    /// Line budget for a row's message. The expanded style is added after the collapsed
    /// one so it wins for the selected row; both are needed because a style cannot
    /// override a value the template set locally.
    /// </summary>
    /// <remarks>
    /// The collapsed row must not wrap. A one-line budget under <see cref="TextWrapping.Wrap"/>
    /// lays the message out as wrapped lines and then keeps the first one, so what the row
    /// draws is the text up to the last <em>word-break opportunity</em> that fits — not the
    /// text that fits. A message whose next token is long and unbreakable ends its first
    /// line early, and the row then ellipsised at a third of its width with two thirds
    /// empty beside it, while a neighbouring row of the same length filled the row. Whether
    /// it happened depended on where the break opportunities fell, which is why it looked
    /// arbitrary: <c>Intent {</c> forbids a break after the brace (UAX #14) and filled the
    /// row, <c>Zntent Z</c> offered one and clipped. <see cref="TextWrapping.NoWrap"/> plus
    /// character ellipsis fills the row and clips at the exact pixel it runs out — which is
    /// what a single-line cell means. Only the selected row, which has a real multi-line
    /// budget, wraps (finding 2).
    /// </remarks>
    private static Avalonia.Styling.Style MessageLineStyle(bool collapsed, int maximumLines)
    {
        var style = new Avalonia.Styling.Style(selector =>
        {
            var item = Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector);
            return (collapsed ? item : item.Class(":selected"))
                .Descendant()
                .OfType<TextBlock>()
                .Name(MessageBlockName);
        });
        style.Setters.Add(new Avalonia.Styling.Setter(
            TextBlock.TextWrappingProperty,
            collapsed ? TextWrapping.NoWrap : TextWrapping.Wrap));
        style.Setters.Add(new Avalonia.Styling.Setter(TextBlock.MaxLinesProperty, maximumLines));
        return style;
    }

    /// <summary>
    /// Shows the affordance on the selected row only. Both states are styles for the same
    /// reason the line budget is: a local value in the template would outrank the
    /// selected-state setter and the glyph would never appear.
    /// </summary>
    private static Avalonia.Styling.Style ExpandGlyphStyle(bool onSelectedRow)
    {
        var style = new Avalonia.Styling.Style(selector =>
        {
            var item = Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector);
            return (onSelectedRow ? item.Class(":selected") : item)
                .Descendant()
                .OfType<TextBlock>()
                .Name(ExpandGlyphName);
        });
        style.Setters.Add(new Avalonia.Styling.Setter(Visual.IsVisibleProperty, onSelectedRow));
        return style;
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
                        RowPreview(entry.Message),
                        _viewModel.Filter.Search,
                        static text => new TextBlock
                        {
                            Name = MessageBlockName,
                            Text = text,
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

            // Costs no row height and appears only on the row the reader picked, so the
            // route to the rest of the message sits where the finger already is.
            var expand = new TextBlock
            {
                Name = ExpandGlyphName,
                Text = "⤢",
                FontSize = 15,
                Foreground = new SolidColorBrush(WorkspacePalette.Accent(dark)),
                Margin = new Thickness(2, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(expand, 2);

            var edge = SeverityEdge(entry.Level);
            Grid.SetColumn(edge, 0);
            return new Border
            {
                BorderBrush = separator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = RowTint(entry.Level, dark),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("3,*,Auto"),
                    Children = { edge, body, expand },
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
            RowPreview(entry.Message),
            _viewModel.Filter.Search,
            static text => new TextBlock
            {
                Name = MessageBlockName,
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 2),
            });

        // A pointer already resting on a clipped message is the cheapest possible moment to
        // answer "what does the rest say" — but only where a pointer exists, and only when
        // something is actually hidden (the same rule the summary readout follows).
        if (entry.Message.Length > 72 || entry.Message.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            ToolTip.SetTip(cell, RowPreview(entry.Message));
        }

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
        block.Inlines = HighlightInlines(text, spans);
        return block;
    }

    /// <summary>
    /// Marks the search term inside a block that already exists — the inspector's message,
    /// which is re-pointed at a new entry rather than rebuilt per row.
    /// </summary>
    private static void ApplyHighlight(TextBlock block, string text, TextSearchSpec? search)
    {
        var spans = EntryHighlight.Match(text, search);
        if (spans.Count == 0)
        {
            block.Inlines?.Clear();
            block.Text = text;
            return;
        }

        block.Text = string.Empty;
        block.Inlines = HighlightInlines(text, spans);
    }

    private static InlineCollection HighlightInlines(string text, IReadOnlyList<HighlightSpan> spans)
    {
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

        return inlines;
    }

    /// <summary>Lines of a message a row may ever draw.</summary>
    private const int RowPreviewLines = 5;

    /// <summary>Characters of a message a row may ever draw.</summary>
    private const int RowPreviewLength = 640;

    /// <summary>
    /// The slice of a message a row can possibly show: one line collapsed, four selected.
    /// Long-format captures join every body line into one message
    /// (<c>SessionCoordinator</c>), so a record here can be megabytes, and laying all of it
    /// out per realized row would be pure waste (§19.3). The inspector shows the rest.
    /// </summary>
    private static string RowPreview(string message)
    {
        var limit = Math.Min(message.Length, RowPreviewLength);
        var lines = 0;
        for (var index = 0; index < limit; index++)
        {
            if (message[index] == '\n' && ++lines >= RowPreviewLines)
            {
                limit = index;
                break;
            }
        }

        return limit >= message.Length ? message : message[..limit];
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

    /// <summary>
    /// Zone every rendered timestamp is read in.
    ///
    /// A device capture is <em>parsed</em> in UTC because that is the format logcat is
    /// asked for (<c>-v …,UTC,…</c>), and that is a parsing decision, not a reading one.
    /// Rendering it back as UTC put the newest entry a whole UTC offset in the past on any
    /// device that is not on UTC — two hours, on the machine this was found on — so a
    /// running capture with Follow engaged looked like it had stopped receiving data.
    /// A capture of what is happening right now has to agree with the clock the reader is
    /// looking at.
    ///
    /// Everything else keeps the session's own policy zone, which is what makes a rendered
    /// row agree with the raw line behind it: an imported file's naive timestamps mean
    /// whatever the policy says they mean, and a followed file is read the same way.
    /// </summary>
    internal string DisplayZoneId()
    {
        if (_viewModel.Snapshot?.Descriptor is not { } descriptor)
        {
            return TimeZoneInfo.Utc.Id;
        }

        return descriptor.SourceKind is SourceKind.Adb or SourceKind.Android
            ? TimeZoneInfo.Local.Id
            : descriptor.TimestampPolicy.TimeZoneId;
    }

    private TimeZoneInfo ResolveSessionZone()
    {
        var zoneId = DisplayZoneId();
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

    /// <summary>Display-zone month-day time — the ISO round-trip form with its offset
    /// suffix overflowed every column it appeared in. Full precision lives in the raw
    /// context and the session pane.</summary>
    /// <remarks>
    /// The fraction shows the digits the capture actually carries. Every logcat text format
    /// prints milliseconds unless the capture asked for the <c>usec</c> modifier, so a fixed
    /// six digits printed three constant zeros on most sessions — width taken from the
    /// message on the row where the message is already being clipped (finding 25). The
    /// decision is per session and one-way: the first entry that carries sub-millisecond
    /// detail widens every timestamp, so precision is never hidden and the column never
    /// oscillates while paging.
    /// </remarks>
    private string FormatInstant(InstantUs? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value.ToDateTimeOffset(), ResolveSessionZone())
                .ToString(
                    _microsecondTimestamps
                        ? TimestampPrecision.MicrosecondFormat
                        : TimestampPrecision.MillisecondFormat,
                    System.Globalization.CultureInfo.InvariantCulture)
            : "untimed";

    /// <summary>
    /// Widens the timestamp column the first time the session shows sub-millisecond detail.
    /// Rows are realized once and keep the text they were built with, so the template is
    /// reinstalled on the transition — exactly once per session, and never afterwards.
    /// </summary>
    private void ObserveTimestampPrecision()
    {
        if (_microsecondTimestamps)
        {
            return;
        }

        var needed = TimestampPrecision.NeedsMicroseconds(_viewModel.Statistics?.FirstInstant) ||
                     TimestampPrecision.NeedsMicroseconds(_viewModel.Statistics?.LastInstant);
        if (!needed)
        {
            foreach (var entry in _viewModel.Entries)
            {
                if (TimestampPrecision.NeedsMicroseconds(entry.Timestamp))
                {
                    needed = true;
                    break;
                }
            }
        }

        if (!needed)
        {
            return;
        }

        _microsecondTimestamps = true;
        if (_entries.ItemTemplate is not null)
        {
            ApplyEntryTemplate();
        }

        UpdateSummaryText();
        if (_inspectedEntry is { } inspected)
        {
            SetInspectedEntry(inspected);
        }
    }

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
                var fromTimeline = _selectingTimelineEntry;
                _selectingTimelineEntry = false;
                var replaced = _inspectedEntry is not { } inspected || inspected.EntryId != entry.EntryId;
                _selectedEntryId = entry.EntryId;
                SetInspectedEntry(entry);

                // Reading the table no longer costs the user their place in the plot
                // (§14.9): the plot marks where this row sits, and says which way to look
                // when the row is outside the current viewport.
                _timeline.SetSelectedEntry(entry.Timestamp, entry.Level);

                // A refresh hands back an equal entry under a new instance. Re-reading its
                // source bytes would restart the inspector the user is mid-read of, so only
                // a genuinely different entry loads.
                if (replaced || fromTimeline)
                {
                    BeginRawContextLoad(entry, fromTimeline ? _selectedTimelineCellCount : null);
                }
            }
            else if (!_reloadingEntries)
            {
                // A collection reset is not a decision to stop reading an entry; only an
                // actual deselection is.
                _selectedEntryId = null;
                SetInspectedEntry(null);
                _timeline.SetSelectedEntry(null, null);
            }
        };

        // Where the finger already is: tapping the row that is already selected opens its
        // full message. The press is what records "already selected", because by the time
        // the tap is delivered the first tap of a fresh row has changed that answer.
        if (_mobile)
        {
            _entries.AddHandler(
                InputElement.PointerPressedEvent,
                (_, eventArgs) => _pressedSelectedEntry =
                    EntryUnder(eventArgs.Source as Control) is { } pressed &&
                    _inspectedEntry is { } current &&
                    pressed.EntryId == current.EntryId,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _entries.Tapped += (_, _) =>
            {
                if (_pressedSelectedEntry)
                {
                    _pressedSelectedEntry = false;
                    ShowInspector();
                }
            };
        }
        else
        {
            _entries.DoubleTapped += (_, _) => ShowInspector();
        }

        _viewModel.EntriesReloading += (_, _) => _reloadingEntries = true;
        _viewModel.EntriesReloaded += (_, _) =>
        {
            _reloadingEntries = false;
            ObserveTimestampPrecision();
            RestoreEntrySelection();
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
                case nameof(SessionTabViewModel.Activity):
                case nameof(SessionTabViewModel.FailureReason):
                    UpdateCaptureActions();
                    break;
                case nameof(SessionTabViewModel.CaptureHealthWarning):
                    // The status bar can only carry a marker pointing here, so the pane
                    // has to rebuild the moment the warning appears or clears rather than
                    // waiting for whatever refresh happens to come next.
                    UpdateSessionInfo();
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

                    // The inspector says which of a bar's entries is on screen, so releasing
                    // the bar has to be able to change that sentence (finding 6).
                    UpdateSelectionHint();
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
                    UpdateSelectionHint();
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
                // Nothing newer is loading — the view moved, which released the scope — so
                // the pane must stop claiming that something is still being read (finding 5).
                _timelineEntryPending = false;
                await Dispatcher.UIThread.InvokeAsync(ResolveInterruptedRawLoad);
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
        UpdateEntryActionRows();
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


    /// <summary>
    /// Collapses the contextual action row when nothing in it applies, so an empty row costs
    /// no height while the controls above it keep their slots (finding 26).
    /// </summary>
    private void UpdateEntryActionRows()
    {
        if (_entryContextActions is not { } contextActions)
        {
            return;
        }

        contextActions.IsVisible = contextActions.Children.Any(static child => child.IsVisible);
    }

    /// <summary>The entry of the row containing <paramref name="source"/>, if any.</summary>
    private static NormalizedEntry? EntryUnder(Control? source)
    {
        for (var control = source; control is not null; control = control.Parent as Control)
        {
            if (control is ListBoxItem item)
            {
                return item.DataContext as NormalizedEntry;
            }
        }

        return null;
    }

    /// <summary>
    /// Re-selects the entry the reader had after a refresh replaced the collection. The
    /// rows are new instances of an equal record, so identity is the entry id rather than
    /// the object, and a selection that no longer matches the filter simply stays released.
    /// </summary>
    private void RestoreEntrySelection()
    {
        if (_selectedEntryId is not { } entryId || _entries.SelectedItem is NormalizedEntry)
        {
            return;
        }

        foreach (var candidate in _viewModel.Entries)
        {
            if (candidate.EntryId == entryId)
            {
                _entries.SelectedItem = candidate;
                return;
            }
        }

        // The entry is not among the loaded rows. While rows remain unloaded it may simply be
        // further down the list, but a complete page that does not contain it means the
        // current view no longer holds it — and the plot's caret then marks a row the table
        // cannot show, in a lane whose own header reads zero (finding 28).
        if (!_viewModel.CanLoadMore)
        {
            _selectedEntryId = null;
            SetInspectedEntry(null);
            _timeline.SetSelectedEntry(null, null);
        }
    }

    /// <summary>Brings the selected entry's full message on screen (§14.9).</summary>
    private void ShowInspector()
    {
        if (_inspectedEntry is null)
        {
            return;
        }

        if (_mobile)
        {
            if (_mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Plot)
            {
                SetMobileDisplayMode(MobileWorkspaceDisplayMode.Split);
            }

            if (_mobileAnalysisTabs is { } tabs)
            {
                tabs.SelectedIndex = tabs.Items.Count - 1;
            }
        }
        else
        {
            SetRawExpanded(true);
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
