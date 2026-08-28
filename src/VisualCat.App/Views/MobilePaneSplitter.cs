using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Timeline;

namespace VisualCat.App.Views;

/// <summary>Which of the two phone boundaries a divider moves.</summary>
internal enum MobilePaneAxis
{
    /// <summary>The stacked boundary: the plot above, the details below.</summary>
    Rows,

    /// <summary>The side-by-side boundary: the plot left, the details right.</summary>
    Columns,
}

/// <summary>
/// A thin mobile divider with a phone-sized hit target. It reports intent and never mutates
/// grid tracks itself; <see cref="MobilePaneAllocator"/> remains the only size authority.
/// </summary>
internal sealed class MobilePaneSplitter : Thumb, ICustomHitTest
{
    internal const double LaneExtent = 12;
    internal const double HitTargetExtent = 48;
    internal const double GripZoneLength = 96;
    internal const double KeyboardIncrement = 16;

    /// <summary>
    /// How much of the target is grabbable along the whole boundary. It is the visible gap
    /// between the two panes and nothing else, so grabbing the line away from the grip costs
    /// neither neighbour any of its own area.
    /// </summary>
    internal const double LaneBandExtent = 20;

    private readonly MobilePaneAxis _axis;
    private IBrush _grip = new SolidColorBrush(Color.Parse("#9BB3CB"));
    private IBrush _activeGrip = new SolidColorBrush(Color.Parse("#59B8FF"));
    private IBrush _line = new SolidColorBrush(Color.Parse("#26374A"));
    private bool _dragging;
    private bool _keyboardFocused;
    private SplitterPeer? _peer;
    private double _minimumValue;
    private double _maximumValue = 100;
    private double _value = 50;

    // Where the press landed, in a space that stands still while the panes resize.
    private Visual? _dragReference;
    private double _pressOffset;
    private double _lastReportedOffset;

    internal MobilePaneSplitter(MobilePaneAxis axis)
    {
        _axis = axis;
        if (axis == MobilePaneAxis.Rows)
        {
            Height = HitTargetExtent;
            MinHeight = HitTargetExtent;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }
        else
        {
            Width = HitTargetExtent;
            MinWidth = HitTargetExtent;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }

        Background = Brushes.Transparent;
        ClipToBounds = false;
        Focusable = true;

        AutomationProperties.SetName(this, "Resize plot and details");
        AutomationProperties.SetHelpText(
            this,
            axis == MobilePaneAxis.Rows
                ? "Drag up or down to resize the plot and details. Double tap or press Home to restore automatic sizing."
                : "Drag left or right to resize the plot and details. Double tap or press Home to restore automatic sizing.");
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Slider);
        ToolTip.SetTip(this, "Drag to resize, double tap or Home for automatic sizing");

        DoubleTapped += (_, _) => ResetRequested?.Invoke();
        KeyDown += OnKeyDown;

