using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

public sealed partial class RecentSessionsDialog
{
    /// <summary>
    /// Forces the touch composition of this dialog on or off, for tests that need to exercise
    /// it. Null means "ask the platform", which is what every shipping build does.
    /// </summary>
    internal static bool? MobileOverride { get; set; }

    private readonly RecentCapturePanel? _deletion;

    /// <summary>
    /// The deletion-capable composition. Without these hooks the dialog is the opening-only
    /// list it has always been, and no destructive control is built at all: this constructor
    /// is the feature switch.
    /// </summary>
    internal RecentSessionsDialog(RecentCaptureSnapshot snapshot, RecentCaptureActions actions)
        : this(snapshot?.Inventory.Sessions ?? [], snapshot?.Capturing, legacy: false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Taller than the opening-only list. This composition adds a selection bar, a status
        // line and a decision row above the list's own space, and a sheet that has to drop its
        // state legend to fit them at its own preferred size was never big enough.
        PreferredSize = new Size(780, 700);
        FontSize = TextScale.Of(14);
        _deletion = new RecentCapturePanel(
            snapshot,
            actions,
            MobileOverride ?? OperatingSystem.IsAndroid(),
            Complete,
            ShowNestedAsync,
            ShowNestedAsync);
        Content = _deletion;
    }

    internal IReadOnlyList<CaptureDeletionResult> DeletionResults => _deletion?.Results ?? [];

    internal void RequestRefresh() => _deletion?.RequestRefresh();

    protected override void OnPresented() => _deletion?.NotifyPresented();

    internal override void Dismiss()
    {
        if (_deletion?.Dismiss() != false)
        {
            base.Dismiss();
        }
    }

    internal override void ForceDismiss()
    {
        _deletion?.StopAndDetach();
        base.ForceDismiss();
    }
}

/// <summary>
/// Recent captures with selection, confirmation, progress and results.
/// </summary>
/// <remarks>
/// The dialog asks; the shell coordinates. Checks, confirmation, progress and results live
/// here; preparation, protection, tab lifetime, filesystem calls and home reconciliation are
/// the shell's, reached only through <see cref="RecentCaptureActions"/>. Deletion never closes
/// this dialog: returning "please delete" and reopening would lose the reader's place.
/// </remarks>
internal sealed class RecentCapturePanel : UserControl
{
    private const int PreviewNames = 5;

    /// <summary>Room enough for one row and a little more; below this, help yields.</summary>
    private const double ListFloor = 96;

    /// <summary>
    /// The extra room the list needs before the help it gave up is worth restoring.
    /// </summary>
    /// <remarks>
    /// In lines of the reader's own type, not logical pixels. Compaction hands back the state
    /// legend and the explanation above the list, and both are text: at 1.8 they are worth
    /// about 182 px together, which is more than a fixed 140 px band could absorb. Leaving
    /// compact then freed less room than restoring the help immediately consumed, the list fell
    /// back under its floor, and the sheet compacted again — an oscillation Avalonia reports as
    /// an infinite layout pass at the very viewport a landscape phone gives it at that scale.
    /// </remarks>
    private double LegendReserve => LineBox * 8;

    /// <summary>
    /// Whether the panel is too short to spend a whole row on the reconciling actions.
    /// </summary>
    /// <remarks>
    /// Measured against the panel's own height, and deliberately not against the list's, which
    /// is what <see cref="ApplyCompactHeight"/> uses. Folding the actions changes how much the
    /// list is given, so deciding it from the list would let the decision change its own input:
    /// the first attempt did exactly that and Avalonia stopped it with an infinite layout pass.
    /// The panel's height is imposed by the sheet and folding cannot move it. The multiple of
    /// the panel's own type is what the fixed bands cost — a selection bar, a status line, a
    /// decision row and one capture — so a reader who has enlarged their text reaches this at a
    /// viewport a reader who has not would still find roomy.
    /// </remarks>
    private bool FoldStatusActions => _root.Bounds.Height > 0 && _root.Bounds.Height < LineBox * 18;

    /// <summary>One drawn line of this sheet's own type.</summary>
    private double LineBox => Math.Ceiling((double.IsFinite(FontSize) && FontSize > 0 ? FontSize : 14) * 1.4);

    private readonly RecentCaptureActions _actions;
    private readonly bool _mobile;
    private readonly Action<string?> _complete;
    private readonly Func<CaptureDeleteConfirmation, Task<bool>> _confirm;
    private readonly Func<CaptureResultDetails, Task<bool>> _details;
    private readonly ObservableCollection<CaptureRow> _rows = [];
    private readonly Dictionary<string, CaptureDeletionResult> _ledger = new(SessionPath.Comparer);
    private readonly Dictionary<string, CaptureExclusion> _exclusions = new(SessionPath.Comparer);
    private readonly Dictionary<string, int> _ordinals = new(SessionPath.Comparer);
    private readonly ListBox _list = new();
    private readonly TextBlock _heading = Wrapped();
    private readonly TextBlock _summary = Wrapped();
    private readonly TextBlock _unavailable = Wrapped();
    private readonly TextBlock _status = Wrapped();
    private readonly TextBlock _health = Wrapped();
    private readonly CheckBox _all;
    private readonly StackPanel _selection = new() { Spacing = 2 };
    private readonly Grid _footer;
    private readonly WrapPanel _destructive = new();
    private readonly WrapPanel _decisions = new() { HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>
    /// Details, Refresh and Retry storage cleanup, which live beside the status line until the
    /// viewport is too short to spend a row on them.
    /// </summary>
    private readonly WrapPanel _statusActions = new();
    private readonly Button _delete, _open, _close, _select, _clear, _refresh, _showDetails, _cleanup, _stop, _capture;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 4, IsVisible = false };
    private readonly Grid _root;
    private readonly Control _legend;
    private readonly ScrollViewer _statusScroller;
    private readonly ScrollViewer _healthScroller;
    private readonly ScrollViewer _selectionSummaryScroller;
    private RecentCaptureSnapshot _snapshot;
    private TopLevel? _keyboard;
    private CancellationTokenSource? _operation;
    private bool _selecting, _busy, _deleting, _nested, _detached, _updating, _refreshQueued, _holdSuppressed, _holdEnteredSelect, _focused, _compact, _folded;
    private CaptureRow? _heldRow;
    private bool _heldWasChecked;
    private Guid _activeOperation;
    private long _lastProgressAnnouncement;
    private string _error = string.Empty;
    private string _selectionNotice = string.Empty;

    internal RecentCapturePanel(RecentCaptureSnapshot snapshot, RecentCaptureActions actions, bool mobile,
        Action<string?> complete, Func<CaptureDeleteConfirmation, Task<bool>> confirm,
        Func<CaptureResultDetails, Task<bool>> details)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _actions = actions;
        _mobile = mobile;
        _complete = complete;
        _confirm = confirm;
        _details = details;

        // Desktop keeps checks present whenever deletion is enabled; the phone has a mode,
        // because a tap there already means "open".
        _selecting = !mobile;
        _all = new SelectAllCheckBox(ToggleAll) { Content = "Select all", IsThreeState = true };
        Floor(_all);
        _delete = Action("Delete captures…", () => _ = DeleteSelectedAsync());
        _open = Action("Open", OpenSelected);
        _close = Action("Close", () => { if (!_busy) { _complete(null); } });
        _select = Action("Select", ToggleSelectMode);
        _clear = Action("Clear", ClearChecks);
        _refresh = Action("Refresh", RequestRefresh);
        _showDetails = Action("Details", () => _ = ShowDetailsAsync());
        _cleanup = Action("Retry storage cleanup", () => _ = RefreshAsync(retryCleanup: true));
        _stop = Action("Stop", Stop);
        _capture = Action("Capture this device's log", () => _complete(RecentSessionsDialog.CaptureThisDevice));

