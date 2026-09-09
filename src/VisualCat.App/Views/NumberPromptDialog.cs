using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;

namespace VisualCat.App.Views;

/// <summary>
/// Asks for one number inside a stated range.
/// </summary>
/// <remarks>
/// <para>
/// Built for the search counter. `3,579 / 7,181` told the reader exactly where they were among
/// 7,181 matches and was not a control, so the only way to reach match 12 was to step to it —
/// and with the counter opening at the caret's position rather than at the first match, that
/// could be thousands of taps (V2-07). The stepper and the two edge buttons cover "next" and
/// "the ends"; this covers "that one".
/// </para>
/// <para>
/// A <see cref="NumericUpDown"/> rather than a free text box, because the answer is bounded on
/// both sides and the control can say so itself — and because
/// <see cref="SheetForm.PrepareSpinButtons"/> already gives its two spin buttons names and a
/// touch target, which is the part a phone gets wrong when this is built by hand.
/// </para>
/// <para>
/// The number is read back from the edited <em>text</em>, not from the control's clamped and
/// rounded <c>Value</c>: a fractional or overflowing entry has to be rejected and explained,
/// not silently turned into a different, valid match number.
/// </para>
/// </remarks>
internal sealed class NumberPromptDialog : DialogBody<long?>, IDisposable
{
    private readonly NumericUpDown _value;
    private readonly TextBlock _range;
    private readonly TextBlock _validation;
    private readonly Button _confirm;
    private readonly SearchMatchPromptModel _model;
    private readonly CancellationTokenSource _lifetime = new();
    private long? _fixedMinimum;
    private long? _fixedMaximum;
    private int _disposed;

    /// <param name="title">The dialog's own heading.</param>
    /// <param name="question">The one line above the field.</param>
    /// <param name="initial">Where the field starts.</param>
    /// <param name="model">The applied search the typed number is counted against.</param>
    internal NumberPromptDialog(string title, string question, long initial, SearchMatchPromptModel model)
        : base(title)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        PreferredSize = new Size(420, 280);
        MinimumSize = new Size(340, 240);
        var mobile = DialogComposition.Mobile;

        // Deliberately unbounded at the control: the bounds move while a capture grows, and
        // "minimum 1, maximum 0" is not a range a control can hold. Validation belongs to the
        // model, which knows the applied result the number has to be valid against.
        _value = new NumericUpDown
        {
            Minimum = 1,
            Maximum = long.MaxValue,
            Increment = 1,
            // Keep a fractional edit visible when the field loses focus. Avalonia commits a
            // NumericUpDown before the button Click handler runs; a whole-number-only display
            // format therefore changed "1.5" into "2" before validation could read it. The
            // optional fractional places preserve the reader's input while whole ordinals still
            // render without a decimal suffix. SearchMatchPromptModel remains the authority that
            // rejects anything other than an in-range Int64.
            FormatString = "0.############################",
            Value = Math.Max(1, initial),
            MinHeight = TouchTarget.SelfSized(mobile),
        };
        AutomationProperties.SetName(_value, question);
        // A short label: the spin buttons prefix it, and "Increase Which of the 7,181
        // matches?" is what a screen reader reads when the question is passed through.
        SheetForm.PrepareSpinButtons(_value, "match number");

