using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

public sealed class MainViewCommandAvailabilityTests
{
    [AvaloniaFact]
    public async Task SessionActionsAreDisabledWhenNoSessionIsOpen()
    {
        await using var view = new MainView();
        var window = new Window
        {
            Content = view,
            Width = 1400,
            Height = 800,
        };
        window.Show();
        try
        {
            var buttons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Where(static button => button.Content is string)
                .ToDictionary(static button => (string)button.Content!, StringComparer.Ordinal);

            Assert.False(buttons["Save"].IsEnabled);
            Assert.False(buttons["Save portable"].IsEnabled);
            Assert.False(buttons["Export"].IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
