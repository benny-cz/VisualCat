using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.Application.UseCases;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;

namespace VisualCat.App.Views;

/// <summary>An editable import review whose display and accepted settings share one sample generation.</summary>
internal sealed class ImportPreviewDialog : DialogBody<IngestSettings>, IDisposable
{
    private static readonly FormatChoice[] FormatChoices =
    [
        new("Auto-detect", null),
        new("Thread time", LogcatFormat.ThreadTime),
        new("Time", LogcatFormat.Time),
        new("Brief", LogcatFormat.Brief),
        new("Long", LogcatFormat.LongFormat),
        new("Epoch", LogcatFormat.Epoch),
    ];

    private readonly ImportPreviewSample _sample;
    private readonly TimestampPolicy _initialPolicy;
    private readonly bool _portableRawRequired;
    private readonly string _yearHelp;
    private const string TimeZoneHelp = "Enter a time-zone ID or exact display name; matching known zones are listed below.";
    private readonly ComboBox _format = new() { ItemsSource = FormatChoices };
    private readonly TextBox _year = new();
    private readonly TextBox _timeZone = new();
    private readonly ComboBox _knownZones = new();
    private readonly ZoneChoice[] _zoneChoices;
    private readonly CheckBox _templates = new() { Content = "Mine deterministic message templates", IsChecked = true };
    private readonly CheckBox _portableRaw = new() { Content = "Embed raw source immediately" };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.82 };
    private readonly TextBlock _validation = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private readonly TextBlock _yearError = InlineError();
    private readonly TextBlock _timeZoneError = InlineError();
    private readonly Button _import;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _evaluationGate = new();
    private readonly HashSet<Task> _evaluationTasks = [];
    private CancellationTokenSource? _evaluationCancellation;
    private IngestSettings? _evaluatedSettings;
    private Task? _releaseTask;
    private int _generation;
    private int _disposed;

    internal ImportPreviewDialog(
        string displayName,
        ImportPreviewSample sample,
        ImportPreview initial,
        bool portableRawRequired)
        : base($"Import preview — {displayName}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        ArgumentNullException.ThrowIfNull(initial);
        _initialPolicy = initial.TimestampPolicy;
        _yearHelp = $"Leave blank to use the source reference date {_initialPolicy.ReferenceInstant:yyyy-MM-dd}, or enter 1970 through 9999.";
        _portableRawRequired = portableRawRequired;
        var mobile = OperatingSystem.IsAndroid();
        PreferredSize = new Size(720, 660);
        MinimumSize = mobile ? new Size(300, 340) : new Size(390, 400);
        ScrollsInternally = true;

        _format.SelectedItem = FormatChoices.FirstOrDefault(choice => choice.Format == initial.FormatOverride) ?? FormatChoices[0];
        _year.Text = initial.TimestampPolicy.AssumedYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _timeZone.Text = initial.TimestampPolicy.TimeZoneId;
        _zoneChoices = BuildZoneChoices();
        _knownZones.PlaceholderText = "Matching time zones";
        RefreshZoneChoices(_timeZone.Text);
        _portableRaw.IsChecked = portableRawRequired;
        _portableRaw.IsEnabled = !portableRawRequired;
        if (portableRawRequired)
        {
            AutomationProperties.SetHelpText(
                _portableRaw,
                "Required so this session keeps its source after the temporary copy is removed.");
        }

        foreach (var (control, name) in new (Control, string)[]
        {
            (_format, "Import format"),
            (_year, "Assumed year"),
            (_timeZone, "Time zone identifier"),
            (_knownZones, "Known time zones"),
            (_templates, "Template mining"),
            (_portableRaw, "Raw source embedding"),
        })
        {
            AutomationProperties.SetName(control, name);
            if (mobile)
            {
                control.MinHeight = 48;
            }
        }

        _format.SelectionChanged += (_, _) => QueueEvaluation();
        _year.TextChanged += (_, _) => QueueEvaluation();
        _timeZone.TextChanged += (_, _) =>
        {
            RefreshZoneChoices(_timeZone.Text);
            QueueEvaluation();
        };
        _knownZones.SelectionChanged += (_, _) =>
        {
            if (_knownZones.SelectedItem is ZoneChoice zone)
            {
                _timeZone.Text = zone.Id;
            }
        };
        _templates.IsCheckedChanged += (_, _) => QueueEvaluation(immediate: true);
        _portableRaw.IsCheckedChanged += (_, _) => QueueEvaluation(immediate: true);
        AutomationProperties.SetHelpText(_year, _yearHelp);
        AutomationProperties.SetHelpText(_timeZone, TimeZoneHelp);
        AutomationProperties.SetLiveSetting(_yearError, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(_timeZoneError, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(_validation, AutomationLiveSetting.Assertive);

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = mobile ? 48 : 0 };
        cancel.Click += (_, _) => Complete(null);
        _import = new Button
        {
            Content = "Import",
            IsDefault = true,
            IsEnabled = false,
            MinHeight = mobile ? 48 : 0,
        };
        _import.Click += (_, _) =>
        {
            if (_evaluatedSettings is { } settings)
            {
                Complete(settings);
            }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, _import },
        };
        var options = new Expander
        {
            Header = "Import options",
            IsExpanded = false,
            Content = BuildOptions(),
        };
        var scroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = displayName,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = $"Preview of up to the first 200 lines · {_sample.CompleteLines.Count:N0} complete lines · {_sample.RetainedBytes / 1024d:N1} KiB retained",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.74,
                    },
                    _summary,
                    _warnings,
                    options,
                    _validation,
                },
            },
        };
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(16),
        };
        layout.Children.Add(scroll);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        Content = layout;
        Publish(
            initial,
            BuildSettings(
                initial.TimestampPolicy,
                initial.FormatOverride,
                mineTemplates: _templates.IsChecked == true,
                portableRaw: _portableRaw.IsChecked == true));
    }

    protected override void OnPresented() =>
        Dispatcher.UIThread.Post(() => _import.Focus(), DispatcherPriority.Input);

    internal override void Dismiss()
    {
        base.Dismiss();
        Dispose();
    }

    internal override void ForceDismiss()
    {
        base.ForceDismiss();
        Dispose();
    }

    private StackPanel BuildOptions()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(Field("Format", _format));
        stack.Children.Add(Field(
            "Assumed year (optional)",
            _year,
            $"Blank uses the source reference date ({_initialPolicy.ReferenceInstant:yyyy-MM-dd}).",
            _yearError));
        stack.Children.Add(Field(
            "Time zone",
            _timeZone,
            "Search by time-zone ID or display name, then choose a match below. Local and UTC are listed first.",
            _timeZoneError));
        stack.Children.Add(_knownZones);
        stack.Children.Add(_templates);
        stack.Children.Add(_portableRaw);
        if (_portableRawRequired)
        {
            stack.Children.Add(Help("Required so this session keeps its source after the temporary copy is removed."));
        }

        return stack;
    }

    private void QueueEvaluation(bool immediate = false)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var generation = ++_generation;
        _evaluationCancellation?.Cancel();
        _evaluationCancellation?.Dispose();
        _evaluationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _evaluationCancellation.Token;
        _evaluatedSettings = null;
        _import.IsEnabled = false;
        SetInlineError(_year, _yearError, null);
        SetInlineError(_timeZone, _timeZoneError, null);
        _validation.Text = "Updating preview…";
        var evaluation = EvaluateAsync(generation, immediate, token);
        lock (_evaluationGate)
        {
            _evaluationTasks.Add(evaluation);
        }

        _ = ForgetWhenCompleteAsync(evaluation);
    }

    private async Task ForgetWhenCompleteAsync(Task evaluation)
    {
        try
        {
            await evaluation.ConfigureAwait(false);
        }
        finally
        {
            lock (_evaluationGate)
            {
                _evaluationTasks.Remove(evaluation);
            }
        }
    }

    private async Task EvaluateAsync(int generation, bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            var input = await Dispatcher.UIThread.InvokeAsync(ReadInput);
            if (input.Error is { } error)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation == _generation)
                    {
                        _validation.Text = string.Empty;
                        var field = input.ErrorTarget == InputErrorTarget.Year ? _year : _timeZone;
                        var label = input.ErrorTarget == InputErrorTarget.Year ? _yearError : _timeZoneError;
                        SetInlineError(field, label, error);
                    }
                });
                return;
            }

            var preview = await Task.Run(
                () => ImportSampleService.Evaluate(
                    _sample,
                    input.Policy!,
                    input.Format,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var settings = BuildSettings(
                input.Policy!,
                input.Format,
                input.MineTemplates,
                input.PortableRaw);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _generation && !cancellationToken.IsCancellationRequested)
                {
                    Publish(preview, settings);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _generation)
                {
                    _validation.Text = $"Could not update the preview. {Presentation.WorkspaceViewModel.FriendlyMessage(exception)}";
                }
            });
        }
    }

    private Input ReadInput()
    {
        var yearText = _year.Text?.Trim();
        int? year = null;
        if (!string.IsNullOrEmpty(yearText) &&
            (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1970 or > 9999))
        {
            return new Input(null, null, "Enter a year from 1970 to 9999, or leave it blank.", InputErrorTarget.Year);
        }
        else if (!string.IsNullOrEmpty(yearText))
        {
            year = int.Parse(yearText, CultureInfo.InvariantCulture);
        }

        var timeZoneId = _timeZone.Text?.Trim();
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return new Input(null, null, "Choose a valid time zone.", InputErrorTarget.TimeZone);
        }

        var resolvedTimeZoneId = timeZoneId;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            var displayMatches = _zoneChoices
                .Where(zone => string.Equals(zone.Display, timeZoneId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (displayMatches.Length != 1)
            {
                return new Input(null, null, "Choose a valid time zone.", InputErrorTarget.TimeZone);
            }

            // Human-readable display text is accepted for discovery, but persisted ingest
            // evidence always records the platform's canonical zone identifier.
            resolvedTimeZoneId = displayMatches[0].Id;
        }

        var format = (_format.SelectedItem as FormatChoice)?.Format;
        return new Input(
            _initialPolicy with { AssumedYear = year, TimeZoneId = resolvedTimeZoneId },
            format,
            null,
            InputErrorTarget.None,
            MineTemplates: _templates.IsChecked == true,
            PortableRaw: _portableRaw.IsChecked == true);
    }

    private IngestSettings BuildSettings(
        TimestampPolicy policy,
        LogcatFormat? format,
        bool mineTemplates,
        bool portableRaw) => new(
        format,
        "utf-8",
        policy,
        new TemplateSettings(Enabled: mineTemplates),
        PortableRaw: _portableRawRequired || portableRaw);

    private void Publish(ImportPreview preview, IngestSettings settings)
    {
        var detection = preview.Detection.PrimaryFormat == LogcatFormat.Unknown
            ? "No supported sample format detected"
            : $"Detected sample format: {FriendlyFormat(preview.Detection.PrimaryFormat)} ({preview.Detection.Confidence:P0} confidence)";
        var parsing = preview.FormatOverride is { } forced
            ? $"Parsing preview as {FriendlyFormat(forced)} · detection confidence remains {preview.Detection.Confidence:P0}"
            : "Parsing preview with auto-detection";
        var parsed = preview.OutcomeCounts.GetValueOrDefault(ParseOutcomeKind.ParsedEntry);
        var unknown = preview.OutcomeCounts.GetValueOrDefault(ParseOutcomeKind.UnknownLine);
        var rejected = preview.OutcomeCounts.GetValueOrDefault(ParseOutcomeKind.RejectedCandidate);
        var (displayZone, displayZoneLabel) = ResolveSampleZone(preview.TimestampPolicy.TimeZoneId);
        var span = preview.FirstInstant is { } first && preview.LastInstant is { } last
            ? $"Sample time span: {FormatSampleInstant(first, displayZone)} — " +
              $"{FormatSampleInstant(last, displayZone)} · {displayZoneLabel}"
            : $"No timed entries in sample · {displayZoneLabel}";
        var year = preview.TimestampPolicy.AssumedYear is { } assumed
            ? $"Assumed year {assumed}"
            : $"Year follows source reference date {preview.TimestampPolicy.ReferenceInstant:yyyy-MM-dd}";
        // With no complete line in the sample there is no denominator, so the outcome row
        // says so rather than reading as "nothing in this file parsed" (plan §8.3).
        var outcomes = preview.CompleteLineCount == 0
            ? "Outcomes unavailable · the preview sample holds no complete line"
            : $"{parsed:N0} parsed · {unknown:N0} unknown · {rejected:N0} rejected";
        _summary.Text = $"{detection}\n{parsing}\n{outcomes}\n{span}\n{year}";
        AutomationProperties.SetName(_summary, _summary.Text.Replace('\n', ' '));
        _warnings.Text = preview.Warnings.Count == 0
            ? "No preview warnings."
            : string.Join(Environment.NewLine, preview.Warnings.Select(static warning => $"• {warning}"));
        _validation.Text = string.Empty;
        SetInlineError(_year, _yearError, null);
        SetInlineError(_timeZone, _timeZoneError, null);
        _evaluatedSettings = settings;
        _import.IsEnabled = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ++_generation;
        _lifetime.Cancel();
        _evaluationCancellation?.Cancel();
        GC.SuppressFinalize(this);
    }

    /// <summary>Waits until cancelled option evaluations have stopped reading the sample.</summary>
    internal Task DrainAsync() => _releaseTask ??= ReleaseCoreAsync();

    private async Task ReleaseCoreAsync()
    {
        Dispose();
        try
        {
            Task[] pending;
            lock (_evaluationGate)
            {
                pending = [.. _evaluationTasks];
            }

            if (pending.Length > 0)
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Evaluations normally publish an inline error themselves. A late dispatcher
            // failure during shell teardown must not turn an intentional dialog dismissal
            // into an import failure or prevent release of the retained sample lifetime.
        }
        finally
        {
            _evaluationCancellation?.Dispose();
            _lifetime.Dispose();
        }
    }

    private void RefreshZoneChoices(string? query)
    {
        var text = query?.Trim() ?? string.Empty;
        var matches = text.Length == 0
            ? _zoneChoices
            : _zoneChoices
                .Where(zone =>
                    zone.Id.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
                    zone.Display.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .Take(100)
                .ToArray();
        _knownZones.ItemsSource = matches;
        _knownZones.SelectedItem = matches.FirstOrDefault(zone =>
            string.Equals(zone.Id, text, StringComparison.Ordinal));
    }

    private static ZoneChoice[] BuildZoneChoices()
    {
        var localId = TimeZoneInfo.Local.Id;
        var utcId = TimeZoneInfo.Utc.Id;
        return TimeZoneInfo.GetSystemTimeZones()
            .Append(TimeZoneInfo.Local)
            .Append(TimeZoneInfo.Utc)
            .GroupBy(static zone => zone.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static zone => new ZoneChoice(zone.Id, zone.DisplayName))
            .OrderBy(zone => string.Equals(zone.Id, localId, StringComparison.Ordinal) ? 0 :
                string.Equals(zone.Id, utcId, StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(static zone => zone.Display, StringComparer.CurrentCulture)
            .ThenBy(static zone => zone.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private void SetInlineError(Control field, TextBlock label, string? message)
    {
        label.Text = message ?? string.Empty;
        label.IsVisible = message is not null;
        AutomationProperties.SetName(label, message ?? string.Empty);
        AutomationProperties.SetHelpText(
            field,
            message ?? (ReferenceEquals(field, _year) ? _yearHelp : TimeZoneHelp));
    }

    private static TextBlock InlineError() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.IndianRed,
        IsVisible = false,
    };

    private static StackPanel Field(string label, Control control, string? help = null, TextBlock? error = null)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(control);
        if (help is not null)
        {
            stack.Children.Add(Help(help));
        }

        if (error is not null)
        {
            stack.Children.Add(error);
        }

        return stack;
    }

    private static TextBlock Help(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.72,
        FontSize = TextScale.Of(11.5),
    };

    private static string FriendlyFormat(LogcatFormat format) => format switch
    {
        LogcatFormat.ThreadTime => "Thread time",
        LogcatFormat.LongFormat => "Long",
        _ => format.ToString(),
    };

    private static (TimeZoneInfo Zone, string Label) ResolveSampleZone(string timeZoneId)
    {
        try
        {
            return (TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // ReadInput prevents an invalid edited ID from reaching Publish. This fallback is
            // for a valid preview created on another platform whose zone database cannot name
            // that ID; say explicitly that the displayed clock is UTC while retaining the
            // source evidence ID rather than silently relabelling it.
            return (TimeZoneInfo.Utc, $"UTC display · source zone {timeZoneId} unavailable here");
        }
    }

    private static string FormatSampleInstant(VisualCat.Domain.Time.InstantUs instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant.ToDateTimeOffset(), zone)
            .ToString("yyyy-MM-dd HH:mm:ss.ffffff zzz", CultureInfo.CurrentCulture);

    private sealed record FormatChoice(string Label, LogcatFormat? Format)
    {
        public override string ToString() => Label;
    }

    private sealed record ZoneChoice(string Id, string Display)
    {
        public override string ToString() => $"{Display} · {Id}";
    }

    private enum InputErrorTarget : byte
    {
        None,
        Year,
        TimeZone,
    }

    private sealed record Input(
        TimestampPolicy? Policy,
        LogcatFormat? Format,
        string? Error,
        InputErrorTarget ErrorTarget,
        bool MineTemplates = false,
        bool PortableRaw = false);
}