        _range = new TextBlock
        {
            Text = model.RangeText,
            FontSize = TextScale.Of(11),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };
        _validation = new TextBlock
        {
            FontSize = TextScale.Of(11),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(LevelPalette.InkOf(
                LogLevel.Error,
                ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light)),
            IsVisible = false,
        };
        AutomationProperties.SetLiveSetting(_validation, AutomationLiveSetting.Assertive);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinHeight = TouchTarget.SelfSized(mobile),
            MinWidth = TouchTarget.SelfSized(mobile),
        };
        cancel.Click += (_, _) => Complete(null);

        // Two characters size themselves to about 36 dp, which the device measured. Height
        // alone is not the floor: both edges are.
        _confirm = new Button
        {
            Content = "Go",
            IsDefault = true,
            IsEnabled = model.CanConfirm,
            MinHeight = TouchTarget.SelfSized(mobile),
            MinWidth = TouchTarget.SelfSized(mobile),
        };
        _confirm.Click += async (_, _) => await ConfirmAsync();
        _value.ValueChanged += (_, _) => ClearValidation();

        Content = SheetForm.Build(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = question,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = TextScale.Of(13),
                    },
                    _value,
                    _range,
                    _validation,
                },
            },
            SheetForm.Decision(null, cancel, _confirm),
            new Thickness(16));

        _model.PropertyChanged += OnModelChanged;
        _model.CloseRequested += OnCloseRequested;
        // A prompt taken down by its host, by Back, or by the shell closing must stop
        // observing a model that outlives it: the model belongs to the workspace.
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    /// <summary>Creates the original fixed-bound number prompt for non-search callers.</summary>
    /// <param name="title">The dialog's own heading.</param>
    /// <param name="question">The one line above the field.</param>
    /// <param name="initial">Where the field starts, clamped into range.</param>
    /// <param name="minimum">The smallest acceptable answer.</param>
    /// <param name="maximum">The largest acceptable answer.</param>
    internal NumberPromptDialog(
        string title,
        string question,
        long initial,
        long minimum,
        long maximum)
        : this(
            title,
            question,
            Math.Clamp(initial, minimum, maximum),
            new SearchMatchPromptModel(
                new VisualCat.Domain.Queries.QueryIdentity(Guid.Empty, 0, "fixed-number-prompt", 0),
                Math.Max(0, maximum)))
    {
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must not be less than minimum.");
        }

        _fixedMinimum = minimum;
        _fixedMaximum = maximum;
        _value.Minimum = minimum;
        _value.Maximum = maximum;
        _value.Value = Math.Clamp(initial, minimum, maximum);
        _range.Text = $"{minimum:N0} to {maximum:N0}";
        _confirm.IsEnabled = true;
    }

    /// <inheritdoc />
    protected override void OnPresented() => _value.Focus();

    /// <summary>Stops observing the workspace's model.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _model.PropertyChanged -= OnModelChanged;
        _model.CloseRequested -= OnCloseRequested;
        _lifetime.Dispose();
    }

    private async Task ConfirmAsync()
    {
        // The reader's own text, not the control's committed Value: the field commits on
        // focus loss, and a clamped commit would answer a question they did not ask.
        var typed = _value.Text is { Length: > 0 } text
            ? text
            : _value.Value?.ToString("0", CultureInfo.CurrentCulture);
        if (_fixedMinimum is { } fixedMinimum && _fixedMaximum is { } fixedMaximum)
        {
            if (!TryReadFixed(typed, fixedMinimum, fixedMaximum, out var fixedOrdinal))
            {
                ShowValidation();
                return;
            }

            Complete(fixedOrdinal);
            return;
        }

        if (!_model.TryConfirm(typed, out var ordinal))
        {
            ShowValidation();
            return;
        }

        try
        {
            if (!await _model.ResolveSelectionAsync(_lifetime.Token))
            {
                ShowValidation();
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        Complete(ordinal);
    }

    private void ShowValidation()
    {
        _validation.Text = ValidationMessage();
        _validation.IsVisible = true;
        AutomationProperties.SetName(_validation, _validation.Text);
        AutomationProperties.SetHelpText(_value, _validation.Text);
        _value.Focus();
    }

    private void ClearValidation()
    {
        if (!_validation.IsVisible)
        {
            return;
        }

        _validation.IsVisible = false;
        _validation.Text = string.Empty;
        AutomationProperties.SetHelpText(_value, null);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        _range.Text = _model.RangeText;
        _confirm.IsEnabled = _model.CanConfirm;
        if (_validation.IsVisible)
        {
            _validation.Text = ValidationMessage();
            AutomationProperties.SetName(_validation, _validation.Text);
        }
    }

    private void OnCloseRequested(string reason)
    {
        _ = reason;
        Complete(null);
    }

    /// <summary>Confirms from a test without a pointer or a committed spin value.</summary>
    internal async Task ConfirmForTest(string typed)
    {
        _value.Text = typed;
        await ConfirmAsync();
    }

    /// <summary>The sentence currently shown beneath the field, for tests.</summary>
    internal string RangeTextForTest => _range.Text ?? string.Empty;

    /// <summary>The visible validation sentence, or null when the field is accepted.</summary>
    internal string? ValidationForTest => _validation.IsVisible ? _validation.Text : null;

    /// <summary>Whether <em>Go</em> can currently act, for tests.</summary>
    internal bool CanConfirmForTest => _confirm.IsEnabled;

    private string ValidationMessage() =>
        _fixedMinimum is { } minimum && _fixedMaximum is { } maximum
            ? $"Enter a whole number from {minimum:N0} to {maximum:N0}."
            : _model.ValidationMessage;

    private static bool TryReadFixed(string? text, long minimum, long maximum, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (!long.TryParse(candidate, NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value) &&
            !long.TryParse(candidate, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value >= minimum && value <= maximum;
    }
}
