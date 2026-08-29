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
using VisualCat.Domain;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

public sealed partial class SessionWorkspaceView : UserControl
{
    /// <summary>Automation id of the inspector's message block.</summary>
    internal const string InspectorMessageId = "InspectorMessage";

    /// <summary>Automation id of the line describing which entry the inspector is showing.</summary>
    internal const string InspectorSelectionHintId = "InspectorSelectionHint";

    /// <summary>
    /// The entry inspector §14.9 asks for: "message first line in the table, full
    /// logical/raw content in an inspector". Only the raw half was ever built, so a
    /// message wider than one clipped row — every stack trace, and every message of a
    /// long-format capture, whose body lines are joined into one record — had no reachable
    /// form anywhere in the product.
    ///
    /// The pane shows exactly one of two things: the selected entry, or a card saying why
    /// there is no entry to show. Source bytes are a section inside the inspector with a
    /// status of their own, because the message is on the row's own record and is ready
    /// instantly, while the bytes need a file read — hiding the first behind the second is
    /// what made a tap look like it did nothing.
    /// </summary>
    private Control BuildEntryInspectorPane()
    {
        _rawContext.FontFamily = MonoFont;
        _rawContext.FontSize = TextScale.Of(12);
        _inspectMessage.FontFamily = MonoFont;
        _inspectMessage.FontSize = TextScale.Of(_mobile ? 13 : 12.5);
        var scroller = _rawScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            // Inside the mobile column the dump scrolls only sideways; the column owns the
            // vertical axis, so the two scrollers never compete for the same drag.
            VerticalScrollBarVisibility = _mobile ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            MaxHeight = _mobile ? double.PositiveInfinity : 132,
            Content = _rawContext,
        };
        _rawPlaceholder = new TextBlock
        {
            Text = "Select a row to read its whole message, then the exact source bytes behind it.",
            FontSize = TextScale.Of(11),
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
        };

        var inspector = BuildInspectorBody(scroller);
        _rawDataSurface = inspector;
        inspector.IsVisible = false;