        // A touch drag leaves the control focused, and a grip that stays lit afterwards reads
        // as a selection rather than as a boundary — next to the tab strip's accent underline
        // it reads as the wrong selection. Only a keyboard arrival needs to be shown.
        GotFocus += (_, eventArgs) => SetKeyboardFocus(eventArgs.NavigationMethod != NavigationMethod.Pointer);
        LostFocus += (_, _) => SetKeyboardFocus(false);
    }

    /// <summary>The pointer's total travel since the press, in the parent's space.</summary>
    internal event Action<double>? DragOffsetChanged;

    internal event Action<double>? NudgeRequested;
    internal event Action? ResetRequested;
    internal event Action<double>? AutomationValueRequested;

    internal MobilePaneAxis Axis => _axis;

    internal void ApplyTheme(bool dark)
    {
        var accent = WorkspacePalette.Accent(dark);
        var muted = WorkspacePalette.TextMuted(dark);
        _grip = new SolidColorBrush(Color.FromArgb(255, muted.R, muted.G, muted.B));
        _activeGrip = new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B));
        var line = WorkspacePalette.BorderLine(dark);
        _line = new SolidColorBrush(Color.FromArgb(dark ? (byte)150 : (byte)120, line.R, line.G, line.B));
        InvalidateVisual();
    }

    internal void SetRange(double minimumShare, double maximumShare, double resolvedShare)
    {
        var oldValue = _value;
        _minimumValue = Percent(minimumShare);
        _maximumValue = Math.Max(_minimumValue, Percent(maximumShare));
        _value = Math.Clamp(Percent(resolvedShare), _minimumValue, _maximumValue);
        if (Math.Abs(oldValue - _value) > 0.001)
        {
            _peer?.NotifyValueChanged(oldValue, _value);
        }
    }

    internal void SetInteractive(bool interactive)
    {
        IsVisible = interactive;
        IsEnabled = interactive;
        IsHitTestVisible = interactive;
        Focusable = interactive;
        AutomationProperties.SetAccessibilityView(
            this,
            interactive ? AccessibilityView.Content : AccessibilityView.Raw);
    }

    /// <summary>
    /// The target is a cross rather than a rectangle: the whole boundary line is grabbable,
    /// and only its middle reaches the full 48 dp. The panes on either side therefore keep
    /// every pixel of their own area outside the marked grip.
    /// </summary>
    public bool HitTest(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
        {
            return false;
        }

        var across = _axis == MobilePaneAxis.Rows ? point.Y : point.X;
        var along = _axis == MobilePaneAxis.Rows ? point.X : point.Y;
        var acrossCentre = (_axis == MobilePaneAxis.Rows ? Bounds.Height : Bounds.Width) / 2;
        var alongCentre = (_axis == MobilePaneAxis.Rows ? Bounds.Width : Bounds.Height) / 2;
        return Math.Abs(across - acrossCentre) <= LaneBandExtent / 2 ||
               Math.Abs(along - alongCentre) <= GripZoneLength / 2;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double gripLength = 56;
        const double gripThickness = 5;
        var brush = _dragging || _keyboardFocused ? _activeGrip : _grip;

        // The line says where the boundary is; the grip says it can be moved.
        if (_axis == MobilePaneAxis.Rows)
        {
            var centre = Bounds.Height / 2;
            context.FillRectangle(_line, new Rect(0, centre - 0.5, Bounds.Width, 1));
            context.DrawRectangle(
                brush,
                null,
                new Rect(
                    Math.Max(0, (Bounds.Width - gripLength) / 2),
                    Math.Max(0, centre - (gripThickness / 2)),
                    Math.Min(gripLength, Bounds.Width),
                    Math.Min(gripThickness, Bounds.Height)),
                radiusX: gripThickness / 2,
                radiusY: gripThickness / 2);
            return;
        }

        var column = Bounds.Width / 2;
        context.FillRectangle(_line, new Rect(column - 0.5, 0, 1, Bounds.Height));
        context.DrawRectangle(
            brush,
            null,
            new Rect(
                Math.Max(0, column - (gripThickness / 2)),
                Math.Max(0, (Bounds.Height - gripLength) / 2),
                Math.Min(gripThickness, Bounds.Width),
                Math.Min(gripLength, Bounds.Height)),
            radiusX: gripThickness / 2,
            radiusY: gripThickness / 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        // Thumb measures DragDelta inside this control, and this control travels with the
        // boundary it is moving. That vector only equals the finger's movement when a layout
        // pass has landed between two pointer events, so a fast drag on a busy phone either
        // counts it twice or loses it. Track the pointer in the parent's space, which stands
        // still, and report absolute travel instead.
        _dragReference = this.GetVisualParent() ?? (Visual?)TopLevel.GetTopLevel(this);
        _pressOffset = OffsetIn(eventArgs, _dragReference);
        _lastReportedOffset = 0;
        base.OnPointerPressed(eventArgs);
    }

    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        if (_dragging)
        {
            Report(eventArgs);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        // A flick can have its moves coalesced away, leaving the release as the only event
        // that says where the finger finished. Apply it before the drag closes.
        if (_dragging)
        {
            Report(eventArgs);
        }

        base.OnPointerReleased(eventArgs);
    }

    protected override void OnDragStarted(VectorEventArgs eventArgs)
    {
        _dragging = true;
        InvalidateVisual();
        base.OnDragStarted(eventArgs);
    }

    protected override void OnDragCompleted(VectorEventArgs eventArgs)
    {
        _dragging = false;
        _dragReference = null;
        InvalidateVisual();
        base.OnDragCompleted(eventArgs);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _peer ??= new SplitterPeer(this);

    private void SetKeyboardFocus(bool focused)
    {
        if (_keyboardFocused == focused)
        {
            return;
        }

        _keyboardFocused = focused;
        InvalidateVisual();
    }

    private void Report(PointerEventArgs eventArgs)
    {
        if (_dragReference is not { } reference)
        {
            return;
        }

        var offset = OffsetIn(eventArgs, reference) - _pressOffset;
        if (!double.IsFinite(offset) || Math.Abs(offset - _lastReportedOffset) < 0.01)
        {
            return;
        }

        _lastReportedOffset = offset;
        DragOffsetChanged?.Invoke(offset);
    }

    private double OffsetIn(PointerEventArgs eventArgs, Visual? reference)
    {
        if (reference is null)
        {
            return 0;
        }

        var position = eventArgs.GetPosition(reference);
        return _axis == MobilePaneAxis.Rows ? position.Y : position.X;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        var back = _axis == MobilePaneAxis.Rows ? Key.Up : Key.Left;
        var forward = _axis == MobilePaneAxis.Rows ? Key.Down : Key.Right;
        if (eventArgs.Key == back)
        {
            NudgeRequested?.Invoke(-KeyboardIncrement);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == forward)
        {
            NudgeRequested?.Invoke(KeyboardIncrement);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Home)
        {
            ResetRequested?.Invoke();
            eventArgs.Handled = true;
        }
    }

    private void SetAutomationValue(double value)
    {
        if (!IsEffectivelyEnabled || !double.IsFinite(value))
        {
            return;
        }

        var share = Math.Clamp(value, _minimumValue, _maximumValue) / 100;
        if (Dispatcher.UIThread.CheckAccess())
        {
            AutomationValueRequested?.Invoke(share);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AutomationValueRequested?.Invoke(share));
        }
    }

    private static double Percent(double share) =>
        double.IsFinite(share) ? Math.Clamp(share * 100, 0, 100) : 0;

    private sealed class SplitterPeer(MobilePaneSplitter owner)
        : ControlAutomationPeer(owner), IRangeValueProvider
    {
        internal void NotifyValueChanged(double oldValue, double newValue) =>
            RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Slider;

        protected override string GetClassNameCore() => nameof(MobilePaneSplitter);

        bool IRangeValueProvider.IsReadOnly => false;
        double IRangeValueProvider.Minimum => owner._minimumValue;
        double IRangeValueProvider.Maximum => owner._maximumValue;
        double IRangeValueProvider.Value => owner._value;
        double IRangeValueProvider.LargeChange => 10;
        double IRangeValueProvider.SmallChange => 2;
        void IRangeValueProvider.SetValue(double value) => owner.SetAutomationValue(value);
    }
}
