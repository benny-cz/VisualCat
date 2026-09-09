using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VisualCat.App.Presentation;
using VisualCat.Domain;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;

namespace VisualCat.App.Views;

/// <summary>Reviews the exact frozen CSV query and its per-export output options.</summary>
/// <remarks>
/// The dialog owns its own counting rather than being handed a finished list, so the reader
/// sees the review immediately, sees <c>Calculating rows…</c> while it settles, and gets a
/// Retry that recounts the same frozen request against the same owned snapshot instead of a
/// failure that closes everything. Choices are published together: completion order must not
/// decide which scope is selected.
/// </remarks>
internal sealed class ExportReviewDialog : DialogBody<ExportDecision>, IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ResolvedExportScope>>> _countAsync;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _scopeOptions = new() { Spacing = 6 };
    private readonly List<(RadioButton Button, ResolvedExportScope Scope)> _buttons = [];
    private readonly Button _retry = new() { Content = "Retry", IsVisible = false };
    private readonly bool _mobile = DialogComposition.Mobile;
    private Task? _counting;
    private Task? _releaseTask;
    private int _disposeRequested;

    /// <param name="request">The frozen export, whose applied filter this reviews.</param>
    /// <param name="countAsync">Counts each offered scope against the owned snapshot.</param>
    /// <param name="displayedTimeZone">The zone the reader's times are shown in.</param>
    /// <param name="hasOffTimelineLines">Whether the session holds records CSV cannot carry.</param>
    /// <param name="templateName">
    /// Resolves an active template filter to its canonical text. Omitted only where no
    /// snapshot is available; the review then falls back to the same <c>Template n</c>
    /// wording the workspace uses for an ID with no definition.
    /// </param>
    internal ExportReviewDialog(
        FrozenExportRequest request,
        Func<CancellationToken, Task<IReadOnlyList<ResolvedExportScope>>> countAsync,
        string displayedTimeZone,
        bool hasOffTimelineLines,
        Func<uint, string>? templateName = null)
        : base("Export CSV")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(countAsync);
        _countAsync = countAsync;
        var nameTemplate = templateName ?? TemplateNames.Fallback;
        PreferredSize = new Size(590, 560);
        MinimumSize = _mobile ? new Size(300, 340) : new Size(390, 390);
        ScrollsInternally = true;
        var mobile = _mobile;
        var scopeOptions = _scopeOptions;
        var buttons = _buttons;
        IReadOnlyList<ResolvedExportScope> scopes = [];

        // The only scope there is, once it has a row to write. One choice is not a question,
        // so it is stated rather than offered — but an empty one leaves this null, because the
        // empty-scope explanation below is what the reader needs then, not a summary of nothing.
        ResolvedExportScope? only = null;

        var rowOrder = new ComboBox
        {
            MinHeight = mobile ? 48 : 0,
            ItemsSource = new[] { "Source order", "Chronological" },
            SelectedIndex = request.DefaultOrder == EntryOrder.SourceSequence ? 0 : 1,
        };
        AutomationProperties.SetName(rowOrder, "Row order");
        var encoding = new ComboBox
        {
            MinHeight = mobile ? 48 : 0,
            ItemsSource = new[] { "UTF-8", "UTF-8 with byte-order mark" },
            SelectedIndex = request.DefaultIncludeUtf8Bom ? 1 : 0,
        };
        AutomationProperties.SetName(encoding, "Encoding");
        var filterDetail = new ContentControl();

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinHeight = mobile ? 48 : 0,
        };
        cancel.Click += (_, _) => Complete(null);

        var decisionStatus = new TextBlock
        {
            // Its accessible name is its text, because it is the line that speaks when the
            // decision changes. A stable control name is what a test locates it by instead.
            Name = "ExportDecisionStatus",
            TextWrapping = TextWrapping.Wrap,
            FontSize = TextScale.Of(12),
        };
        var choose = new Button
        {
            Content = "Choose a file…",
            IsDefault = true,
            MinHeight = mobile ? 48 : 0,
        };

        void RefreshDecision()
        {
            var selected = only ?? buttons.FirstOrDefault(item => item.Button.IsChecked == true).Scope;
            choose.IsEnabled = selected?.TimedRows is > 0;
            // The details belong to the scope being decided, not merely to the workspace
            // filter captured at invocation. In particular, "All timed entries" deliberately
            // ignores that filter; leaving its values visible here would contradict both the
            // status line and the CSV that is about to be written.
            filterDetail.Content = selected is null
                ? null
                : FilterDetail(selected.Filter, nameTemplate);
            cancel.Content = scopes.Count > 0 && scopes.All(static scope => scope.TimedRows is 0)
                ? "Close"
                : "Cancel";
            decisionStatus.Text = selected is not null
                ? $"{Counted.TimedRows(selected.TimedRows ?? 0)} · {displayedTimeZone} · {FilterSummary(selected.Filter)}"
                : _retry.IsVisible
                    ? "Could not calculate export rows. Try again."
                    : scopes.Count == 0
                        ? "Calculating rows…"
                        : scopes.All(static scope => scope.TimedRows is 0)
                            ? "No timed entries in this scope. Change the range or filters before exporting."
                            : scopes.Any(static scope => scope.Preferred && scope.TimedRows is 0)
                                ? "The selected scope has no timed rows. Choose another scope or close this dialog."
                                : "Choose a nonempty scope to continue.";
            AutomationProperties.SetName(decisionStatus, decisionStatus.Text);
        }

        // Publishing the whole offered set at once, with the preferred choice already made,
        // is what stops a slower count from moving the selection under the reader's hand.
        void Publish(IReadOnlyList<ResolvedExportScope> counted)
        {
            scopes = counted;
            buttons.Clear();
            scopeOptions.Children.Clear();
            only = counted.Count == 1 && counted[0].TimedRows is > 0 ? counted[0] : null;
            if (counted.Count == 1)
            {
                // Export range resolves its own scope, and a fitted unfiltered plot dedupes to
                // one. Asking the reader to choose from a list of one puts a control on screen
                // that cannot be answered wrongly and cannot be left alone; the review still
                // earns its place through the two options beside the decision.
                scopeOptions.Children.Add(ScopeSummary(counted[0]));
            }
            else
            {
                foreach (var scope in counted)
                {
                    var option = ScopeOption(scope, mobile);
                    option.IsCheckedChanged += (_, _) => RefreshDecision();
                    buttons.Add((option, scope));
                    scopeOptions.Children.Add(option);
                }

                var preferred = buttons.FirstOrDefault(item => item.Scope.Preferred && item.Scope.TimedRows is > 0);
                if (preferred.Button is not null)
                {
                    preferred.Button.IsChecked = true;
                }
            }

            RefreshDecision();
        }

        _publish = Publish;
        _refreshDecision = RefreshDecision;
        choose.Click += (_, _) =>
        {
            var selected = only ?? buttons.FirstOrDefault(item => item.Button.IsChecked == true).Scope;
            if (selected?.TimedRows is not > 0)
            {
                return;
            }

            Complete(new ExportDecision(
                selected,
                rowOrder.SelectedIndex == 0 ? EntryOrder.SourceSequence : EntryOrder.Chronological,
                encoding.SelectedIndex == 1));
        };

        var warnings = new StackPanel { Spacing = 4 };
        if (request.CaptureContinues)
        {
            warnings.Children.Add(Note(
                "Exports the committed records available when this export was opened. Capture continues."));
        }

        if (hasOffTimelineLines)
        {
            warnings.Children.Add(Note(
                "CSV contains timed parsed entries. Lines not on the timeline remain available in the session."));
        }

        var optionGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(mobile ? "*" : "*,*"),
            RowDefinitions = new RowDefinitions(mobile ? "Auto,Auto" : "Auto"),
            ColumnSpacing = 12,
            RowSpacing = 8,
        };
        optionGrid.Children.Add(Field("Row order", rowOrder));
        var encodingField = Field("Encoding", encoding);
        if (mobile)
        {
            Grid.SetRow(encodingField, 1);
        }
        else
        {
            Grid.SetColumn(encodingField, 1);
        }
        optionGrid.Children.Add(encodingField);

        _retry.MinHeight = mobile ? 48 : 0;
        _retry.Click += (_, _) => StartCounting();
        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var action in new[] { _retry, cancel, choose })
        {
            action.Margin = new Thickness(3);
            actions.Children.Add(action);
        }
        RefreshDecision();

        Content = SheetForm.Build(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Note("Choose exactly which timed entries to write. Counts and output use the same frozen session snapshot."),
                    scopeOptions,
                    decisionStatus,
                    warnings,
                    optionGrid,
                    Note("A successful export remembers these two choices as the new defaults."),
                    filterDetail,
                },
            },
            actions,
            new Thickness(16));
    }

    private Action<IReadOnlyList<ResolvedExportScope>>? _publish;
    private Action? _refreshDecision;

    /// <inheritdoc />
    protected override void OnPresented() => StartCounting();

    private void StartCounting()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _retry.IsVisible = false;
        _publish?.Invoke([]);
        _counting = CountAsync();

        async Task CountAsync()
        {
            try
            {
                var counted = await _countAsync(_lifetime.Token).ConfigureAwait(true);
                if (!_lifetime.IsCancellationRequested)
                {
                    _publish?.Invoke(counted);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch
            {
                if (_lifetime.IsCancellationRequested)
                {
                    return;
                }

                // No partial scope list: a count that failed has nothing true to say about
                // which scopes exist, so the reader is offered the same request again.
                _retry.IsVisible = true;
                _refreshDecision?.Invoke();
            }
        }
    }

    /// <inheritdoc />
    internal override void Dismiss()
    {
        base.Dismiss();
        Dispose();
    }

    /// <inheritdoc />
    internal override void ForceDismiss()
    {
        base.ForceDismiss();
        Dispose();
    }

    /// <summary>Cancels counting so the caller can dispose the snapshot it was reading.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
        {
            _lifetime.Cancel();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Waits for a cancelled count to actually stop touching the owned snapshot.</summary>
    internal Task DrainAsync() => _releaseTask ??= ReleaseCoreAsync();

    private async Task ReleaseCoreAsync()
    {
        Dispose();
        try
        {
            if (_counting is { } counting)
            {
                try
                {
                    await counting.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    /// <summary>
    /// The filter's actual values, on demand, in a section that scrolls on its own.
    /// </summary>
    /// <remarks>
    /// A filter can name thousands of tags. Naming its dimensions in the decision row keeps
    /// that row readable; a reader who needs to check one exact value opens this.
    /// </remarks>
    private static Control FilterDetail(FilterSpec filter, Func<uint, string> templateName)
    {
        var values = FilterValues(filter, templateName);
        if (values.Count == 0)
        {
            return new TextBlock { IsVisible = false };
        }

        return new Expander
        {
            Header = "Filter values",
            Content = new ScrollViewer
            {
                MaxHeight = 180,
                Content = new SelectableTextBlock
                {
                    Text = string.Join(Environment.NewLine, values),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = TextScale.Of(11.5),
                },
            },
        };
    }

    private static List<string> FilterValues(FilterSpec filter, Func<uint, string> templateName)
    {
        var lines = new List<string>();
        void Add(string label, IEnumerable<string> included, IEnumerable<string> excluded)
        {
            var inside = included.Order(StringComparer.Ordinal).ToArray();
            var outside = excluded.Order(StringComparer.Ordinal).ToArray();
            if (inside.Length > 0)
            {
                lines.Add($"{label} is {string.Join(", ", inside)}");
            }

            if (outside.Length > 0)
            {
                lines.Add($"{label} is not {string.Join(", ", outside)}");
            }
        }

        if (filter.Search is { } search)
        {
            lines.Add(
                $"Message {(search.IsRegex ? "matches regular expression" : "contains")} {search.Query} · " +
                (search.CaseSensitive ? "Match case" : "Ignore case"));
        }

        if (filter.IncludedLevels.Count > 0)
        {
            lines.Add($"Level is {string.Join(", ", filter.IncludedLevels.Select(static level => level.ToString()).Order(StringComparer.Ordinal))}");
        }

        Add("Tag", filter.IncludedTags, filter.ExcludedTags);
        Add("Process", filter.IncludedProcesses, filter.ExcludedProcesses);
        Add("PID", filter.IncludedPids.Select(Invariant), filter.ExcludedPids.Select(Invariant));
        Add("Thread", filter.IncludedTids.Select(Invariant), filter.ExcludedTids.Select(Invariant));
        Add("Buffer", filter.IncludedBuffers, filter.ExcludedBuffers);
        // By the message shape it matches, not by its mined ID: the same wording the chip
        // strip and the Templates group use, so the reader is checking one filter's values
        // rather than translating a number they were never shown the meaning of.
        Add("Template", filter.IncludedTemplates.Select(templateName), filter.ExcludedTemplates.Select(templateName));
        if (filter.IncludedOutcomes.Count > 0)
        {
            lines.Add(
                $"Parse outcome is {string.Join(", ", filter.IncludedOutcomes.Order().Select(OutcomeName))}");
        }

        return lines;
    }

    private static string Invariant(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string OutcomeName(ParseOutcomeKind outcome) => outcome switch
    {
        ParseOutcomeKind.ParsedEntry => "parsed entry",
        ParseOutcomeKind.MetaRecord => "meta record",
        ParseOutcomeKind.Continuation => "continuation line",
        ParseOutcomeKind.UntimedEntry => "untimed entry",
        ParseOutcomeKind.IgnoredBlank => "ignored blank line",
        ParseOutcomeKind.UnknownLine => "unknown line",
        ParseOutcomeKind.RejectedCandidate => "rejected candidate",
        _ => outcome.ToString(),
    };

    /// <summary>The one scope this export has, stated rather than offered.</summary>
    private static StackPanel ScopeSummary(ResolvedExportScope scope)
    {
        var heading = Heading(scope);
        var summary = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold },
                Note(scope.Summary),
            },
        };
        AutomationProperties.SetName(summary, heading);
        AutomationProperties.SetHelpText(summary, scope.Summary);
        return summary;
    }

    private static string Heading(ResolvedExportScope scope) =>
        $"{scope.Label} — {Counted.TimedRows(scope.TimedRows ?? 0)}";

    private static RadioButton ScopeOption(ResolvedExportScope scope, bool mobile)
    {
        var rows = scope.TimedRows ?? 0;
        var heading = Heading(scope);
        var option = new RadioButton
        {
            GroupName = "ExportScope",
            IsEnabled = rows > 0,
            MinHeight = mobile ? 56 : 0,
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap },
                    Note(scope.Summary),
                },
            },
        };
        AutomationProperties.SetName(option, heading);
        AutomationProperties.SetHelpText(option, scope.Summary);
        return option;
    }

    private static StackPanel Field(string label, Control value) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold },
            value,
        },
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.74,
        FontSize = TextScale.Of(11.5),
    };

    private static string FilterSummary(FilterSpec filter)
    {
        var dimensions = new List<string>();
        if (filter.Search is not null) dimensions.Add("search");
        if (filter.IncludedLevels.Count > 0) dimensions.Add("level");
        if (filter.IncludedTags.Count + filter.ExcludedTags.Count > 0) dimensions.Add("tag");
        if (filter.IncludedProcesses.Count + filter.ExcludedProcesses.Count > 0) dimensions.Add("process");
        if (filter.IncludedPids.Count + filter.ExcludedPids.Count > 0) dimensions.Add("PID");
        if (filter.IncludedTids.Count + filter.ExcludedTids.Count > 0) dimensions.Add("thread");
        if (filter.IncludedBuffers.Count + filter.ExcludedBuffers.Count > 0) dimensions.Add("buffer");
        if (filter.IncludedTemplates.Count + filter.ExcludedTemplates.Count > 0) dimensions.Add("template");
        if (filter.IncludedOutcomes.Count > 0) dimensions.Add("parse outcome");
        if (filter.TimeRange is not null) dimensions.Add("time");
        return dimensions.Count == 0 ? "No filters" : $"Filters: {string.Join(", ", dimensions)}";
    }
}