        // A selected-entry column can be much taller than its tab. On mobile the tab is the
        // viewport, so neither drawing nor hit testing may escape it while the column scrolls.
        var content = new Grid { ClipToBounds = _mobile };
        if (_mobile)
        {
            _inspectScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = inspector,
                ClipToBounds = true,
            };
            // Which entry this is stays on screen while the column scrolls. Expanding SOURCE
            // CONTEXT scrolls the column 1,628 px to land on the selected source line —
            // which is right — and that took the entry's severity, tag, timestamp and
            // message off the top with it, leaving a dump with nothing saying what it was a
            // dump of (audit 3, D5).
            var pinned = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                ClipToBounds = true,
            };
            if (_inspectIdentity is { } identityLine)
            {
                pinned.Children.Add(identityLine);
            }

            Grid.SetRow(_inspectScroll, 1);
            pinned.Children.Add(_inspectScroll);
            _rawCodeSurface = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = pinned,
                IsVisible = false,
                ClipToBounds = true,
            };
            AutomationProperties.SetName(_rawCodeSurface, "Selected entry inspector");
            _rawDataSurface = _rawCodeSurface;
            inspector.IsVisible = true;
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
            AutomationProperties.SetName(chooseEntry, "Open Entries to choose a row");

            _rawPlaceholder.Text =
                "Choose a log entry to read its whole message and the source bytes behind it.";
            _rawPlaceholder.FontStyle = FontStyle.Normal;
            _rawPlaceholder.TextAlignment = TextAlignment.Center;
            _rawPlaceholder.MaxWidth = 320;
            _rawEmptyTitle = new TextBlock
            {
                Text = "No entry selected",
                FontSize = TextScale.Of(16),
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
                        FontSize = TextScale.Of(24),
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    _rawEmptyTitle,
                    _rawPlaceholder,
                    chooseEntry,
                },
            };
            // The card is as tall as the pane rather than as tall as its content, and the
            // content is centred inside it and scrolls when it cannot fit. Centring the card
            // itself let a panel taller than the pane be arranged at its full desired height
            // anyway, and a Border does not clip: "Choose an entry" was drawn half inside and
            // half outside, cutting through the rounded border (audit 2, D5).
            emptyPanel.VerticalAlignment = VerticalAlignment.Center;
            _rawEmptyCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18),
                Margin = new Thickness(14),
                MaxWidth = 390,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = new ScrollViewer
                {
                    Content = emptyPanel,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            };
            _rawEmptyState = _rawEmptyCard;
            AutomationProperties.SetName(_rawEmptyCard, "No entry selected");
            content.Children.Add(_rawEmptyCard);
        }
        else
        {
            _rawEmptyState = _rawPlaceholder;
            content.Children.Add(inspector);
            content.Children.Add(_rawPlaceholder);
        }

        _rawContentBorder = new Border
        {
            BorderThickness = new Thickness(_mobile ? 0 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = _mobile ? new Thickness(0) : new Thickness(8, 6),
            Child = content,
            ClipToBounds = _mobile,
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
            Text = "SELECTED ENTRY",
            FontSize = TextScale.Of(10),
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rawHeaderHint = new TextBlock
        {
            Text = "full message and the source bytes behind it",
            FontSize = TextScale.Of(10),
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
        ToolTip.SetTip(header, "Show or hide the whole message and the source bytes behind the selected row.");
        AutomationProperties.SetName(header, "Toggle the selected entry inspector");
        header.Click += (_, _) => SetRawExpanded(!_rawExpanded);

        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        pane.Children.Add(header);
        Grid.SetRow(_rawContentBorder, 1);
        pane.Children.Add(_rawContentBorder);
        SetRawExpanded(false);
        return pane;
    }

    /// <summary>
    /// Identity, then the whole message, then the source bytes. The order is the order the
    /// questions arrive in: which entry is this, what did it say, and what exactly was on
    /// the wire around it.
    /// </summary>
    private Grid BuildInspectorBody(ScrollViewer sourceScroller)
    {
        _rawSelectionHint = new TextBlock
        {
            FontSize = TextScale.Of(10),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(_rawSelectionHint, InspectorSelectionHintId);

        _inspectPillText = new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = TextScale.Of(11),
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#0A0F18")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _inspectPill = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _inspectPillText,
        };
        _inspectTag = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            FontSize = TextScale.Of(_mobile ? 13 : 12.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _inspectMeta = new TextBlock
        {
            FontSize = TextScale.Of(10.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var identity = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 0,
            LineSpacing = 3,
            Margin = new Thickness(0, 0, 0, 7),
            Children = { _inspectPill, _inspectTag, _inspectMeta },
        };
        AutomationProperties.SetName(identity, "Selected entry");
        _inspectIdentity = identity;

        // A phone pane cannot give two scrolling surfaces a fair share of one screen: in
        // Split mode the fixed controls between them consumed everything and the dump was
        // allocated nothing at all. On mobile the inspector is therefore one scrolling
        // column — the ordinary detail-page shape — and only the desktop pane, which is
        // height-capped inside the table, keeps a scroller per section.
        Control messageSurface = _inspectMessage;
        if (!_mobile)
        {
            messageSurface = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 150,
                Content = _inspectMessage,
            };
        }
        // The spoken name becomes the entry itself once one is selected, so the stable
        // handle for automation is the id rather than the name.
        AutomationProperties.SetAutomationId(_inspectMessage, InspectorMessageId);
        AutomationProperties.SetName(_inspectMessage, "Full message");

        _inspectTruncated = new TextBlock
        {
            FontSize = TextScale.Of(10),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0),
            IsVisible = false,
        };

        var copyMessage = _copyMessage = new Button
        {
            Content = "Copy message",
            MinHeight = _mobile ? 48 : 0,
            Margin = new Thickness(0, 7, 6, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(copyMessage, "Copy the whole message of the selected entry");
        AutomationProperties.SetName(copyMessage, "Copy the whole message");
        copyMessage.Click += async (_, _) => await RunUiActionAsync(CopyInspectedMessageAsync);

        var messageActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 0,
            LineSpacing = 6,
            Children = { copyMessage },
        };

        var sourceSection = BuildSourceSection(sourceScroller);

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
        };
        body.Children.Add(_rawSelectionHint);
        if (!_mobile)
        {
            // On a phone this line is pinned above the scroller instead; see the mobile
            // branch of BuildEntryInspectorPane.
            Grid.SetRow(identity, 1);
            body.Children.Add(identity);
        }

        Grid.SetRow(messageSurface, 2);
        body.Children.Add(messageSurface);
        Grid.SetRow(_inspectTruncated, 3);
        body.Children.Add(_inspectTruncated);
        Grid.SetRow(messageActions, 3);
        body.Children.Add(messageActions);
        Grid.SetRow(sourceSection, 4);
        body.Children.Add(sourceSection);
        SetSourceExpanded(!_mobile);
        return body;
    }

    /// <summary>
    /// The source dump, unchanged in what it shows and collapsible on a phone, where the
    /// message is what the reader came for and the bytes are the follow-up question.
    /// </summary>
    private Grid BuildSourceSection(ScrollViewer scroller)
    {
        _sourceChevron = new TextBlock { Text = "▾", Width = 13, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = "SOURCE CONTEXT",
            FontSize = TextScale.Of(10),
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The claim has to match what is drawn. Each line carries a line number and a
        // parse tag before the file's own text, so "exact bytes" was wrong about the line and
        // right only about the part after the divider (finding 15a). The number is 1-based, so
        // it is the same line `sed -n Np`, `grep -n` and an editor's Go to line would name
        // (finding F-08).
        //
        // The glyph is named rather than shown. While the section is collapsed the divider is
        // not on screen, so the caption ended on a bare vertical bar and read as a sentence
        // that had been cut off — a rendering fault, on the one control whose whole job is to
        // look trustworthy about raw bytes (audit 3, E3).
        var hint = new TextBlock
        {
            Text = _mobile
                ? "exact bytes, after the │ divider"
                : "exact bytes after the │ divider, with the lines around them",
            FontSize = TextScale.Of(10),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(
            hint,
            "Each line shows its line number in the file and how the parser read it, then the "
            + "file's own bytes after the divider.");
        var header = _sourceHeader = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4),
            MinHeight = TouchTarget.For(_mobile),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _sourceChevron, label, hint },
            },
        };
        header.Click += (_, _) => SetSourceExpanded(!_sourceExpanded);

        _sourceStatus = new TextBlock
        {
            FontSize = TextScale.Of(11),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
            IsVisible = false,
        };

        // The floor under every source read. A read that is superseded, or that outlives the
        // pane it was started for, used to leave "Reading the source bytes around this
        // entry…" on screen with no timeout, no error and no way to ask again — the feature
        // the tool is trusted for, failing silently and indistinguishably from a slow disk
        // (finding 5). The state always resolves now, and when it resolves badly this is how
        // the reader asks again.
        var retry = _rawRetry = new Button
        {
            Content = "Retry",
            MinHeight = _mobile ? 48 : 0,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
        };
        ToolTip.SetTip(retry, "Read this entry's source bytes again");
        AutomationProperties.SetName(retry, "Read the source bytes again");
        retry.Click += (_, _) => RetryRawContextLoad();

        Control tools;
        if (_mobile)
        {
            // A segmented pair, not one button that both shows a mode and switches it. Its
            // neighbour "Wrap ✓" names the state the reader is in while this one named the
            // action a tap performs, so two adjacent controls disagreed about what their own
            // labels meant (finding 21.3). This is the same Plot/Split/Details idiom the
            // workspace already uses: both modes are on screen, the current one is lit, and
            // a tap selects rather than flips.
            var panToggle = _rawPanToggle = RawModeSegment("Scroll", 0, pan: true);
            var selectToggle = _rawSelectToggle = RawModeSegment("Select", 2, pan: false);
            var modeSelector = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                Children = { panToggle, selectToggle },
            };
            Grid.SetColumn(selectToggle, 1);
            AutomationProperties.SetName(modeSelector, "What a drag over the source does");

            // What the mode currently is, in words, on screen. It was a ToolTip — which a
            // touch device never shows — so the only clue was a button labelled with the
            // name of a mode, and the first natural "scroll the trace" swipe selected a
            // block of source text instead of scrolling it (finding 15).
            var panState = _rawPanState = new TextBlock
            {
                FontSize = TextScale.Of(10.5),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(1, 0, 0, 4),
            };

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
                Width = TouchTarget.MinimumWithEdgeReserve,
            };
            panLeft.Click += (_, _) => PanRawContext(-1);
            ToolTip.SetTip(panLeft, "Pan source left by one page");
            AutomationProperties.SetName(panLeft, "Pan source left by one page");

            var panRight = _rawPanRight = new Button
            {
                Content = "→",
                MinHeight = 48,
                Width = TouchTarget.MinimumWithEdgeReserve,
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

            var sourceButtons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 4,
                LineSpacing = 6,
                Children =
                {
                    modeSelector,
                    wrapToggle,
                    panLeft,
                    panRight,
                    copySelection,
                },
            };
            var sourceTools = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(0, 0, 0, 7),
                Children = { sourceButtons, panState },
            };
            AutomationProperties.SetName(sourceTools, "Source navigation and selection controls");
            tools = sourceTools;
            scroller.ScrollChanged += (_, _) => UpdateRawNavigationButtons();
            _rawContext.PointerReleased += (_, _) => Dispatcher.UIThread.Post(CompleteRawTextSelection);

            // Pan first on a touch screen: the swipe a reader arrives with is a scroll, and
            // selecting text is the deliberate second act.
            SetRawPanMode(true);
            SetRawWrap(false);
        }
        else
        {
            tools = new Panel { IsVisible = false };
        }

        _rawSourceTools = tools;
        var section = new Grid
        {
            RowDefinitions = new RowDefinitions(_mobile ? "Auto,Auto,Auto,Auto,*" : "Auto,Auto,Auto,Auto,Auto"),
        };
        section.Children.Add(header);
        Grid.SetRow(_sourceStatus, 1);
        section.Children.Add(_sourceStatus);
        Grid.SetRow(retry, 2);
        section.Children.Add(retry);
        Grid.SetRow(tools, 3);
        section.Children.Add(tools);
        Grid.SetRow(scroller, 4);
        section.Children.Add(scroller);
        return section;
    }

    /// <summary>
    /// Opens or closes the source section. On a phone the message and the bytes compete
    /// for one screen, so closing the bytes gives the message all of it.
    /// </summary>
    private void SetSourceExpanded(bool expanded)
    {
        _sourceExpanded = expanded;
        if (_sourceChevron is { } chevron)
        {
            chevron.Text = expanded ? "▾" : "▸";
        }

        if (_sourceStatus is { } status)
        {
            status.IsVisible = expanded && status.Text is { Length: > 0 };
        }

        if (_rawRetry is { } retry)
        {
            retry.IsVisible = expanded &&
                              !_rawLoadPending &&
                              _sourceStatus?.Text is { Length: > 0 } &&
                              _inspectedEntry is not null;
        }

        if (_rawSourceTools is { } tools)
        {
            tools.IsVisible = expanded && _mobile && _sourceStatus?.Text is not { Length: > 0 };
        }

        if (_rawScroller is { } scroller)
        {
            scroller.IsVisible = expanded && _sourceStatus?.Text is not { Length: > 0 };
        }

        if (_sourceHeader is { } header)
        {
            AutomationProperties.SetName(
                header,
                expanded ? "Hide the source bytes" : "Show the source bytes");
        }

        // The context is loaded once and the section may open long afterwards, so the line
        // the reader is looking for is put on screen here as well as on load.
        if (expanded)
        {
            ScrollMarkedLineIntoView();
        }
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

    /// <summary>
    /// Presents an entry the moment it is picked. Everything here comes off the record the
    /// row is already bound to, so it costs no query and cannot be outrun by a live
    /// snapshot; the source read that follows is the only asynchronous part.
    /// </summary>
    private void SetInspectedEntry(NormalizedEntry? entry)
    {
        _inspectedEntry = entry;
        if (_openInspector is { } open)
        {
            open.IsEnabled = entry is not null;
        }

        if (_copyMessage is { } copy)
        {
            copy.IsEnabled = entry is not null;
        }

        if (entry is null)
        {
            _inspectMessage.Inlines?.Clear();
            _inspectMessage.Text = string.Empty;
            _presentedRawText = string.Empty;
            _rawContext.Inlines?.Clear();
            _rawContext.Text = string.Empty;
            ShowNoEntryState();
            return;
        }

        if (_inspectPill is { } pill)
        {
            pill.Background = LevelPalette.BrushOf(entry.Level);
        }

        if (_inspectPillText is { } pillText)
        {
            pillText.Text = LevelPalette.Label(entry.Level);
        }

        if (_inspectTag is { } tag)
        {
            tag.Text = entry.Tag;
            tag.Foreground = LevelPalette.InkBrushOf(entry.Level, ActualThemeVariant != ThemeVariant.Light);
        }

        if (_inspectMeta is { } meta)
        {
            // A capture without buffer names would otherwise render an empty slot between
            // two separators, which reads as a missing value rather than an absent field.
            var parts = new List<string>(4)
            {
                FormatInstant(entry.Timestamp),
                $"{ProcessLabel(entry)}:{entry.Tid}",
            };
            if (!string.IsNullOrWhiteSpace(entry.Buffer))
            {
                parts.Add(entry.Buffer);
            }

            parts.Add($"tpl {entry.TemplateId}");
            meta.Text = "·  " + string.Join("  ·  ", parts);
        }

        var capped = entry.Message.Length > InspectorMessageLimit;
        var shown = capped ? entry.Message[..InspectorMessageLimit] : entry.Message;
        ApplyHighlight(_inspectMessage, shown, _viewModel.Filter.Search);
        _inspectMessage.IsVisible = true;
        if (_inspectTruncated is { } truncated)
        {
            truncated.IsVisible = capped;
            truncated.Text = capped
                ? $"Showing the first {InspectorMessageLimit / 1024:N0} KB of {entry.Message.Length:N0} " +
                  "characters. Copy message copies the whole record."
                : string.Empty;
        }

        // A screen reader should hear the entry, not a megabyte of it.
        AutomationProperties.SetName(
            _inspectMessage,
            $"Message of {entry.Level} {entry.Tag}: {RowPreview(entry.Message)}");
        UpdateSelectionHint();
        UpdateInspectorVisibility();
    }

    /// <summary>
    /// States what the inspected entry is, from the selection as it stands now.
    /// </summary>
    /// <remarks>
    /// This line was written once, when a load began, from the cell count that load carried —
    /// so it went on reading "First of 27 in the selected bar · choose another row in Entries"
    /// after the bar had been released and the row had been picked straight from the table,
    /// quoting a count that belonged to a scope that no longer existed (finding 6). Everything
    /// here is read at the moment it is rendered.
    /// </remarks>
    private void UpdateSelectionHint()
    {
        if (_rawSelectionHint is not { } hint)
        {
            return;
        }

        if (_viewModel.DetailRange is null || _viewModel.MatchesInView is not { } inCell || inCell <= 1)
        {
            hint.Text = "Selected entry";
            return;
        }

        var position = IndexOfInspectedEntry();
        hint.Text = position >= 0
            ? $"Row {position + 1:N0} of {inCell:N0} in the selected bar · choose another row in Entries"
            : $"One of {inCell:N0} in the selected bar · choose another row in Entries";
    }

    /// <summary>Position of the inspected entry among the loaded rows, or -1.</summary>
    private int IndexOfInspectedEntry()
    {
        if (_inspectedEntry is not { } entry)
        {
            return -1;
        }

        for (var index = 0; index < _viewModel.Entries.Count; index++)
        {
            if (_viewModel.Entries[index].EntryId == entry.EntryId)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task CopyInspectedMessageAsync()
    {
        if (_inspectedEntry is not { } entry || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(entry.Message);
        _viewModel.ReportTransientStatus($"Copied {entry.Message.Length:N0} characters");
        Notify($"Copied {entry.Message.Length:N0} characters of this entry.");
    }

    /// <summary>
    /// The pane shows the inspector whenever there is an entry to inspect, and the empty
    /// card otherwise. A pending source read is a state of the source section, never a
    /// reason to hide a message that is already in hand.
    /// </summary>
    private void UpdateInspectorVisibility()
    {
        var hasEntry = _inspectedEntry is not null;
        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = !hasEntry;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = hasEntry;
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

    /// <summary>
    /// Switches what a drag over the source does. The button is labelled with the action it
    /// performs rather than with the mode it is in, and the mode itself is stated beside it.
    /// </summary>
    /// <summary>One half of the source drag-mode selector.</summary>
    private Button RawModeSegment(string label, int corner, bool pan)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 48,
            MinWidth = 74,
            Padding = new Thickness(8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = corner == 0 ? new CornerRadius(7, 0, 0, 7) : new CornerRadius(0, 7, 7, 0),
        };
        AutomationProperties.SetName(
            button,
            pan ? "Drag to scroll the source" : "Drag to select source text");
        button.Click += (_, _) => SetRawPanMode(pan);
        return button;
    }

    private void SetRawPanMode(bool pan)
    {
        _rawPanMode = pan;
        _rawContext.IsHitTestVisible = !pan;
        if (_rawPanState is { } panState)
        {
            panState.Text = pan
                ? "Dragging scrolls the source."
                : "Dragging selects text; it returns to scrolling once you copy or lift.";
        }

        if (_rawPanToggle is { } panToggle)
        {
            ApplyMobileChoiceAppearance(panToggle, selected: pan);
            panToggle.FontWeight = pan ? FontWeight.SemiBold : FontWeight.Normal;
        }

        if (_rawSelectToggle is { } selectToggle)
        {
            ApplyMobileChoiceAppearance(selectToggle, selected: !pan);
            selectToggle.FontWeight = pan ? FontWeight.Normal : FontWeight.SemiBold;
        }
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
        if (!string.Equals(_presentedRawText, raw, StringComparison.Ordinal))
        {
            _presentedRawText = raw;
            _rawContext.ClearSelection();
            ApplyRawContextText();
            if (_rawCopySelection is { } copySelection)
            {
                copySelection.IsEnabled = false;
            }
        }

        if (string.IsNullOrEmpty(raw))
        {
            ShowRawUnavailableState();
            return;
        }

        SetSourceStatus(null);
        ScrollMarkedLineIntoView();

        // Selecting a row is the request to read it, so the panel opens itself the moment
        // content arrives; the user can still collapse it.
        if (!_mobile && !_rawExpanded)
        {
            SetRawExpanded(true);
        }
    }

    /// <summary>
    /// Draws the dump with the entry's own line in its severity color and bold, and every
    /// surrounding line muted. The <c>▶</c> the text already carried was a single character
    /// that vanished the moment the lines wrapped; color and weight survive wrapping, and
    /// three runs keep selection and copy working across the whole block.
    /// </summary>
    private void ApplyRawContextText()
    {
        var raw = _presentedRawText;
        var marker = _viewModel.RawContextMarker;
        var dark = ActualThemeVariant != ThemeVariant.Light;
        if (marker is not { } mark ||
            mark.Offset < 0 ||
            mark.Length <= 0 ||
            mark.Offset + mark.Length > raw.Length)
        {
            _rawContext.Inlines?.Clear();
            _rawContext.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
            _rawContext.Text = raw;
            return;
        }

        var muted = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        var inlines = new InlineCollection();
        if (mark.Offset > 0)
        {
            inlines.Add(new Run(raw[..mark.Offset]) { Foreground = muted });
        }

        inlines.Add(new Run(raw.Substring(mark.Offset, mark.Length))
        {
            Foreground = LevelPalette.InkBrushOf(_inspectedEntry?.Level ?? LogLevel.Unknown, dark),
            FontWeight = FontWeight.Bold,
        });
        var end = mark.Offset + mark.Length;
        if (end < raw.Length)
        {
            inlines.Add(new Run(raw[end..]) { Foreground = muted });
        }

        _rawContext.Text = string.Empty;
        _rawContext.Inlines = inlines;
    }

    /// <summary>
    /// Puts the entry's own line on screen. Hit-testing the laid-out text is what makes
    /// this exact under wrapping, where a line index would not be, and it has to wait for
    /// layout to have run over the new text.
    /// </summary>
    private void ScrollMarkedLineIntoView()
    {
        if (_viewModel.RawContextMarker is not { } marker)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                // A hidden scroller has no extent to scroll within, so the offset would
                // clamp to zero and the line would stay lost.
                if (_rawScroller is not { IsVisible: true })
                {
                    return;
                }

                // Whoever owns the vertical axis does the scrolling: the surrounding column
                // on mobile, the dump's own scroller on the desktop pane.
                var scroller = _inspectScroll ?? _rawScroller;
                var y = _rawContext.TextLayout.HitTestTextPosition(marker.Offset).Y;
                if (scroller.Content is Control target &&
                    _rawContext.TranslatePoint(new Point(0, y), target) is { } point)
                {
                    y = point.Y;
                }

                // A margin above the line, so the entry's own line lands inside the view with
                // the lines that follow it visible. Eight pixels put it flush against the
                // bottom edge on first open, half-clipped, with no following context at all
                // until the reader scrolled (finding 28).
                var margin = Math.Clamp(scroller.Viewport.Height * 0.3, 12, 160);
                scroller.Offset = new Vector(
                    scroller.Offset.X,
                    Math.Clamp(y - margin, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>How long a source read may sit on "reading…" before it is called a failure.</summary>
    private static readonly TimeSpan RawLoadTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Source bytes have their own status so a slow or failed read never removes
    /// the message from the screen.</summary>
    /// <param name="status">The line to show, or <c>null</c> once bytes are on screen.</param>
    /// <param name="loading">
    /// Whether this status describes a read still in flight. A read in flight arms the
    /// watchdog; anything else disarms it and, when it is a failure, offers Retry.
    /// </param>
    private void SetSourceStatus(string? status, bool loading = false)
    {
        var pending = status is { Length: > 0 };
        if (_sourceStatus is { } label)
        {
            label.Text = status ?? string.Empty;
            label.IsVisible = pending && _sourceExpanded;
        }

        if (_rawScroller is { } scroller)
        {
            scroller.IsVisible = !pending && _sourceExpanded;
        }

        if (_rawSourceTools is { } tools)
        {
            tools.IsVisible = !pending && _sourceExpanded && _mobile;
        }

        _rawLoadPending = pending && loading;
        if (_rawRetry is { } retry)
        {
            retry.IsVisible = pending && !loading && _sourceExpanded && _inspectedEntry is not null;
        }

        if (_rawLoadPending)
        {
            ArmRawLoadWatchdog();
        }
        else
        {
            DisarmRawLoadWatchdog();
        }
    }

    private void ArmRawLoadWatchdog()
    {
        _rawLoadWatchdog ??= new DispatcherTimer(
            RawLoadTimeout,
            DispatcherPriority.Background,
            (_, _) => HandleRawLoadTimeout());
        _rawLoadWatchdog.Stop();
        _rawLoadWatchdog.Start();
    }

    private void DisarmRawLoadWatchdog() => _rawLoadWatchdog?.Stop();

    /// <summary>
    /// Turns a read that never answered into a stated failure. Whatever the underlying race
    /// is — a superseded load whose result was dropped, a pane that was never attached when
    /// the read began — the reader is told, and can ask again.
    /// </summary>
    private void HandleRawLoadTimeout()
    {
        DisarmRawLoadWatchdog();
        if (!_rawLoadPending)
        {
            return;
        }

        CancelRawContextLoad();
        _timelineEntryPending = false;
        SetSourceStatus(
            $"Reading this entry's source bytes took longer than {RawLoadTimeout.TotalSeconds:N0} seconds " +
            "and was stopped. The message above is complete.");
        if (_inspectedEntry is null)
        {
            if (_rawEmptyTitle is { } title)
            {
                title.Text = "Source read timed out";
            }

            if (_rawPlaceholder is { } description)
            {
                description.Text = "Choose a row in Entries to read it again.";
            }

            if (_rawChooseEntry is { } chooseEntry)
            {
                chooseEntry.IsVisible = true;
            }

            UpdateInspectorVisibility();
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source read timed out");
        }
    }

    /// <summary>
    /// Resolves a "reading…" state whose read was abandoned before it could answer, so the
    /// pane never outlives the work it is describing.
    /// </summary>
    private void ResolveInterruptedRawLoad()
    {
        if (!_rawLoadPending || HasRawContextLoad())
        {
            return;
        }

        if (_inspectedEntry is null)
        {
            SetSourceStatus(null);
            ShowNoEntryState();
            return;
        }

        SetSourceStatus("Reading this entry's source bytes was interrupted.");
    }

    /// <summary>Reads the selected entry's source bytes again after a failure.</summary>
    private void RetryRawContextLoad()
    {
        if ((_inspectedEntry ?? _rawLoadEntry) is { } entry)
        {
            BeginRawContextLoad(entry, _rawLoadTimelineCount);
        }
    }

    private void ShowNoEntryState()
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "No entry selected";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = _mobile
                ? "Choose a log entry to read its whole message and the source bytes behind it."
                : "Select a row to read its whole message, then the exact source bytes behind it.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "No entry selected");
        }

        UpdateInspectorVisibility();
    }

    private void ShowRawUnavailableState()
    {
        SetSourceStatus("No source context was returned for this entry. Choose it again to retry.");
        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source context unavailable");
        }
    }

    private void ShowRawLoadingState(long? timelineCount = null)
    {
        UpdateSelectionHint();
        SetSourceStatus("Reading the source bytes around this entry…", loading: true);

        // Before the cell query answers there is no entry yet, so the card explains the
        // wait; once one is selected the message is already on screen and only the source
        // section is still loading.
        if (_inspectedEntry is not null)
        {
            UpdateInspectorVisibility();
            return;
        }

        if (_rawEmptyTitle is { } title)
        {
            title.Text = timelineCount is > 0 ? "Loading first entry…" : "Loading entry…";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = timelineCount is > 0
                ? $"Reading the first of {Counted.Entries(timelineCount.Value)} in the selected timeline bar."
                : "Reading the selected entry.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(
                emptyCard,
                timelineCount is > 0
                    ? "Loading first entry from selected timeline bar"
                    : "Loading the selected entry");
        }

        UpdateInspectorVisibility();
    }

    private void ShowRawNoMatchesState()
    {
        SetInspectedEntry(null);
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

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "No matching entries in selected timeline bar");
        }

        UpdateInspectorVisibility();
    }

    private void ShowRawErrorState(Exception exception)
    {
        SetSourceStatus("VisualCat could not read this entry's source context. Choose it again to retry.");
        if (_inspectedEntry is null)
        {
            if (_rawEmptyTitle is { } title)
            {
                title.Text = "Source unavailable";
            }

            if (_rawPlaceholder is { } description)
            {
                description.Text = "VisualCat could not read this entry. Choose another row to retry.";
            }

            if (_rawChooseEntry is { } chooseEntry)
            {
                chooseEntry.IsVisible = true;
            }

            UpdateInspectorVisibility();
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source context unavailable");
        }

        _viewModel.ReportTransientStatus($"Source unavailable · {WorkspaceViewModel.FriendlyMessage(exception)}");
    }
}
