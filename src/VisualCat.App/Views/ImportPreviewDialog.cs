using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VisualCat.Application.UseCases;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;

namespace VisualCat.App.Views;

public sealed class ImportPreviewDialog : Window
{
    private readonly ImportPreview _preview;
    private readonly ComboBox _format = new();
    private readonly TextBox _year = new();
    private readonly TextBox _timeZone = new();
    private readonly CheckBox _templates = new() { Content = "Mine deterministic message templates", IsChecked = true };
    private readonly CheckBox _portableRaw = new() { Content = "Embed raw source immediately" };
    private readonly TextBlock _validation = new();

    public ImportPreviewDialog(string displayName, ImportPreview preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _preview = preview;
        Title = $"Import preview — {displayName}";
        Width = 720;
        Height = 620;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
    }

    public IngestSettings? SelectedSettings { get; private set; }

    private Grid Build()
    {
        var formats = new[]
        {
            LogcatFormat.ThreadTime,
            LogcatFormat.Time,
            LogcatFormat.Brief,
            LogcatFormat.LongFormat,
            LogcatFormat.Epoch,
        };
        _format.ItemsSource = formats;
        _format.SelectedItem = formats.Contains(_preview.Detection.PrimaryFormat)
            ? _preview.Detection.PrimaryFormat
            : LogcatFormat.ThreadTime;
        _year.Text = _preview.TimestampPolicy.AssumedYear?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        _timeZone.Text = _preview.TimestampPolicy.TimeZoneId;
        _validation.Foreground = Avalonia.Media.Brushes.OrangeRed;
        _validation.TextWrapping = Avalonia.Media.TextWrapping.Wrap;

        var counts = string.Join(
            Environment.NewLine,
            _preview.OutcomeCounts
                .Where(static pair => pair.Value > 0)
                .OrderBy(static pair => pair.Key)
                .Select(static pair => $"{pair.Key}: {pair.Value:N0}"));
        var candidates = string.Join(
            Environment.NewLine,
            _preview.Detection.Candidates
                .OrderByDescending(static candidate => candidate.Score)
                .Select(static candidate =>
                    $"{candidate.Format,-12} score {candidate.Score:P1} · {candidate.Matched:N0} matched"));
        var warnings = _preview.Warnings.Count == 0
            ? "No preview warnings."
            : string.Join(Environment.NewLine, _preview.Warnings.Select(static warning => $"• {warning}"));

        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        AddRow(form, 0, "Detected candidates", new TextBlock { Text = candidates, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        AddRow(form, 1, "Import format", _format);
        AddRow(form, 2, "Assumed year (optional)", _year);
        AddRow(form, 3, "Time zone", _timeZone);
        AddRow(
            form,
            4,
            "Resolved preview span",
            new TextBlock
            {
                Text = $"{_preview.FirstInstant?.ToString() ?? "none"} — {_preview.LastInstant?.ToString() ?? "none"}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => Close(false);
        var import = new Button { Content = "Import", MinWidth = 100 };
        import.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(import);

        return new Grid
        {
            Margin = new Thickness(18),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                At(new TextBlock
                {
                    Text = "Review format and timestamp assumptions before indexing. These choices are persisted in the session manifest.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                }, 0),
                At(form, 1),
                At(new TextBlock
                {
                    Text = $"Preview outcomes:{Environment.NewLine}{counts}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                }, 2),
                At(new TextBlock { Text = warnings, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, 3),
                At(new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Spacing = 6,
                    Children = { _templates, _portableRaw, _validation },
                }, 4),
                At(actions, 5),
            },
        };
    }

    private void Accept()
    {
        try
        {
            var yearText = _year.Text?.Trim();
            int? year = null;
            if (!string.IsNullOrEmpty(yearText))
            {
                if (!int.TryParse(yearText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
                    parsed is < 1970 or > 9999)
                {
                    throw new InvalidDataException("Assumed year must be blank or between 1970 and 9999.");
                }

                year = parsed;
            }

            var timeZoneId = _timeZone.Text?.Trim();
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                throw new InvalidDataException("A time-zone identifier is required.");
            }

            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (_format.SelectedItem is not LogcatFormat format)
            {
                throw new InvalidDataException("Select a supported logcat format.");
            }

            SelectedSettings = new IngestSettings(
                format,
                "utf-8",
                _preview.TimestampPolicy with { AssumedYear = year, TimeZoneId = timeZoneId },
                new TemplateSettings(Enabled: _templates.IsChecked == true),
                PortableRaw: _portableRaw.IsChecked == true);
            Close(true);
        }
        catch (Exception exception) when (exception is InvalidDataException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // InvalidDataException here is thrown by this dialog and already reads as a
            // sentence; the two time-zone exceptions are the framework's, and a trimmed
            // Release build gives those as resource keys (finding F-04). FriendlyMessage
            // passes the first through and replaces the second.
            _validation.Text = Presentation.WorkspaceViewModel.FriendlyMessage(exception);
        }
    }

    private static void AddRow(Grid grid, int row, string label, Control value)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    private static T At<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
