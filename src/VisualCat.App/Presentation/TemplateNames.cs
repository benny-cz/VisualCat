using VisualCat.Core.Store;

namespace VisualCat.App.Presentation;

/// <summary>
/// The one wording for an active template filter, wherever the reader meets it.
/// </summary>
/// <remarks>
/// <para>
/// A template filter is stored as a mined numeric ID. Shown raw it reads
/// <c>template = 3821941</c>: a filter nobody can read, which is the defect the facet package
/// exists to correct. A filter holds a handful of IDs, so resolving them against the
/// snapshot's existing template table is a bounded lookup rather than another tally.
/// </para>
/// <para>
/// Both the workspace and the export review ask this, for the same reason the dimension
/// words are asked in one place: two surfaces naming the same filter differently is how a
/// reader ends up unable to tell they are looking at the same thing. An ID the snapshot has
/// no definition for keeps the wording <c>QueryTopTemplates</c> already uses, so a template
/// that has fallen out of the table degrades to something identifying rather than vanishing.
/// </para>
/// </remarks>
internal static class TemplateNames
{
    /// <summary>The canonical text of one mined template, or the wording it has none.</summary>
    internal static string Of(SessionSnapshot? snapshot, uint id)
    {
        // A session being torn down cannot name its templates. The ID still identifies the
        // filter, which is what removing it needs, so the fallback wording is used rather
        // than letting a caller fail while it renders.
        try
        {
            return snapshot?.Templates.FirstOrDefault(template => template.TemplateId == id)?.CanonicalText
                   ?? Fallback(id);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return Fallback(id);
        }
    }

    /// <summary>What an ID with no definition in the snapshot is called.</summary>
    internal static string Fallback(uint id) => $"Template {id}";
}
