using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VisualCat.App.Presentation;

namespace VisualCat.App.Views;

/// <summary>
/// The workspace status line, and the only route that can write it.
/// </summary>
/// <remarks>
/// The invariant this type exists to hold used to be a comment on <c>ApplyStatusText</c>:
/// every route that changes the status comes through one place, so that the visible text and
/// the accessible name can never disagree. Five routes did not — they assigned the
/// <see cref="TextBlock"/>'s <c>Text</c> — and both halves of the defect followed. A screen
/// reader went on being told <c>Ready · 49,994 entries</c> while the line read
/// <c>Failed · MakeException, …</c>, and because the view model's own status had not moved,
/// nothing ever rewrote the line: the failure outlived a successful query, a cleared filter
/// and a workspace mode switch (finding F-05).
///
/// So the <see cref="TextBlock"/> is private and the only mutator takes no text at all: it
/// reads <see cref="SessionTabViewModel.Status"/>. Writing the status line now means changing
/// the view model — through <see cref="SessionTabViewModel.ReportActivity"/> or
/// <see cref="SessionTabViewModel.ReportTransientStatus"/> — which is the one thing every
/// surface already follows. The layout members the workspace genuinely needs are re-exposed
/// individually; <c>Text</c> is readable and not writable.
/// </remarks>
internal sealed class StatusLine
{
    private readonly TextBlock _block = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly SessionTabViewModel _viewModel;

    public StatusLine(SessionTabViewModel viewModel)
    {
        _viewModel = viewModel;

        // A report on work in progress, so a reader who is not looking at it is told when it
        // changes — politely, because a failure raises the notice lane, which is assertive.
        AutomationProperties.SetLiveSetting(_block, AutomationLiveSetting.Polite);
    }

    /// <summary>The control to place in the layout.</summary>
    public Control Control => _block;

    /// <summary>What the line currently says. Readable; not writable.</summary>
    public string Text => _block.Text ?? string.Empty;

    /// <summary>The arranged width, which is what decides whether the line was clipped.</summary>
    public double ArrangedWidth => _block.Bounds.Width;

    /// <summary>The arranged text, for the overflow question the layout pass answers.</summary>
    public TextLayout? Layout => _block.TextLayout;

    public event EventHandler? LayoutUpdated
    {
        add => _block.LayoutUpdated += value;
        remove => _block.LayoutUpdated -= value;
    }

    /// <summary>Switches between one clipped line and the whole sentence.</summary>
    public void SetExpanded(bool expanded)
    {
        _block.TextWrapping = expanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _block.TextTrimming = expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
    }

    /// <summary>
    /// Republishes the view model's status everywhere the status line is read.
    /// </summary>
    public void Refresh()
    {
        var status = _viewModel.Status ?? string.Empty;
        _block.Text = status;

        // The visible line may be clipped; what a screen reader is handed never is.
        AutomationProperties.SetName(_block, status);
        AutomationProperties.SetHelpText(_block, status);
        ToolTip.SetTip(_block, status.Length > 0 ? status : null);
    }
}
