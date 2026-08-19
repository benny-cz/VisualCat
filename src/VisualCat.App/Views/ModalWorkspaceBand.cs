using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace VisualCat.App.Views;

/// <summary>
/// The workspace band, with a switch that takes it out of the accessibility tree while a
/// modal sheet is over it.
/// </summary>
/// <remarks>
/// The scrim under a sheet catches every pointer, so touch was already safe. Assistive
/// technology was not: with the <em>More actions</em> sheet open, an accessibility dump held
/// twenty-nine clickable nodes — the sheet's nine plus every one of the workspace's own,
/// still enabled, still clickable. A screen-reader user swiping past the sheet's last item
/// walked into Open log, Live, the mode buttons and the entry rows, and could start a live
/// capture or close a session from behind a modal (audit 3, B3).
///
/// The previous attempt said the right thing with the wrong instrument:
/// <c>AutomationProperties.AccessibilityView = Raw</c> and <c>IsOffscreenBehavior.Offscreen</c>
/// describe <em>one</em> peer and are not inherited, so the band left the control view and its
/// twenty descendants were promoted in its place. Nothing about that is Android-specific and
/// no attached property fixes it: the tree has to stop at the band.
///
/// So the band owns its own peer and reports no children while it is sealed. That is one
/// statement, made in the layer every platform bridge reads through — Android's node provider,
/// UIA's <c>Navigate</c> and AppKit's <c>accessibilityChildren</c> all arrive at
/// <see cref="AutomationPeer.GetChildren"/> — and it needs nothing from the backend beyond
/// asking the question. <see cref="AutomationPeer.IsOffscreen"/> is answered as well, because a
/// bridge that reads it before the children gets the same answer either way, and because
/// "behind a modal" is exactly what it means.
///
/// Nothing here has any visual effect. Disabling the band would have one, and a workspace
/// greyed out behind a bottom sheet would be a worse lie than the one being fixed.
/// </remarks>
internal sealed class ModalWorkspaceBand : DockPanel
{
    private BandPeer? _peer;
    private bool _sealed;

    /// <summary>Whether a modal surface is currently covering this band.</summary>
    internal bool IsSealedForModal
    {
        get => _sealed;
        set
        {
            if (_sealed == value)
            {
                return;
            }

            _sealed = value;

            // The peer's child list is cached, so a client that has already walked the tree
            // keeps the old answer until it is told to ask again.
            _peer?.NotifyChildrenChanged();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => _peer ??= new BandPeer(this);

    private sealed class BandPeer(ModalWorkspaceBand owner) : ControlAutomationPeer(owner)
    {
        private readonly ModalWorkspaceBand _owner = owner;

        internal void NotifyChildrenChanged() => InvalidateChildren();

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() =>
            _owner.IsSealedForModal ? [] : base.GetOrCreateChildrenCore();

        protected override bool IsOffscreenCore() =>
            _owner.IsSealedForModal || base.IsOffscreenCore();

        protected override bool IsContentElementCore() =>
            !_owner.IsSealedForModal && base.IsContentElementCore();

        protected override bool IsControlElementCore() =>
            !_owner.IsSealedForModal && base.IsControlElementCore();
    }
}