        var selectLine = new WrapPanel();
        // The label runs right up to the next control without this: on the device "Select all"
        // ended flush against Clear's border.
        _all.Margin = new Thickness(0, 3, 12, 0);
        selectLine.Children.Add(_all);
        selectLine.Children.Add(_clear);
        _selection.Children.Add(selectLine);
        var selectionSummary = new StackPanel { Spacing = 2 };
        selectionSummary.Children.Add(_summary);
        selectionSummary.Children.Add(_unavailable);
        _selectionSummaryScroller = new ScrollViewer
        {
            Content = selectionSummary,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _selection.Children.Add(_selectionSummaryScroller);

        // The list itself takes the focus, not one of its rows: a focused row would also
        // become the highlight, and Open must start with nothing to open.
        _list.Focusable = true;
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CaptureRow>((_, _) => BuildRow(), supportsRecycling: true);
        SheetForm.SpeakRows<CaptureRow>(_list, row => row.Speech);
        AutomationProperties.SetName(_list, "Stored captures");
        AutomationProperties.SetHelpText(_list, SheetForm.SessionStateHelp);
        _list.SelectionChanged += (_, _) => Update();
        _list.Tapped += OnListTapped;
        _list.DoubleTapped += OnListDoubleTapped;
        _list.SetValue(InputElement.IsHoldingEnabledProperty, mobile);
        _list.PointerPressed += (_, _) => { _holdSuppressed = false; _heldRow = null; };
        _list.Holding += OnListHolding;

        var help = new Expander { Header = "Capture states", Content = SheetForm.SessionStateLegend() };
        Floor(help);
        _legend = new ScrollViewer { Content = help, MaxHeight = 110 };

        var statusArea = new StackPanel { Spacing = 3 };
        _statusScroller = new ScrollViewer
        {
            Content = _status,
            MaxHeight = 64,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _healthScroller = new ScrollViewer
        {
            Content = _health,
            MaxHeight = 48,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        statusArea.Children.Add(_statusScroller);
        statusArea.Children.Add(_healthScroller);
        statusArea.Children.Add(_progress);
        statusArea.Children.Add(_statusActions);
        statusArea.Children.Add(_legend);
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        // A destructive action is not a peer of the decision that ends the dialog, so it sits
        // at the far side of the row where convention puts it. Both halves wrap, and the whole
        // row stacks when the viewport is too narrow for enlarged labels to fit side by side.
        _footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        _footer.Children.Add(_destructive);
        Grid.SetColumn(_decisions, 1);
        _footer.Children.Add(_decisions);
        // A newly visible cleanup action or changed label can alter the required width
        // without changing the footer's current bounds. Recheck after children are measured.
        _footer.LayoutUpdated += (_, _) => ApplyFooterOrientation();

        _root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(12), RowSpacing = 4 };
        _list.LayoutUpdated += (_, _) => ApplyCompactHeight();
        Add(_heading, 0);
        Add(_selection, 1);
        Add(_list, 2);
        Add(statusArea, 3);
        Add(_footer, 4);
        Content = _root;
        Apply(snapshot);

        void Add(Control control, int row)
        {
            Grid.SetRow(control, row);
            _root.Children.Add(control);
        }
    }

    internal IReadOnlyList<CaptureDeletionResult> Results => [.. _ledger.Values];

    /// <summary>
    /// Gives up help before it gives up the list.
    /// </summary>
    /// <remarks>
    /// Every other row of the sheet sizes to its content, so on a landscape phone their sum took
    /// the whole card and the star row the list lives in was left with nothing: the list measured
    /// about a pixel while the state legend, which is help rather than an action, kept its full
    /// height. The trigger is the list actually being squeezed to its floor, not a height
    /// threshold — the sheet grows to its content where it can, and a short-looking card with
    /// four captures in it has plenty of room. Coming back out needs clearly more room than
    /// going in, so restoring the legend cannot squeeze the list and start the cycle again.
    /// </remarks>
    private void ApplyCompactHeight()
    {
        if (_detached || _root.Bounds.Height <= 0)
        {
            return;
        }

        // Restated on every pass: the budgets are whole lines of the reader's type, and the
        // scale can change under a live view.
        ApplyTextBudgets();

        // The star row's own allocation, which is the height the list was actually given and
        // is unaffected by anything the list asks for. An empty sheet has no list to protect,
        // so it keeps the answer it last had rather than reading a row that is not there.
        var allocated = _root.RowDefinitions[2].ActualHeight;
        var compact = !_list.IsVisible ? _compact
            : _compact ? allocated < ListFloor + LegendReserve
            : allocated < ListFloor;

        // Both are re-read here rather than inside Update, which does not run on every layout
        // pass. Deciding the fold there left it stale whenever the sheet changed height without
        // the list crossing its floor — a rotation into a roomier viewport, say. The fold is
        // about the panel rather than the list, so an empty sheet answers it too: left frozen
        // there, every action an emptied list still offers ended up in one row that did not
        // wrap, and its last button ran off the side of the card.
        var folded = FoldStatusActions;
        if (compact == _compact && folded == _folded)
        {
            return;
        }

        _compact = compact;
        _folded = folded;
        ApplyTextBudgets();
        Update();
    }

    /// <summary>
    /// How much room the status, health and selection summary may take, in whole lines.
    /// </summary>
    /// <remarks>
    /// Whole lines of the reader's own type, never a fixed band of logical pixels. The caps
    /// were written as pixels, and at 1.8 that put 1.33 lines in the compact status box: on the
    /// device <em>Tap a capture to open it. Select chooses captures to delete.</em> came out
    /// with its second line sliced along its x-height, which reads as broken rendering rather
    /// than as a line that continues below. The roomy budgets are multiples of the line box so
    /// enlarged text keeps the same number of lines; the compact ones round a fixed budget down
    /// into whole lines, so a short viewport gives up a line rather than half of one.
    /// </remarks>
    private void ApplyTextBudgets()
    {
        var line = LineBox;
        _heading.LineHeight = line;
        _summary.LineHeight = line;
        _unavailable.LineHeight = line;
        _status.LineHeight = line;
        _health.LineHeight = line;

        _statusScroller.MaxHeight = _compact ? Whole(40) : line * 3;
        _healthScroller.MaxHeight = _compact ? Whole(32) : line * 2;
        _selectionSummaryScroller.MaxHeight = _compact ? Whole(56) : double.PositiveInfinity;

        // Breathing room is the cheapest thing on the sheet to spend when the list is at its
        // floor, and four rows of it add up to a visible part of one capture.
        _root.RowSpacing = _compact ? 2 : 4;

        double Whole(double budget) => Math.Max(1, Math.Floor(budget / line)) * line;
    }

    private static TextBlock Wrapped() => new() { TextWrapping = TextWrapping.Wrap };

    private void Floor(Control control)
    {
        control.MinHeight = TouchTarget.SelfSized(_mobile);
        control.MinWidth = TouchTarget.SelfSized(_mobile);
    }

    private Button Action(string text, Action activate)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 3, 6, 0) };
        Floor(button);
        button.Click += (_, _) => activate();
        return button;
    }

    /// <summary>
    /// One row: a leading check with a hit area of its own, and a flexible text column.
    /// </summary>
    /// <remarks>
    /// Everything is bound, so a recycled container is correct for its new item without any
    /// retained callback capturing the item it used to hold.
    /// </remarks>
    private Grid BuildRow()
    {
        var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        Floor(check);
        check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(CaptureRow.Checked)) { Mode = BindingMode.TwoWay });
        check.Bind(IsEnabledProperty, new Binding(nameof(CaptureRow.CanCheck)));
        check.Bind(IsVisibleProperty, new Binding(nameof(CaptureRow.ShowCheck)));
        check.Bind(AutomationProperties.NameProperty, new Binding(nameof(CaptureRow.SelectLabel)));
        check.Bind(AutomationProperties.HelpTextProperty, new Binding(nameof(CaptureRow.State)));
        var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(CaptureRow.Name)));
        var state = Wrapped();
        state.Bind(TextBlock.TextProperty, new Binding(nameof(CaptureRow.State)));
        var note = Wrapped();
        note.Bind(TextBlock.FontSizeProperty, new Binding(nameof(FontSize))
        {
            Source = this,
            Converter = new Avalonia.Data.Converters.FuncValueConverter<double, double>(size => size * 11 / 14),
        });
        note.Bind(TextBlock.TextProperty, new Binding(nameof(CaptureRow.Note)));
        note.Bind(IsVisibleProperty, new Binding(nameof(CaptureRow.HasNote)));
        var text = new StackPanel { Spacing = 3, Margin = new Thickness(5, 6) };
        text.Children.Add(name);
        text.Children.Add(state);
        text.Children.Add(note);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),

            // 48 dp is the floor for a target; a list row a thumb scrolls and taps gets the
            // 56 dp list-item height instead, which is also what two lines of text need.
            MinHeight = _mobile ? 56 : 0,
        };
        row.Children.Add(check);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static CaptureRow? RowFrom(object? source) => source is Visual visual
        ? visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as CaptureRow
        : null;

    private static bool CheckboxSource(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<CheckBox>(includeSelf: true) is not null;

    private static string IdentityKey(string path, string? identity) => SessionPath.Canonical(path) + "|" + identity;

    private static string IdentityKey(TemporarySessionInfo session) => IdentityKey(session.Path, session.Identity);

    private static string IdentityKey(PreparedCapture capture) => IdentityKey(capture.Target.Path, capture.Target.Identity);

    // ---- snapshot ----------------------------------------------------------------------

    /// <summary>
    /// Applies one snapshot: sessions, live/busy/open state, storage health and cleanup
    /// inventory together, never one at a time.
    /// </summary>
    private void Apply(RecentCaptureSnapshot snapshot)
    {
        if (_detached || snapshot.Generation < _snapshot.Generation)
        {
            return;
        }

        var previousInventory = _snapshot.Inventory;
        _snapshot = snapshot;
        if (!snapshot.Inventory.Available)
        {
            _snapshot = snapshot with
            {
                Inventory = snapshot.Inventory with
                {
                    PendingCleanup = Math.Max(previousInventory.PendingCleanup, snapshot.Inventory.PendingCleanup),
                    UnresolvedCleanup = Math.Max(previousInventory.UnresolvedCleanup, snapshot.Inventory.UnresolvedCleanup),
                }
            };
            // An unreadable root is not an empty one, and it is not proof that anything was
            // deleted: the rows that were listed stay listed and Refresh stays available.
            _error = "Temporary storage is unavailable. Try Refresh.";
            Update();
            return;
        }

        var highlight = (_list.SelectedItem as CaptureRow)?.Key;
        var focused = FocusedRowIndex();
        var scroller = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroller?.Offset;
        var previous = _rows.ToDictionary(row => row.Key, SessionPath.Comparer);
        var wasChecked = _rows.Where(row => row.Checked).Select(row => row.Key).ToHashSet(SessionPath.Comparer);

        // Duplicate names are disambiguated by date and size first. A dialog-local ordinal is
        // added only when those collide too, and it is stable for an identity across refresh.
        var names = new Dictionary<string, string>(SessionPath.Comparer);
        var stamps = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var session in snapshot.Inventory.Sessions)
        {
            var key = IdentityKey(session);
            var name = SessionCacheName.Describe(session.Path);
            names[key] = name;
            var stamp = Stamp(name, session);
            stamps[stamp] = stamps.GetValueOrDefault(stamp) + 1;
        }

        var next = new List<CaptureRow>();
        foreach (var session in snapshot.Inventory.Sessions)
        {
            var key = IdentityKey(session);
            if (_ledger.TryGetValue(key, out var known) && known.File.Removed)
            {
                // A scan that started before this deletion committed cannot reinsert what it
                // removed. A recreated path with a new identity is new data, not this row.
                continue;
            }

            if (!_ordinals.TryGetValue(key, out var ordinal))
            {
                ordinal = _ordinals.Count + 1;
                _ordinals.Add(key, ordinal);
            }

            var name = names[key];
            var stamp = Stamp(name, session);
            var label = stamps[stamp] > 1 ? $"{stamp} · Capture {ordinal}" : stamp;
            var row = previous.GetValueOrDefault(key) ?? new CaptureRow(key, session, Update);
            row.Session = session;
            row.Name = name;
            row.Label = label;
            var capturing = snapshot.Capturing.Contains(session.Path);
            var busy = snapshot.Busy.Contains(session.Path);
            row.Eligible = session.Identity is not null && !capturing && !busy;
            row.State = SheetForm.DescribeSessionState(session, capturing) +
                (busy && !capturing ? " · work in progress" : string.Empty) +
                (snapshot.Open.Contains(session.Path) ? " · open in a tab" : string.Empty) +
                (session.Identity is null ? " · unavailable for deletion" : string.Empty) +
                (stamps[stamp] > 1 ? $" · Capture {ordinal}" : string.Empty);
            row.Note = !row.Eligible
                ? CaptureReason.For(
                    capturing ? CaptureProtection.Recording : busy ? CaptureProtection.Working : CaptureProtection.None,
                    CaptureDeleteOutcome.Refused)
                : _ledger.TryGetValue(key, out var result) && result.Failed ? result.Reason
                : string.Empty;
            if (!row.Eligible)
            {
                row.Checked = false;
            }

            next.Add(row);
        }

        var nextSet = next.ToHashSet();
        var presentPaths = next.Select(row => row.Session.Path).ToHashSet(SessionPath.Comparer);
        if (snapshot.Inventory.InspectionIssues > 0)
        {
            // A capture the scan could not read is not a capture that went away. Its row stays,
            // says so, and cannot be selected until a refresh can see it again.
            foreach (var row in previous.Values.Where(row => !nextSet.Contains(row)))
            {
                // An unreadable sibling is not a reason to retain an obsolete identity at a
                // path whose replacement was successfully listed in this very snapshot.
                if (presentPaths.Contains(row.Session.Path)) continue;
                if (_ledger.TryGetValue(row.Key, out var removed) && removed.File.Removed)
                {
                    continue;
                }

                row.Eligible = false;
                row.Checked = false;
                row.Note = "This capture could not be listed. Try Refresh.";
                next.Add(row);
                nextSet.Add(row);
            }
        }

        for (var i = _rows.Count - 1; i >= 0; i--)
        {
            if (!nextSet.Contains(_rows[i]))
            {
                _rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < next.Count; i++)
        {
            // Ordinary refreshes and arrivals stay linear. Only a real reorder needs to
            // search/move an existing observable row and disturb its realised container.
            if (i < _rows.Count && ReferenceEquals(_rows[i], next[i])) continue;
            var old = previous.ContainsKey(next[i].Key) ? _rows.IndexOf(next[i]) : -1;
            if (old < 0)
            {
                _rows.Insert(i, next[i]);
            }
            else if (old != i)
            {
                _rows.Move(old, i);
            }
        }

        _list.SelectedItem = _rows.FirstOrDefault(row => SessionPath.Comparer.Equals(row.Key, highlight));
        AnnounceLostSelection(wasChecked);
        Update();
        if (offset.HasValue && scroller is not null)
        {
            scroller.Offset = offset.Value;
        }

        RestoreFocus(focused);
    }

    private static string Stamp(string name, TemporarySessionInfo session) =>
        $"{name} · {session.UpdatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)} · " +
        RecentSessionsDialog.FormatBytes(session.SizeBytes);

    /// <summary>Says why checks disappeared, rather than letting them vanish silently.</summary>
    private void AnnounceLostSelection(HashSet<string> wasChecked)
    {
        if (wasChecked.Count == 0 || _deleting || _nested)
        {
            return;
        }

        var eligible = _rows.Where(row => row.Eligible).Select(row => row.Key).ToHashSet(SessionPath.Comparer);
        var lost = wasChecked.Count(key => !eligible.Contains(key) &&
            (!_ledger.TryGetValue(key, out var result) || !result.File.Removed));
        if (lost > 0)
        {
            _selectionNotice = $"{Counted.Captures(lost)} you had selected {(lost == 1 ? "is" : "are")} no longer available to delete.";
            _status.Text = string.Join(' ', new[] { ResultSummary(), _selectionNotice }.Where(part => part.Length > 0));
        }
    }

    private int FocusedRowIndex()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_list.ContainerFromIndex(i) is { } container && container.IsKeyboardFocusWithin)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Keeps the keyboard somewhere useful after rows disappear: the next survivor, then the
    /// previous one, then the action that is left.
    /// </summary>
    private void RestoreFocus(int index)
    {
        if (index < 0 || _detached)
        {
            return;
        }

        if (_rows.Count == 0)
        {
            (_capture.IsVisible ? _capture : _close).Focus();
            return;
        }

        var target = Math.Min(index, _rows.Count - 1);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_detached && target < _rows.Count)
            {
                _list.ContainerFromIndex(target)?.Focus();
            }
        }, DispatcherPriority.Input);
    }

    // ---- rendering ---------------------------------------------------------------------

    private void Update()
    {
        if (_updating || _detached)
        {
            return;
        }

        _updating = true;
        try
        {
            var available = _snapshot.Inventory.Available;
            var selected = _rows.Where(row => row.Checked && row.Eligible).ToArray();
            var eligible = _rows.Count(row => row.Eligible);
            var recording = _rows.Count(row => _snapshot.Capturing.Contains(row.Session.Path));
            var working = _rows.Count(row => !row.Eligible) - recording;
            var empty = _rows.Count == 0;
            foreach (var row in _rows)
            {
                row.Present(_selecting, !_busy && available);
            }

            _all.IsChecked = selected.Length == 0 ? false : selected.Length == eligible ? true : null;
            _all.IsEnabled = eligible > 0 && !_busy && available;
            _selection.IsVisible = !empty && _selecting;
            _summary.Text = (_compact
                ? $"{selected.Length:N0} of {eligible:N0} selected"
                : $"{selected.Length:N0} of {eligible:N0} available captures selected") +
                (selected.Length > 0
                    ? $" · about {RecentSessionsDialog.FormatBytes(SumSizes(selected.Select(row => row.Session.SizeBytes)))}"
                    : string.Empty);
            _unavailable.Text = recording > 0 && working > 0
                ? $"{Counted.Captures(recording)} {(recording == 1 ? "is" : "are")} being recorded and {Counted.Captures(working)} {(working == 1 ? "is" : "are")} in use."
                : recording > 0 ? $"{Counted.Captures(recording)} {(recording == 1 ? "is" : "are")} being recorded."
                : working > 0 ? $"{Counted.Captures(working)} {(working == 1 ? "is" : "are")} in use and cannot be selected."
                : string.Empty;
            _unavailable.IsVisible = _unavailable.Text.Length > 0;
            if (_compact && _unavailable.IsVisible)
            {
                _unavailable.Text = recording > 0 && working > 0 ? $"{recording:N0} recording · {working:N0} unavailable"
                    : recording > 0 ? $"{recording:N0} recording" : $"{working:N0} unavailable";
            }

            _heading.Text = !available ? "Temporary storage is unavailable. Try Refresh."
                : empty ? _ledger.Values.Any(result => result.File.Removed)
                    ? "No captures remain in temporary storage."
                    : "No captures on this device yet."
                : _mobile && _selecting ? "Choose captures to delete. Deleting is permanent."
                : _mobile
                    ? "These captures are stored in this app's private storage. Share… hands one to another app as a portable archive."
                    : "These captures are stored in temporary storage. Saving a capture keeps a copy in a location you choose.";
            // The title, checks and Delete action still explain the mode. On a constrained
            // screen instructional prose must yield before the capture list disappears.
            _heading.IsVisible = empty || !_compact;

            // A card sized to a fixed bottom band would pin an empty region to the bottom of
            // the display; an empty dialog is small and a full one is tall.
            _root.RowDefinitions[2].Height = empty ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            _list.IsVisible = !empty;
            _legend.IsVisible = !empty && !_compact;
            _list.IsEnabled = !_busy;

            _delete.Content = selected.Length == 0
                ? _mobile ? "Delete…" : "Delete captures…"
                : _mobile ? $"Delete {selected.Length:N0}…" : $"Delete {Counted.Captures(selected.Length)}…";
            AutomationProperties.SetName(_delete, selected.Length == 0 ? "Delete captures" : $"Delete {Counted.Captures(selected.Length)}");
            _delete.IsEnabled = selected.Length > 0 && !_busy && available;
            _open.IsEnabled = _list.SelectedItem is CaptureRow highlighted && _rows.Contains(highlighted) && !_busy;
            _clear.IsEnabled = selected.Length > 0 && !_busy;
            _select.Content = _selecting ? "Done" : "Select";
            foreach (var button in new[] { _select, _close, _refresh, _cleanup, _capture })
            {
                button.IsEnabled = !_busy;
            }

            _cleanup.IsVisible = _snapshot.Inventory.PendingCleanup + _snapshot.Inventory.UnresolvedCleanup > 0 ||
                _ledger.Values.Any(result => result.File.Outcome == CaptureDeleteOutcome.DeletedPendingReclaim);
            _showDetails.IsVisible = _ledger.Count > 0 || _exclusions.Count > 0 || _snapshot.Inventory.UnresolvedCleanup > 0;
            _showDetails.IsEnabled = !_busy;
            _health.Text = _error.Length > 0 ? _error
                : _snapshot.Inventory.InspectionIssues > 0 ? "Some captures could not be listed. Try Refresh."
                : _snapshot.Inventory.UnresolvedCleanup > 0 ? "Some storage cleanup could not be verified. Open Details."
                : _snapshot.Inventory.PendingCleanup > 0 ? "Storage cleanup is pending. Some space is still in use."
                : string.Empty;
            _health.IsVisible = _health.Text.Length > 0;
            _healthScroller.IsVisible = _health.IsVisible;
            // Selection controls already describe these instructions. On a short screen
            // preserve result/error text and spend this duplicate help line on the list.
            _statusScroller.IsVisible = !string.IsNullOrEmpty(_status.Text) && !(_compact && _status.Text is
                "Selecting captures. Tap a capture to select it." or
                "Tap a capture to open it. Select chooses captures to delete." or "Finished selecting.");
            _progress.IsVisible = _busy;

            var destructive = new List<Control>();
            var decisions = new List<Control>();

            // A short viewport gave up the state legend first, and that was not enough at an
            // enlarged text scale: on the device in landscape at 1.8 the list measured 37 dp,
            // less than half of one row, while a whole 49 dp row below it held three buttons.
            // Reconciling actions are not help and cannot simply be dropped, so they move down
            // to the decision row, which already wraps and had the width to spare. Nothing
            // leaves the screen; the list gets the row back.
            var statusActions = new Control[] { _showDetails, _refresh, _cleanup };
            // Height alone, so a deletion cannot make the sheet rearrange itself underneath
            // the reader while it runs.
            Fill(_statusActions, _folded ? [] : statusActions);
            _statusActions.IsVisible = !_folded;
            if (_folded)
            {
                decisions.AddRange(statusActions);
            }

            if (_deleting)
            {
                decisions.Add(_stop);
            }
            else
            {
                if (!empty && _selecting)
                {
                    destructive.Add(_delete);
                }

                if (!empty && _mobile)
                {
                    decisions.Add(_select);
                }

                decisions.Add(_close);

                // Open belongs to the composition that needs it. A tap already opens a capture
                // on the phone, so the button could never be anything but permanently disabled
                // there: nothing ever sets the highlight it acts on.
                if (!empty && !_mobile)
                {
                    decisions.Add(_open);
                }

                // Only where it does something. The sentinel starts a recording on the touch
                // composition; on the desktop it closed the dialog and changed nothing, which
                // is the "enabled and silently does nothing" shape the audits removed elsewhere.
                if (empty && available && _mobile)
                {
                    decisions.Add(_capture);
                }
            }

            if (_folded)
            {
                // One wrapping run can use the whole next line. Two separate groups leave
                // the space beneath Delete unused while the other actions add more rows.
                decisions.InsertRange(0, destructive);
                destructive.Clear();
            }
            _decisions.HorizontalAlignment = _folded ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Fill(_destructive, destructive);
            Fill(_decisions, decisions);
            ApplyFooterOrientation();
        }
        finally
        {
            _updating = false;
        }

        static void Fill(Panel panel, IReadOnlyList<Control> children)
        {
            if (panel.Children.SequenceEqual(children))
            {
                return;
            }

            panel.Children.Clear();
            foreach (var child in children)
            {
                // A control keeps one parent, and these move between the status area and the
                // decision row as the viewport changes shape. Taking it out of wherever it is
                // makes the order of the two fills irrelevant.
                (child.Parent as Panel)?.Children.Remove(child);
                panel.Children.Add(child);
            }
        }
    }

    /// <summary>
    /// Stacks the decision row when a horizontal one would not fit enlarged labels.
    /// </summary>
    /// <remarks>
    /// The buttons are measured, not the panels that hold them. A wrapping panel's own desired
    /// width is a consequence of the width it was given, and the two halves of this row are
    /// given different widths side by side and stacked — so reading it made the answer depend
    /// on the arrangement the answer chooses, and once the row held enough buttons to be
    /// genuinely close to the boundary, Avalonia stopped the flip-flop with an infinite layout
    /// pass. What a button asks for does not change when the row rearranges.
    /// </remarks>
    private void ApplyFooterOrientation()
    {
        // Decisions may wrap beside Delete. Stack the groups only when even the widest
        // individual decision cannot fit there; stacking for their combined width wastes
        // another complete action row on an enlarged landscape phone.
        var stacked = _footer.Bounds.Width > 0 && Wanted(_destructive) +
            _decisions.Children.Where(child => child.IsVisible).Select(child => child.DesiredSize.Width).DefaultIfEmpty().Max()
            > _footer.Bounds.Width;
        if (stacked == (Grid.GetRow(_decisions) == 1))
        {
            return;
        }

        // DesiredSize already carries each child's margin, so the buttons' own spacing is in
        // this sum exactly once.
        static double Wanted(Panel panel) => panel.Children
            .Where(child => child.IsVisible)
            .Sum(child => child.DesiredSize.Width);

        Grid.SetRow(_decisions, stacked ? 1 : 0);
        Grid.SetColumn(_decisions, stacked ? 0 : 1);

        // A stacked half still has to wrap inside the row rather than inside one Auto column,
        // which measures it unconstrained and lets its last button run off the side.
        Grid.SetColumnSpan(_decisions, stacked ? 2 : 1);
        Grid.SetColumnSpan(_destructive, stacked ? 2 : 1);
    }

    internal static long SumSizes(IEnumerable<long> sizes)
    {
        long sum = 0;
        foreach (var size in sizes)
        {
            sum = size > long.MaxValue - sum ? long.MaxValue : sum + Math.Max(0, size);
        }

        return sum;
    }

    // ---- selection ---------------------------------------------------------------------

    private void ToggleAll()
    {
        if (_busy)
        {
            return;
        }

        // The cycle is defined by the model's previous state, not by three-state visuals:
        // none and mixed both mean "check every eligible capture", checked means "clear".
        var select = _rows.Any(row => row.Eligible && !row.Checked);
        SetChecks(row => select && row.Eligible);
    }

    private void ClearChecks() => SetChecks(_ => false);

    private void SetChecks(Func<CaptureRow, bool> value)
    {
        _updating = true;
        try
        {
            foreach (var row in _rows)
            {
                row.Checked = value(row);
            }
        }
        finally
        {
            _updating = false;
        }

        Update();
    }

    private void ToggleSelectMode()
    {
        _selecting = !_selecting;
        ClearChecks();
        _status.Text = _selecting ? "Selecting captures. Tap a capture to select it." : "Finished selecting.";
        Update();
    }

    private void OpenSelected()
    {
        if (!_busy && !_nested && (!_mobile || !_selecting) && _list.SelectedItem is CaptureRow row && _rows.Contains(row))
        {
            _complete(row.Session.Path);
        }
    }

    private void OnListTapped(object? sender, TappedEventArgs args)
    {
        if (_busy || _nested || _holdSuppressed || CheckboxSource(args.Source) || RowFrom(args.Source) is not { } row)
        {
            return;
        }

        if (_mobile && _selecting)
        {
            row.Checked = !row.Checked;
            args.Handled = true;
        }
        else if (_mobile)
        {
            _complete(row.Session.Path);
            args.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (_mobile || _busy || _nested || _holdSuppressed || CheckboxSource(args.Source))
        {
            return;
        }

        OpenSelected();
        args.Handled = true;
    }

    private void OnListHolding(object? sender, HoldingRoutedEventArgs args)
    {
        if (!_mobile || _busy || _nested || CheckboxSource(args.Source))
        {
            return;
        }

        if (args.HoldingState == HoldingState.Started && RowFrom(args.Source) is { } row)
        {
            _heldRow = row;
            _heldWasChecked = row.Checked;
            _holdSuppressed = true;
            _holdEnteredSelect = !_selecting;
            _selecting = true;

            // A protected row may enter select mode and still refuse the check; the reason is
            // announced rather than left to a disabled control nobody can reach.
            row.Checked = true;
            _status.Text = row.Eligible ? "Selecting captures. Tap a capture to select it." : row.Note;
            Update();
        }
        else if (args.HoldingState == HoldingState.Canceled && _heldRow is { } held)
        {
            // A hold that turned into a scroll must neither check nor open, and must not leave
            // the mode it was about to enter.
            held.Checked = _heldWasChecked;
            if (_holdEnteredSelect)
            {
                _selecting = false;
                _holdEnteredSelect = false;
            }

            _holdSuppressed = true;
            Update();
        }

        args.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (_nested || args.Handled)
        {
            return;
        }

        // Not on the touch composition. Android delivers a Back press as an Escape key-down
        // and then the platform back callback, and the host answers the second one through the
        // overlay stack: handling the first as well took two steps for one press — leaving
        // select mode and closing the dialog — which is the shape of V2-18 all over again.
        if (args.Key == Key.Escape && !_mobile)
        {
            if (Dismiss())
            {
                _complete(null);
            }

            args.Handled = true;
            return;
        }

        if (_busy || args.Source is not Visual source || !(ReferenceEquals(source, _list) || source.GetVisualAncestors().Contains(_list)))
        {
            return;
        }

        // A focused control acts for itself; the row does not also toggle, and text selection
        // keeps its own keys.
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null ||
            CheckboxSource(source) ||
            source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            return;
        }

        switch (args.Key)
        {
            case Key.Space when _list.SelectedItem is CaptureRow row:
                if (_mobile)
                {
                    _selecting = true;
                }

                row.Checked = !row.Checked;
                Update();
                break;
            case Key.A when args.KeyModifiers.HasFlag(KeyModifiers.Control):
                if (_mobile)
                {
                    _selecting = true;
                }

                ClearChecks();
                if (!args.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ToggleAll();
                }

                break;
            case Key.Delete when _rows.Any(row => row.Checked && row.Eligible):
                _ = DeleteSelectedAsync();
                break;
            case Key.Enter:
                OpenSelected();
                break;
            default:
                return;
        }

        args.Handled = true;
    }

    // ---- lifetime ----------------------------------------------------------------------

    internal void NotifyPresented()
    {
        if (_mobile)
        {
            _status.Text = "Tap a capture to open it. Select chooses captures to delete.";
        }

        TakeInitialFocus();
    }

    /// <summary>
    /// Puts the keyboard where a reader would want it, once: the list, or the way out when
    /// there is no list.
    /// </summary>
    /// <remarks>
    /// A modal window is not necessarily activated at the moment it reports being presented, so
    /// the request is posted after layout and retried on the first attachment. Escape and
    /// Ctrl+A do not depend on it — those are handled at the top level — but arrow-key
    /// navigation does, and a dialog that opens with nothing focused is a dialog a keyboard
    /// reader has to tab into before it does anything.
    /// </remarks>
    private void TakeInitialFocus(int attempt = 0)
    {
        if (_focused || attempt > 10)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_detached || _focused || _keyboard is null)
                {
                    return;
                }

                // A modal window can still be inactive here, and focus is refused until it is
                // not. Ask again rather than leaving the dialog with nothing focused.
                _focused = (_rows.Count > 0 ? (Control)_list : _close).Focus();
                if (!_focused)
                {
                    TakeInitialFocus(attempt + 1);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Escape has to work from anywhere in the dialog, including from the window chrome, so the
    /// handler is attached to the top level for as long as this panel is on screen. It is
    /// tunnelled and claims only what section 8.8 gives it, and it stands down while a nested
    /// dialog owns the keyboard.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _keyboard = TopLevel.GetTopLevel(this);

        // FontSize is inherited, so the line box is only knowable once there is a parent.
        ApplyTextBudgets();

        // Tunnelled, not bubbled. A ListBox answers Space for its own selection and marks it
        // handled, so a bubbling handler never saw the one key section 8.8 gives to the row's
        // check. The handler claims only the keys it means to and leaves navigation to the list.
        _keyboard?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        TakeInitialFocus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The top level is already gone by the time detachment is observed, so it is the one
        // captured on attachment that has to be unhooked.
        _keyboard?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _keyboard = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// State-aware dismissal. Returns true when the dialog itself should end.
    /// </summary>
    internal bool Dismiss()
    {
        if (_busy)
        {
            if (_deleting)
            {
                _status.Text = "Deletion is in progress. Use Stop to stop after the current capture.";
            }
            else if (!_nested)
            {
                _operation?.Cancel();
            }

            return false;
        }

        if (_mobile && _selecting)
        {
            _selecting = false;
            ClearChecks();
            _status.Text = "Finished selecting.";
            Update();
            return false;
        }

        if (!_mobile && _rows.Any(row => row.Checked))
        {
            ClearChecks();
            return false;
        }

        return true;
    }

    private void Stop()
    {
        _operation?.Cancel();
        _stop.Content = "Stopping…";
        _stop.IsEnabled = false;
        _status.Text = "Stopping… Finishing the current capture.";
    }

    /// <summary>Real owner teardown: stop future work, ignore late posts, keep the commits.</summary>
    internal void StopAndDetach()
    {
        _detached = true;
        _operation?.Cancel();
    }

    internal void RequestRefresh()
    {
        if (_detached)
        {
            return;
        }

        if (_busy || _nested)
        {
            _refreshQueued = true;
            return;
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync(bool retryCleanup = false)
    {
        if (_busy || _detached)
        {
            return;
        }

        _busy = true;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _selectionNotice = string.Empty;
        _progress.IsIndeterminate = true;
        if (retryCleanup)
        {
            _status.Text = "Cleaning up storage…";
        }

        Update();
        try
        {
            if (retryCleanup)
            {
                await _actions.RetryCleanup(operation.Token);
            }

            var snapshot = await _actions.Refresh(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (_detached || snapshot.Generation < _snapshot.Generation) return;
            _error = string.Empty;
            Apply(snapshot);
            var settled = snapshot.Inventory.Available &&
                snapshot.Inventory.PendingCleanup == 0 &&
                snapshot.Inventory.UnresolvedCleanup == 0;
            if (snapshot.Inventory.Available)
            {
                // Cleanup finished after the fact. A capture that was reported as pending is
                // now simply deleted; it is never counted as a second deletion.
                foreach (var (key, result) in _ledger.ToArray())
                {
                    if (result.File.Outcome == CaptureDeleteOutcome.DeletedPendingReclaim &&
                        (settled || snapshot.Inventory.UnresolvedCleanup == 0 && snapshot.Inventory.PendingIdentities is { } pending &&
                            !pending.Contains(result.Capture.Target.Identity)))
                    {
                        _ledger[key] = result with { File = result.File with { Outcome = CaptureDeleteOutcome.Deleted } };
                    }
                }
            }

            if (retryCleanup || snapshot.Inventory.Available)
            {
                // "Cleaning up storage…" must not survive the work it described, whether or
                // not the work finished.
                var parts = new List<string>();
                if (_ledger.Count > 0 || _exclusions.Count > 0)
                {
                    parts.Add(ResultSummary());
                }

                if (retryCleanup)
                {
                    parts.Add(settled ? "Storage cleanup finished." : "Some storage cleanup is still outstanding.");
                }

                if (_selectionNotice.Length > 0) parts.Add(_selectionNotice);

                _status.Text = string.Join(' ', parts.Where(part => part.Length > 0));
            }
        }
        catch (OperationCanceledException)
        {
            if (retryCleanup) _status.Text = "Storage cleanup may still be running. Refresh to check its progress.";
        }
        catch (Exception error)
        {
            _error = "The list could not be refreshed. Try Refresh.";
            WorkspaceViewModel.RecordFailure("capture.delete.refresh", error);
        }
        finally
        {
            _operation = null;
            _busy = false;
            Update();
            DrainRefresh();
        }
    }

    // ---- deletion ----------------------------------------------------------------------

    private async Task DeleteSelectedAsync()
    {
        // The guard is set before the first await, so a second Delete cannot enter preparation
        // or raise a second confirmation.
        if (_busy || _detached || _nested)
        {
            return;
        }

        var selected = _rows
            .Where(row => row.Checked && row.Eligible)
            .Select(row => new CaptureSelection(row.Session, row.Label))
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        // Where the keyboard was before confirmation took it. A reader who pressed Delete in
        // the list gets the list back; one who clicked the button is already at the footer and
        // is left there.
        var fromRow = FocusedRowIndex();

        _busy = true;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _progress.IsIndeterminate = true;
        _status.Text = "Checking selected captures…";
        Update();
        PreparedCaptures? prepared = null;
        var started = false;
        try
        {
            prepared = await _actions.Prepare(selected, operation.Token);
            if (_detached || operation.IsCancellationRequested)
            {
                return;
            }

            foreach (var item in selected) _exclusions.Remove(IdentityKey(item.Session));

            if (prepared.Excluded.Count > 0)
            {
                // The reader asked for a set; the honest answer is the part of it that can
                // still be confirmed, and why the rest left.
                _status.Text = $"{Counted.Captures(prepared.Excluded.Count)} could not be prepared and " +
                    $"{(prepared.Excluded.Count == 1 ? "was" : "were")} removed from this deletion. " +
                    prepared.Excluded[0].Reason;
                foreach (var excluded in prepared.Excluded)
                {
                    _exclusions[IdentityKey(excluded.Selection.Session)] = excluded;
                    var row = _rows.FirstOrDefault(row => SessionPath.Comparer.Equals(row.Session.Path, excluded.Selection.Session.Path));
                    if (row is not null)
                    {
                        row.Checked = false;
                        row.Note = excluded.Reason;
                    }
                }
            }

            if (prepared.Captures.Count == 0)
            {
                // Never an empty confirmation.
                return;
            }

            _nested = true;
            bool confirmed;
            try
            {
                confirmed = await _confirm(new CaptureDeleteConfirmation(prepared, _mobile));
            }
            finally
            {
                _nested = false;
            }

            if (!confirmed || _detached)
            {
                _status.Text = ResultSummary();
                _delete.Focus();
                return;
            }

            _deleting = true;
            started = true;
            _activeOperation = prepared.Operation;
            _lastProgressAnnouncement = 0;
            _stop.Content = "Stop";
            _stop.IsEnabled = true;
            _progress.IsIndeterminate = false;
            _progress.Minimum = 0;
            _progress.Maximum = prepared.Captures.Count;
            _progress.Value = 0;
            Update();
            var results = await _actions.Delete(prepared, new Progress<CaptureDeletionProgress>(Report), operation.Token);
            foreach (var result in results)
            {
                _ledger[IdentityKey(result.Capture)] = result;
            }

            RemoveDeletedRows();
            _status.Text = ResultSummary();
        }
        catch (OperationCanceledException)
        {
            if (started && prepared is not null)
            {
                RecoverKnownResults(prepared);
                _status.Text = ResultSummary();
            }
            else _status.Text = "Stopped before deletion.";
        }
        catch (Exception error)
        {
            if (started && prepared is not null)
            {
                // Something unexpected happened after work began. Known commits are preserved
                // and everything unresolved says so; the report is never replaced with nothing.
                RecoverKnownResults(prepared);
                _status.Text = ResultSummary();
            }

            _error = started
                ? "Something went wrong. Check the results before trying again."
                : "The selection could not be prepared. Refresh and select the captures again.";
            WorkspaceViewModel.RecordFailure("capture.delete.dialog", error);
        }
        finally
        {
            _deleting = false;
            _nested = false;
            _progress.IsIndeterminate = true;
            if (!_detached)
            {
                using var read = new CancellationTokenSource();
                _operation = read;
                try
                {
                    var snapshot = await _actions.Refresh(read.Token);
                    read.Token.ThrowIfCancellationRequested();
                    Apply(snapshot);
                }
                catch (OperationCanceledException)
                {
                    _error = "The list refresh was stopped. Try Refresh.";
                }
                catch (Exception error)
                {
                    _error = _error.Length > 0
                        ? _error + " The list could not be refreshed. Try Refresh."
                        : "The list could not be refreshed. Try Refresh.";
                    WorkspaceViewModel.RecordFailure("capture.delete.refresh", error);
                }
            }

            _operation = null;
            _busy = false;
            Update();
            if (!_detached)
            {
                if (fromRow >= 0)
                {
                    // The nearest survivor, then the previous one, then whatever action is
                    // left when the list has emptied.
                    RestoreFocus(fromRow);
                }
                else if (_rows.Count == 0)
                {
                    (_capture.IsVisible ? _capture : _close).Focus();
                }
                else if (started)
                {
                    (_delete.IsEnabled ? _delete : _close).Focus();
                }
            }

            DrainRefresh();
        }
    }

    /// <summary>
    /// Coalesces progress so hundreds of captures cannot enqueue hundreds of announcements.
    /// </summary>
    private void Report(CaptureDeletionProgress value)
    {
        if (_detached || !_deleting || value.Operation != _activeOperation || _operation?.IsCancellationRequested == true)
        {
            return;
        }

        _progress.Value = value.Completed;
        if (value.Phase == CaptureDeletionPhase.Resolved) return;
        var now = Environment.TickCount64;
        if (now - _lastProgressAnnouncement < 500)
        {
            return;
        }

        _lastProgressAnnouncement = now;
        _status.Text = value.Phase switch
        {
            CaptureDeletionPhase.Closing => $"Closing capture {value.Current:N0} of {value.Total:N0} · {value.Label}",
            CaptureDeletionPhase.Cleaning => $"Cleaning up storage · {value.Label}",
            _ => $"Deleting capture {value.Current:N0} of {value.Total:N0} · {value.Label}",
        };
    }

    private void RemoveDeletedRows()
    {
        var removed = _ledger.Values.Where(result => result.File.Removed)
            .Select(result => IdentityKey(result.Capture)).ToHashSet(SessionPath.Comparer);
        for (var index = _rows.Count - 1; index >= 0; index--)
        {
            if (removed.Contains(_rows[index].Key))
            {
                _rows.RemoveAt(index);
            }
        }

        Update();
    }

    private void RecoverKnownResults(PreparedCaptures request)
    {
        var resolved = new HashSet<string>(SessionPath.Comparer);
        try
        {
            foreach (var result in _actions.Resolved?.Invoke() ?? [])
            {
                var key = IdentityKey(result.Capture);
                _ledger[key] = result;
                resolved.Add(key);
            }
        }
        catch (Exception error) { WorkspaceViewModel.RecordFailure("capture.delete.ledger", error); }
        foreach (var capture in request.Captures)
        {
            var key = IdentityKey(capture);
            if (!resolved.Contains(key) && (!_ledger.TryGetValue(key, out var old) || !old.File.Committed))
                _ledger[key] = new(capture, new(capture.Target, CaptureDeleteOutcome.Unknown));
        }
        RemoveDeletedRows();
    }

    private void DrainRefresh()
    {
        if (!_refreshQueued || _detached)
        {
            return;
        }

        _refreshQueued = false;
        Dispatcher.UIThread.Post(RequestRefresh);
    }

    private string ResultSummary() => string.Join(' ', new[]
    {
        CaptureDeletionResult.Summary(_ledger.Values),
        _exclusions.Count > 0 ? $"{Counted.Captures(_exclusions.Count)} could not be included in deletion. Open Details for the reasons." : string.Empty,
    }.Where(part => part.Length > 0));

    private async Task ShowDetailsAsync()
    {
        if (_busy || _nested)
        {
            return;
        }

        _nested = true;
        try
        {
            var details = _ledger.Values.Select(result => result.Detail).ToList();
            details.AddRange(_exclusions.Values.Select(excluded =>
                excluded.Selection.Label + "\nNot included in deletion. " + excluded.Reason));
            if (_snapshot.Inventory.UnresolvedCleanup > 0)
            {
                details.Add("Unverified storage cleanup\nVisualCat cannot safely remove some leftover storage, so it " +
                    "is left alone. Retry storage cleanup checks it again without relaxing any safety check.");
            }

            await _details(new CaptureResultDetails(details, _mobile));
        }
        finally
        {
            _nested = false;
            _showDetails.Focus();
            DrainRefresh();
        }
    }

    /// <summary>
    /// Select all, whose cycle is defined by the model rather than by three-state visuals.
    /// </summary>
    /// <remarks>
    /// <c>IsThreeState</c> alone would let a pointer, a space bar or an automation client walk
    /// the control into a mixed state the model has no meaning for. Overriding the toggle means
    /// every route runs the one transition and the visual is derived from the result.
    /// </remarks>
    private sealed class SelectAllCheckBox(Action activate) : CheckBox
    {
        /// <summary>
        /// A derived control resolves its own type as its style key, so without this it had no
        /// control theme and drew nothing but its text: on the device the one checkbox that
        /// carries unchecked, mixed and checked had no box at all, while automation reported
        /// the state correctly. Essential state cannot live only in the accessibility tree.
        /// </summary>
        protected override Type StyleKeyOverride => typeof(CheckBox);

        protected override void Toggle() => activate();
    }

    private sealed class CaptureRow(string key, TemporarySessionInfo session, Action changed) : INotifyPropertyChanged
    {
        private bool _checked;
        private string _name = string.Empty;
        private string _label = string.Empty;
        private string _state = string.Empty;
        private string _note = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; } = key;

        public TemporarySessionInfo Session { get; set; } = session;

        public string Name { get => _name; set => Set(ref _name, value); }

        public string Label { get => _label; set => Set(ref _label, value); }

        public string State { get => _state; set => Set(ref _state, value); }

        public string Note { get => _note; set { if (Set(ref _note, value)) { Raise(nameof(HasNote)); } } }

        public bool HasNote => _note.Length > 0;

        public string SelectLabel => "Select " + _label;

        public string Speech => _label + ". " + _state + (_note.Length > 0 ? ". " + _note : string.Empty);

        public bool Eligible { get; set; }

        public bool ShowCheck { get; private set; }

        public bool CanCheck { get; private set; }

        public bool Checked
        {
            get => _checked;
            set
            {
                // The invariant, enforced where it cannot be forgotten: a check only ever
                // exists on a capture this operation is allowed to delete.
                value &= Eligible;
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Raise(nameof(Checked));
                changed();
            }
        }

        internal void Present(bool select, bool enabled)
        {
            ShowCheck = select;
            CanCheck = Eligible && enabled;
            Raise(null);
        }

        public override string ToString() => _label;

        private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        private bool Set(ref string field, string value)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return false;
            }

            field = value;
            Raise(null);
            return true;
        }
    }
}

