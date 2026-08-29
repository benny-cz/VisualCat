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
using VisualCat.Core.Query;
using VisualCat.Domain;
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
        new(WorkspacePalette.SearchMatch);
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush SearchHighlightText =
        new(WorkspacePalette.SearchMatchText);

    /// <summary>Name given to the message block of every row, so the two styles below can
    /// address it without the row template knowing anything about selection.</summary>
    private const string MessageBlockName = "EntryMessage";

    /// <summary>Name of the mobile row's "there is more of this" affordance.</summary>
    private const string ExpandGlyphName = "EntryExpand";

    /// <summary>Name of the mobile row's timestamp · process · buffer line.</summary>
    private const string MetadataBlockName = "EntryMetadata";

    private Avalonia.Styling.Style? _entryRowStyle;
    private double _entryRowMinimumHeight = double.NaN;

    private void ConfigureEntryList()
    {
        _entries.SelectionMode = SelectionMode.Multiple;

        // A log table earns its keep by rows on screen: monospace for scanability and
        // compact item containers instead of Fluent's touch-sized defaults.
        _entries.FontFamily = MonoFont;
        _entries.FontSize = TextScale.Of(_mobile ? 12 : 12.5);
        if (!_mobile)
        {
            // The column header is a separate grid drawn flush to the list's left edge, so
            // the rows must sit flush too — a list or item inset slides every value out
            // from under its label.
            _entries.Padding = new Thickness(0);
            ApplyEntryRowHeight(22);
        }
        else
        {
            // A virtualized item keeps its desired height even when the last viewport sliver
            // is shorter. Without clipping it painted through the list and into the status
            // band in compact landscape (F-09). The parent pane clips as well, but the list is
            // the semantic boundary and owns the invariant.
            _entries.ClipToBounds = true;
            ApplyEntryRowHeight(64);

            // The metadata line is the row's own foreground and Fluent's selection is not,
            // so a selected row printed muted grey on accent blue at 1.97:1 (finding 7). It
            // is styled rather than set in the template for the reason every other
            // state-dependent value here is: a local value in the template outranks the
            // selected-state setter and would never yield.
            _entries.Styles.Add(MetadataLineStyle(onSelectedRow: false));
            _entries.Styles.Add(MetadataLineStyle(onSelectedRow: true));
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
    /// <remarks>
    /// 320 was still a paragraph per row. A row draws one ellipsised line, and what it is for
    /// is deciding whether to open the entry — so what it should say is enough to make that
    /// decision and no more. A binary payload made the cost plain: rows carrying a
    /// 1,000-character hex <c>DUMP=…</c> handed a reader 300 characters of hex to sit through
    /// before the next row (audit 3, B6). At 120 an ordinary logcat message still arrives
    /// whole, a dump announces itself and stops, and the sentence that follows says where the
    /// rest is.
    /// </remarks>
    private const int RowSpokenMessageLength = 120;

    /// <summary>
    /// What a screen reader should hear for one row: which severity, which tag, when, and
    /// what it said — the same shape the inspector's message block already uses, and in the
    /// order the questions arrive. The message is capped because a row is a table cell, not
    /// the record: the inspector reads the rest.
    /// </summary>
    private string EntryAutomationName(NormalizedEntry entry) =>
        $"{entry.Level} {entry.Tag} at {FormatInstant(entry.Timestamp)}: " +
        SpokenEntryMessage(entry.Message).ReplaceLineEndings(" ");

    /// <summary>As much of a message as a row is worth spending a reader's time on.</summary>
    internal static string SpokenEntryMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Length > RowSpokenMessageLength
            ? message[..RowSpokenMessageLength] + "… (open the entry for the rest)"
            : message;
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
    /// The row's secondary line, lifted to primary text on the row the reader has picked.
    /// </summary>
    /// <remarks>
    /// Selection is a tint rather than a slab now (see <c>ProductTheme.SelectedRowFill</c>),
    /// which restores the metadata line's contrast on its own — but the selected row is also
    /// the one row being read rather than scanned, and muted is the wrong weight for it.
    /// </remarks>
    private static Avalonia.Styling.Style MetadataLineStyle(bool onSelectedRow)
    {
        var style = new Avalonia.Styling.Style(selector =>
        {
            var item = Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector);
            return (onSelectedRow ? item.Class(":selected") : item)
                .Descendant()
                .OfType<TextBlock>()
                .Name(MetadataBlockName);
        });

        // Named rather than captured. A style is built once per list and never rebuilt, so
        // the brush it closes over is the brush it keeps: these two setters were resolved
        // against whatever variant was in force when the workspace was constructed, which on
        // a cold start in light mode was the dark one, and the row went on printing #8FA5C4
        // on #E9EFF7 at 2.17:1 for the life of the process (audit 2, A1c). A dynamic
        // resource is re-resolved by the framework whenever the variant changes.
        style.Setters.Add(new Avalonia.Styling.Setter(
            TextBlock.ForegroundProperty,
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(onSelectedRow
                ? VisualCat.App.Theme.ProductTheme.TextPrimaryKey
                : VisualCat.App.Theme.ProductTheme.TextMutedKey)));
        return style;
    }

    /// <summary>
    /// Sets how short an entry row may be, and re-applies it when the viewport class changes.
    /// </summary>
    /// <remarks>
    /// A landscape phone gives the analysis pane about a third of the height a portrait one
    /// does, and the fixed chrome above the list did not shrink with it — the list was left
    /// a few pixels tall and no log line was readable in any mode (finding 2). The row keeps
    /// a 48 dp touch target there; the 64 dp portrait row is comfort, not reach. A style is
    /// replaced rather than mutated because a setter's value is read when the style is
    /// applied, not on every change.
    /// </remarks>
    private void ApplyEntryRowHeight(double minimumHeight)
    {
        if (_entryRowMinimumHeight.Equals(minimumHeight))
        {
            return;
        }

        _entryRowMinimumHeight = minimumHeight;
        if (_entryRowStyle is { } previous)
        {
            _entries.Styles.Remove(previous);
        }

        // First in the collection: the message-line and glyph styles added after it are
        // state-dependent and must keep winning.
        _entryRowStyle = CompactItemStyle(minimumHeight);
        _entries.Styles.Insert(0, _entryRowStyle);
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
                    Cell(entry.Level.ToLetter().ToString(), 1, LevelPalette.InkBrushOf(entry.Level, dark)),
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

    /// <summary>
    /// Whether entry rows are drawn in two lines rather than three.
    /// </summary>
    /// <remarks>
    /// A three-line row is 58 dp, and in a 434 dp landscape viewport that is most of what the
    /// list has: the pane measured 1.3 rows of log under three 42 dp bands of chrome
    /// (audit 3, D2). The lines are the same three; two of them share a line, because the tag
    /// and the timestamp are both identity and read together anyway. The message keeps a line
    /// of its own, which is the one that matters.
    /// </remarks>
    private bool _compactEntryRows;

    /// <summary>Chooses the row density for the viewport, and rebuilds the rows if it changed.</summary>
    private void SetCompactEntryRows(bool compact)
    {
        if (_compactEntryRows == compact)
        {
            return;
        }

        _compactEntryRows = compact;
        if (_entries.ItemTemplate is not null)
        {
            ApplyEntryTemplate();
        }
    }

    private FuncDataTemplate<NormalizedEntry> BuildMobileEntryTemplate()
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var separator = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        var compact = _compactEntryRows;
        return new FuncDataTemplate<NormalizedEntry>((entry, _) =>
        {
            if (entry is null)
            {
                return new Border();
            }

            // The row's identity, and the one place the severity palette is read rather than
            // looked at (audit 3, B1).
            var identity = new TextBlock
            {
                Text = $"{entry.Level.ToLetter()}  {entry.Tag}",
                FontWeight = FontWeight.Bold,
                Foreground = LevelPalette.InkBrushOf(entry.Level, dark),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            // No local Foreground: the two MetadataLineStyle rules own it, so the selected row
            // can lift it out of muted (finding 7).
            var metadata = new TextBlock
            {
                Name = MetadataBlockName,
                Text = compact
                    ? $"{FormatInstant(entry.Timestamp)}  ·  {ProcessLabel(entry)}:{entry.Tid}"
                    : $"{FormatInstant(entry.Timestamp)}  ·  {ProcessLabel(entry)}:{entry.Tid}  ·  {entry.Buffer}",
                FontSize = TextScale.Of(10),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = compact ? new Thickness(8, 0, 0, 1) : default,
            };

            var message = HighlightedText(
                RowPreview(entry.Message),
                _viewModel.Filter.Search,
                static text => new TextBlock
                {
                    Name = MessageBlockName,
                    Text = text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

            Control head = identity;
            if (compact)
            {
                var heads = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                heads.Children.Add(identity);
                Grid.SetColumn(metadata, 1);
                heads.Children.Add(metadata);
                head = heads;
            }

            var body = new StackPanel
            {
                Margin = compact ? new Thickness(7, 3) : new Thickness(7, 5),
                Spacing = compact ? 1 : 2,
                Children = { head, message },
            };
            if (!compact)
            {
                body.Children.Add(metadata);
            }
            Grid.SetColumn(body, 1);

            // The row's own name already says the severity, the tag, the time, and as much of
            // the message as is worth hearing. Left in the tree, the three lines said all of it
            // a second time — and the message line said the whole <em>uncapped</em> string,
            // which is where a kilobyte of hex was arriving from (audit 3, B6). What is drawn
            // and what is spoken are the same content presented for two different senses; only
            // one of them should say it.
            foreach (var line in new[] { identity, metadata, message })
            {
                AutomationProperties.SetAccessibilityView(line, AccessibilityView.Raw);
                AutomationProperties.SetIsControlElementOverride(line, false);
            }

            // Costs no row height and appears only on the row the reader picked, so the
            // route to the rest of the message sits where the finger already is.
            var expand = new TextBlock
            {
                Name = ExpandGlyphName,
                Text = "⤢",
                FontSize = TextScale.Of(15),
                Foreground = new SolidColorBrush(WorkspacePalette.Accent(dark)),
                Margin = new Thickness(2, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            // A decoration on the selected row, not a control: tapping the row is what opens
            // the entry, and a lone "⤢" is not a thing to announce.
            AutomationProperties.SetAccessibilityView(expand, AccessibilityView.Raw);
            AutomationProperties.SetIsControlElementOverride(expand, false);
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
                FontSize = TextScale.Of(9),
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
        _timeline.SearchMarkerPicked += (_, instant) => _ = RunUiActionAsync(() => GoToNearestMatchAsync(instant));
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
                _selectionFilterFingerprint = _viewModel.Filter.Fingerprint();
                _selectedEntryInstant = entry.Timestamp;
                SetSelectedEntryOffPage(false);
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
                // actual deselection is. Nor is the live window moving past it: that case is
                // handled in RestoreEntrySelection, which is what runs after a refresh.
                if (_selectedEntryOffPage)
                {
                    return;
                }

                ClearEntrySelection();
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
                var onSelected = _pressedSelectedEntry;
                _pressedSelectedEntry = false;
                if (onSelected && !EntriesJustMoved())
                {
                    ShowInspector();
                }
            };

            // Every arrange, but only a handful of comparisons, and it writes nothing back
            // into the layout it is observing.
            _entries.LayoutUpdated += (_, _) => ObserveEntriesPosition();
        }
        else
        {
            _entries.DoubleTapped += (_, _) => ShowInspector();
        }

        // Named handlers rather than lambdas, so DetachViewModel can take them off again. A
        // workspace view is replaced rather than mutated when the reader changes the device's
        // text size — every font size in it was resolved while it was being built — and a view
        // left subscribed to a session it no longer draws would answer every change that
        // session makes for as long as the tab is open (audit 3, A2).
        _viewModel.EntriesReloading += OnEntriesReloading;
        _viewModel.EntriesReloaded += OnEntriesReloaded;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SnapshotChanged += OnViewModelSnapshotChanged;
    }

    /// <summary>
    /// Stops this view from answering its session. Called when the view is replaced.
    /// </summary>
    /// <remarks>
    /// Its place in the shell goes back with its subscriptions. In a compact-height viewport
    /// the command strip has been reparented into the application's own row, so a view that is
    /// dropped without giving it back leaves it there — and the replacement adds its strip
    /// beside it rather than instead of it (finding F-39). Taking the visual half off at the
    /// same moment as the event half is what makes "replaced" mean one thing.
    /// </remarks>
    internal void DetachViewModel()
    {
        _viewModel.EntriesReloading -= OnEntriesReloading;
        _viewModel.EntriesReloaded -= OnEntriesReloaded;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.SnapshotChanged -= OnViewModelSnapshotChanged;
        HostCompactCommands(null);
    }

    /// <summary>Whether this view has already said that its session is a partial recovery.</summary>
    private bool _partialRecoveryAnnounced;

    /// <summary>
    /// Says once, in the notice lane, that this session's acquisition never finished.
    /// </summary>
    /// <remarks>
    /// The status line and Session info both carry the fact continuously (finding F-19), but a
    /// reader entering through automatic session restoration is looking at rows, not at the
    /// status line, and the difference between "this capture is complete" and "this capture
    /// stops here because the app did" is the whole meaning of what they are reading.
    /// </remarks>
    private void AnnouncePartialRecovery()
    {
        if (_partialRecoveryAnnounced ||
            _viewModel.Activity != SessionActivity.RecoverablePartial)
        {
            return;
        }

        _partialRecoveryAnnounced = true;
        const string message =
            "This capture was interrupted. Everything below reached disk and is exact; " +
            "anything the source produced after the last save is not in the session.";
        if (PartialRecoveryRaised is { } recovery)
        {
            recovery(message);
        }
        else
        {
            Notify(message, failure: true);
        }
    }

    /// <summary>Where the entries list was on screen the last time it was arranged.</summary>
    private double _entriesTopOnScreen = double.NaN;

    /// <summary>When the entries list last changed position.</summary>
    private long _entriesMovedAtMs;

    /// <summary>
    /// Notices the list moving under the reader, so a tap that lands on a row which arrived
    /// after the finger did is not read as a request to open it.
    /// </summary>
    /// <remarks>
    /// Publishing a notice takes about 160 px from the workspace band, and the row that had
    /// been under the reader's thumb moves up with everything else. Tapping <em>Copy raw</em>
    /// and tapping again 350 ms later at the same coordinate therefore hit the reflowed entry
    /// list, and — because the row that arrived there was the selected one — opened the Entry
    /// inspector: two taps in one place invoking two different things, which is the invariant
    /// U-18 exists to hold (finding F-24).
    /// </remarks>
    private void ObserveEntriesPosition()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel ||
            _entries.TranslatePoint(default, topLevel) is not { } origin)
        {
            return;
        }

        if (double.IsNaN(_entriesTopOnScreen))
        {
            _entriesTopOnScreen = origin.Y;
            return;
        }

        if (Math.Abs(origin.Y - _entriesTopOnScreen) > 1)
        {
            _entriesTopOnScreen = origin.Y;
            _entriesMovedAtMs = Environment.TickCount64;
        }
    }

    /// <summary>Whether the list moved within the platform's double-tap interval.</summary>
    private bool EntriesJustMoved() =>
        _entriesMovedAtMs != 0 &&
        Environment.TickCount64 - _entriesMovedAtMs < ListSettleMilliseconds;

    /// <summary>
    /// Android's own double-tap timeout, which is the window a second tap can arrive in.
    /// </summary>
    private const long ListSettleMilliseconds = 400;

    private void OnEntriesReloading(object? sender, EventArgs eventArgs) => _reloadingEntries = true;

    private void OnEntriesReloaded(object? sender, EventArgs eventArgs)
    {
        _reloadingEntries = false;
        ObserveTimestampPrecision();
        RestoreEntrySelection();
    }

    // Both view-model notifications are answered through the dispatcher, so a change raised
    // while the tab was alive can arrive after it has been closed — and a redraw then reads a
    // session that is being torn down. A closed session does not drive a view: the work is
    // dropped rather than performed against a corpse.
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsDisposed)
            {
                return;
            }

            switch (eventArgs.PropertyName)
            {
                case nameof(SessionTabViewModel.HeatMap):
                case nameof(SessionTabViewModel.Overview):
                case nameof(SessionTabViewModel.Viewport):
                    UpdateTimelines();
                    UpdateMarkerNavigation();
                    break;
                case nameof(SessionTabViewModel.SearchText):
                    _search.Text = _viewModel.SearchText;
                    break;
                case nameof(SessionTabViewModel.EntryOrder):
                    _order.SelectedIndex = _viewModel.EntryOrder == EntryOrder.SourceSequence ? 1 : 0;
                    break;
                case nameof(SessionTabViewModel.SearchResult):
                    _timeline.SetSearchResult(_viewModel.SearchResult);
                    UpdateMarkerNavigation();
                    break;
                case nameof(SessionTabViewModel.Status):
                    ApplyStatusText();
                    UpdateCaptureActions();
                    break;
                case nameof(SessionTabViewModel.Activity):
                case nameof(SessionTabViewModel.FailureReason):
                    UpdateCaptureActions();
                    AnnouncePartialRecovery();
                    break;
                case nameof(SessionTabViewModel.Completion):
                    UpdateSessionInfo();
                    AnnouncePartialRecovery();
                    break;
                case nameof(SessionTabViewModel.CaptureScopeRemedy):
                case nameof(SessionTabViewModel.CaptureScopeSummary):
                    // What the capture can see is settled several seconds in, so the empty
                    // plot's explanation and the session pane both have to be able to
                    // acquire it after the fact (audit 2, C1).
                    UpdateCaptureActions();
                    UpdateSessionInfo();
                    break;
                case nameof(SessionTabViewModel.CaptureHealthWarning):
                    // The status bar can only carry a marker pointing here, so the pane
                    // has to rebuild the moment the warning appears or clears rather than
                    // waiting for whatever refresh happens to come next.
                    UpdateSessionInfo();
                    break;
                case nameof(SessionTabViewModel.QueuedBehind):
                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.SearchStatus):
                case nameof(SessionTabViewModel.SearchInProgress):
                    _searchStatus.Text = _viewModel.SearchStatus;
                    UpdateMarkerNavigation();
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
    private void OnViewModelSnapshotChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsDisposed)
            {
                return;
            }

            UpdateTimelines();
        });

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


    /// <summary>
    /// Applies the query, and says why if it cannot be applied.
    /// </summary>
    /// <returns><c>true</c> when the query is now in force.</returns>
    private async Task<bool> ApplySearchAsync()
    {
        _viewModel.SearchText = _search.Text ?? string.Empty;
        var problem = await _viewModel.ApplySearchAsync(_regex.IsChecked == true, _caseSensitive.IsChecked == true);
        ShowSearchPatternProblem(problem);
        return problem is null;
    }

    /// <summary>
    /// Marks the query field, or clears the mark, and says the reason beside it.
    /// </summary>
    /// <remarks>
    /// Nothing else changes: a rejected pattern leaves the previous result, the chip bar and
    /// the status line exactly as they were, because none of them has stopped being true
    /// (finding F-04). The message is assertive because it appears in response to something
    /// the reader just did, and it is on the field rather than on the status line so that a
    /// screen reader hears it while the field still has focus.
    /// </remarks>
    private void ShowSearchPatternProblem(SearchPatternProblem? problem)
    {
        if (problem is not { } invalid)
        {
            _searchProblem.IsVisible = false;
            _searchProblem.Text = string.Empty;
            AutomationProperties.SetHelpText(_searchProblem, null);
            _search.Classes.Remove("invalid");
            _search.BorderBrush = null;
            return;
        }

        var sentence = invalid.Sentence;
        _searchProblem.Text = sentence;
        _searchProblem.Foreground = new SolidColorBrush(LevelPalette.InkOf(LogLevel.Error, ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light));
        _searchProblem.IsVisible = true;
        AutomationProperties.SetLiveSetting(_searchProblem, AutomationLiveSetting.Assertive);
        AutomationProperties.SetName(_searchProblem, sentence);
        _search.Classes.Add("invalid");
        _search.BorderBrush = _searchProblem.Foreground;
        AutomationProperties.SetHelpText(_search, sentence);
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

    /// <summary>Applies the query inside the same failure guard every other action uses.</summary>
    private async Task<bool> ApplySearchGuardedAsync()
    {
        var applied = false;
        await RunUiActionAsync(async () => applied = await ApplySearchAsync());
        return applied;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            _viewModel.ReportTransientStatus("Cancelled");
        }
        catch (Exception exception)
        {
            _viewModel.ReportTransientStatus(FriendlyActionFailure(exception));
        }
    }

    /// <summary>
    /// What an action that threw says on the status line.
    /// </summary>
    /// <remarks>
    /// This used to interpolate <c>exception.GetBaseException().Message</c>. In a Release
    /// Android build the .NET SDK trims framework resource strings, so that message is the
    /// resource key and its arguments rather than a sentence — the status line read
    /// <c>Failed · MakeException, (unclosed, 9, InsufficientClosingParentheses</c> on the
    /// device and the ordinary sentence in every test (finding F-04). A framework sentence
    /// would not be product language either, so the message is composed here, and the raw
    /// exception goes to the diagnostic bundle where it is useful.
    /// </remarks>
    private static string FriendlyActionFailure(Exception exception)
    {
        var cause = exception.GetBaseException();
        WorkspaceViewModel.RecordFailure("workspace.action.failed", cause);
        return $"Failed · {WorkspaceViewModel.FriendlyMessage(cause)}";
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
            _viewModel.ReportTransientStatus(FriendlyActionFailure(exception));
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

    /// <summary>The width the footer label may draw in, unbounded until the band is arranged.</summary>
    private double _loadMoreRoom = double.PositiveInfinity;

    /// <summary>The footer band's own height, remembered while the band is not shown.</summary>
    private double _entryFooterBand;

    /// <summary>
    /// Drops the load-more band when the pane cannot seat it and a row of log together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry panel's rows are Auto/star/Auto, and Avalonia gives an Auto row its desired
    /// height even when the grid has less to give. On a 136 dp analysis pane — a 393 dp
    /// landscape phone at a 1.55× text scale — the tab strip, the action row and this band
    /// add up to 144 dp, and the band was drawn cut through its own middle, across the status
    /// line below it (A-07). A list whose last row is partly visible is ordinary; a button
    /// drawn through its middle is not, and the same overrun is what F-32 was about.
    /// </para>
    /// <para>
    /// Hysteresis, because hiding the band is itself what makes room for it: the band returns
    /// only when the list can hold a row <em>and</em> the band, so the two states cannot
    /// alternate on consecutive layout passes.
    /// </para>
    /// </remarks>
    private void EnforceLoadMoreFooterFit()
    {
        if (!_mobile ||
            _entryFooter is not { } footer ||
            footer.Child is null ||
            !_loadMore.IsVisible ||
            _analysisGrid is not { Bounds.Height: > 0 })
        {
            return;
        }

        if (footer.IsVisible)
        {
            if (footer.Bounds.Height > 0)
            {
                _entryFooterBand = footer.Bounds.Height;
            }

            if (_entries.Bounds.Height < _entryRowMinimumHeight)
            {
                footer.IsVisible = false;
            }

            return;
        }

        if (_entryFooterBand > 0 &&
            _entries.Bounds.Height >= _entryRowMinimumHeight + _entryFooterBand)
        {
            footer.IsVisible = true;
        }
    }

    /// <summary>
    /// The most the footer's load-more label can say in the band it was given.
    /// </summary>
    /// <remarks>
    /// Under the list the control stretches across the analysis pane, so its width is the
    /// pane's rather than its own content's — and at a large text scale the sentence outgrew
    /// it and was clipped mid-glyph: a 393 dp phone at 1.55× drew
    /// <c>Load 500 more · 49,656 remainir</c>, with no ellipsis and no sign that anything was
    /// missing (A-05). Same rule as everywhere else a label has to live in a leftover width:
    /// give up the remaining count whole, then fall back to the compact form the header row
    /// already uses.
    /// </remarks>
    private string NarrowestLoadMoreThatFits(string fullLabel, string shortLabel, bool loading)
    {
        if (loading)
        {
            return fullLabel;
        }

        var trunk = fullLabel.Split(" · ", StringSplitOptions.None)[0];
        return NarrowestThatFits([fullLabel, trunk, shortLabel], _loadMoreRoom, MeasureLoadMoreWidth);
    }

    /// <summary>How wide a footer label would draw, in the face and size it will draw in.</summary>
    private double MeasureLoadMoreWidth(string text) =>
        new FormattedText(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            new Typeface(_loadMore.FontFamily, _loadMore.FontStyle, _loadMore.FontWeight),
            _loadMore.FontSize,
            Brushes.White).WidthIncludingTrailingWhitespace;

    /// <summary>
    /// Re-resolves the footer label when the band it lives in changes width.
    /// </summary>
    /// <remarks>
    /// The control stretches in the footer, so its width is imposed by the pane and does not
    /// answer its own content — which is what makes re-resolving on layout safe rather than a
    /// loop. In the header row it sizes to itself and already carries the compact label, so
    /// nothing here applies.
    /// </remarks>
    private void TrackLoadMoreRoom()
    {
        if (_loadMoreInHeader != false)
        {
            return;
        }

        var width = _loadMore.Bounds.Width;
        var room = double.IsFinite(width) && width > 0
            ? Math.Max(
                0,
                width
                - _loadMore.Padding.Left - _loadMore.Padding.Right
                - _loadMore.BorderThickness.Left - _loadMore.BorderThickness.Right)
            : double.PositiveInfinity;
        if (Math.Abs(room - _loadMoreRoom) <= 0.5)
        {
            return;
        }

        _loadMoreRoom = room;
        UpdateEntryLoadControls();
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
        if (_entryFooter is { } footer)
        {
            // The footer's frame is only worth a band while it is holding the control; in the
            // short composition the control has moved into the header row (see MoveLoadMore).
            // Whether the pane can seat the band at all is EnforceLoadMoreFooterFit's, and it
            // answers on the next layout pass, so this must not assert a band it can't have.
            footer.IsVisible = _loadMore.IsVisible &&
                               footer.Child is not null &&
                               (footer.IsVisible || _entryFooterBand <= 0 || !_mobile ||
                                _entries.Bounds.Height >= _entryRowMinimumHeight + _entryFooterBand);

            // The footer sits at the end of what is loaded, so it says how far the end is:
            // "Load next 500" beside 59 640 unread rows answers a question nobody asked.
            // Two lengths, because it has two homes. Under the list it is a full-width band
            // and can say the whole sentence; in the short composition it shares one row with
            // the count line and three other controls, and a 170 dp label there is what pushes
            // the row past the edge of a Split-mode column (audit 3, D2).
            var fullLabel = loading
                ? "Loading…"
                : remaining > 0
                    ? $"Load {Math.Min(remaining, SessionTabViewModel.EntryPageSize):N0} more; {remaining:N0} remaining"
                    : $"Load next {SessionTabViewModel.EntryPageSize:N0}";
            // The screen reader's sentence and the button's label are the same words with a
            // different separator, and swapping the character in place left `more· 49,656`
            // — the semicolon's own spacing, on a mark that carries its own (F-42). Every
            // other separator in the product is ` · `.
            var shortLabel = $"+{Math.Min(Math.Max(remaining, 1), SessionTabViewModel.EntryPageSize):N0}";
            _loadMore.Content = _loadMoreInHeader == true && !loading
                ? shortLabel
                : NarrowestLoadMoreThatFits(
                    fullLabel.Replace("; ", " · ", StringComparison.Ordinal),
                    shortLabel,
                    loading);
            AutomationProperties.SetName(_loadMore, fullLabel);
            AutomationProperties.SetHelpText(_loadMore, fullLabel);
            ToolTip.SetTip(_loadMore, fullLabel);
        }

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
                SetSelectedEntryOffPage(false);
                return;
            }
        }

        // In Follow, a refresh that no longer returns the inspected identity has moved the
        // live window/page past it. `CanLoadMore` can still be true because the new 30-second
        // window itself contains more than 500 rows; treating that as "the old row may be on
        // the next page" kept the inspector alive but hid the off-page explanation and its
        // Show-it route on busy captures (Samsung F-25 recheck).
        if (_viewModel.FollowLatest && _viewModel.IsLiveCaptureActive)
        {
            SetSelectedEntryOffPage(true);
            return;
        }

        // Outside a moving live window, rows remaining unloaded really can mean the entry is
        // simply further down the current result page.
        if (_viewModel.CanLoadMore)
        {
            return;
        }

        // A complete page that does not contain it means one of two very different things.
        // The filter has changed and now excludes the record — in which case the plot's caret
        // would mark a row the table cannot show, in a lane whose own header reads zero
        // (finding 28) — or the record is exactly as it was and the window it was in has moved
        // on, which is what a live capture does every second and is not a deselection
        // (finding F-25).
        if (!string.Equals(_selectionFilterFingerprint, _viewModel.Filter.Fingerprint(), StringComparison.Ordinal))
        {
            ClearEntrySelection();
            return;
        }

        // Kept: the inspector goes on showing the message and the source bytes, the caret goes
        // on marking where the entry is, and the pane says the row is no longer on screen and
        // offers the way back to it.
        SetSelectedEntryOffPage(true);
    }

    /// <summary>Drops the inspected entry, its caret and its actions, together.</summary>
    private void ClearEntrySelection()
    {
        _selectedEntryId = null;
        _selectionFilterFingerprint = null;
        _selectedEntryInstant = null;
        SetSelectedEntryOffPage(false);
        SetInspectedEntry(null);
        _timeline.SetSelectedEntry(null, null);
    }

    /// <summary>
    /// Says whether the inspected entry is still among the rows on screen, and offers the
    /// way back to it when it is not.
    /// </summary>
    private void SetSelectedEntryOffPage(bool offPage)
    {
        if (_selectedEntryOffPage == offPage && _entryOffPageBanner is not null)
        {
            if (_entryOffPageBanner.IsVisible == offPage)
            {
                return;
            }
        }

        _selectedEntryOffPage = offPage;
        if (_entryOffPageBanner is { } banner)
        {
            banner.IsVisible = offPage;
        }

        if (offPage && _entryOffPageText is { } text)
        {
            text.Text = _viewModel.FollowLatest
                ? "This entry has scrolled out of the live window. It is still open below."
                : "This entry is outside the rows on screen. It is still open below.";
            AutomationProperties.SetName(text, text.Text);
        }
    }

    /// <summary>
    /// Puts the live edge down and brings the inspected entry back into view.
    /// </summary>
    private async Task ShowInspectedEntryAgainAsync()
    {
        if (_selectedEntryInstant is not { } instant ||
            _viewModel.Viewport is not { } viewport ||
            _viewModel.Snapshot?.TimedRange is not { } session)
        {
            return;
        }

        // Following the live edge and holding a place in the past are opposite requests, and
        // the reader has just made the second one.
        if (_viewModel.FollowLatest)
        {
            await _viewModel.ToggleFollowAsync().ConfigureAwait(true);
        }

        var span = Math.Min(viewport.DurationUs, session.DurationUs);
        var maximumStart = session.EndExclusive.Value - span;
        var start = Math.Clamp(instant.Value - span / 2, session.StartInclusive.Value, maximumStart);
        await _viewModel.SetViewportAsync(
            new TimeRange(new InstantUs(start), new InstantUs(start + span))).ConfigureAwait(true);
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

        if (selected.Length == 0)
        {
            Notify("Select an entry first, then Copy raw.", failure: true);
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            Notify("This device did not offer a clipboard.", failure: true);
            return;
        }

        var text = await _viewModel.ReadRawEntriesAsync(selected);
        await clipboard.SetTextAsync(text);
        Notify($"Copied the raw text of {Counted.Entries(selected.Length)}.");
    }

}