/// <summary>
/// The frozen set, what happens to it, and a Cancel that is the safe default.
/// </summary>
/// <remarks>
/// A scrollable body over pinned actions, because long names, a review-all list and the
/// consequences together do not fit a fixed stack at an enlarged text scale.
/// </remarks>
internal sealed class CaptureDeleteConfirmation : DialogBody<bool>
{
    private readonly Button _cancel;

    internal CaptureDeleteConfirmation(PreparedCaptures request, bool mobile)
        : base($"Delete {Counted.Captures(request?.Captures.Count ?? 0)}?")
    {
        ArgumentNullException.ThrowIfNull(request);
        PreferredSize = new Size(620, 520);
        FontSize = TextScale.Of(14);
        MinimumSize = new Size(320, 300);
        ScrollsInternally = true;
        var body = new StackPanel { Spacing = 14 };
        var count = request.Captures.Count;
        var known = request.Captures.Where(capture => capture.Target.SizeEstimate is >= 0).ToArray();
        var estimate = RecentSessionsDialog.FormatBytes(
            RecentCapturePanel.SumSizes(known.Select(capture => capture.Target.SizeEstimate!.Value)));
        var size = known.Length == 0 ? "size unavailable"
            : known.Length == count ? $"about {estimate}" : $"at least about {estimate}";
        body.Children.Add(Copy(
            (count == 1 ? $"This capture ({size})" : $"These {count:N0} captures ({size})") +
            " will be permanently removed from temporary storage. This cannot be undone."));
        if (known.Length != count)
        {
            body.Children.Add(Copy("Some sizes are unavailable."));
        }

        foreach (var capture in request.Captures.Take(PreviewNames))
        {
            body.Children.Add(Copy("• " + capture.Label));
        }

        if (count > PreviewNames)
        {
            body.Children.Add(Copy($"And {count - PreviewNames:N0} more captures."));
            var review = new Expander
            {
                Header = $"Review all {count:N0}",
                MinHeight = TouchTarget.SelfSized(mobile),
                MinWidth = TouchTarget.SelfSized(mobile),
            };
            var all = new ListBox { ItemsSource = request.Captures.Select(capture => capture.Label).ToArray(), Height = 240 };
            all.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((label, _) => Copy(label ?? string.Empty));
            AutomationProperties.SetName(all, "Every capture selected for deletion");
            review.Content = all;
            body.Children.Add(review);
        }

        // Always disclosed, even when the preview sees no open tab: one can be opened between
        // this sentence and the deletion.
        body.Children.Add(Copy(count == 1
            ? "Any open tab for this capture will close first."
            : "Any open tabs for these captures will close first."));
        var open = request.Captures.Count(capture => capture.Open);
        if (open > 0)
        {
            body.Children.Add(Copy(open == 1
                ? "1 of these captures is currently open."
                : $"{open:N0} of these captures are currently open."));
        }

        body.Children.Add(Copy(
            "Saved copies, original files and shared archives will not be deleted. Storage cleanup may finish later."));

        _cancel = ActionButton("Cancel", mobile, () => Complete(false));

        // Enter must not delete. The safe action is the default and takes the initial focus;
        // deleting stays one deliberate activation away, and reads after the way out.
        _cancel.IsDefault = true;
        var delete = ActionButton("Delete permanently", mobile, () => Complete(true));
        var decisions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        decisions.Children.Add(_cancel);
        decisions.Children.Add(delete);
        Content = SheetForm.Build(body, decisions, new Thickness(12));
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Dismiss();
                args.Handled = true;
            }
        };
    }

    private const int PreviewNames = 5;

    protected override void OnPresented() => _cancel.Focus();

    internal static TextBlock Copy(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    internal static Button ActionButton(string label, bool mobile, Action activate)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = TouchTarget.SelfSized(mobile),
            MinWidth = TouchTarget.SelfSized(mobile),
            Margin = new Thickness(0, 3, 8, 0),
        };
        button.Click += (_, _) => activate();
        return button;
    }
}

/// <summary>Every result that needs explaining, in full, with one way out.</summary>
internal sealed class CaptureResultDetails : DialogBody<bool>
{
    private readonly Button _close;

    internal CaptureResultDetails(IReadOnlyList<string> details, bool mobile)
        : base("Deletion results")
    {
        ScrollsInternally = true;
        PreferredSize = new Size(620, 500);
        FontSize = TextScale.Of(14);
        MinimumSize = new Size(320, 280);
        var list = new ListBox { ItemsSource = details };
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) =>
            new Border { Padding = new Thickness(4, 10), Child = CaptureDeleteConfirmation.Copy(text ?? string.Empty) });
        AutomationProperties.SetName(list, "Results for every selected capture");
        _close = CaptureDeleteConfirmation.ActionButton("Close", mobile, () => Complete(true));
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(12) };
        root.Children.Add(list);
        Grid.SetRow(_close, 1);
        root.Children.Add(_close);
        Content = root;
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Dismiss();
                args.Handled = true;
            }
        };
    }

    protected override void OnPresented() => _close.Focus();
}
