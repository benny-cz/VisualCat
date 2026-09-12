namespace VisualCat.Application.UseCases;

/// <summary>Machine-readable stages reported by finite file application services.</summary>
public enum FileWorkStage : byte
{
    Preparing,
    Copying,

    /// <summary>
    /// Nothing is moving: a modal review or a chooser owns the interaction and the product is
    /// waiting for a person.
    /// </summary>
    /// <remarks>
    /// The card said "Copying file…" with an animating bar for the entire life of the import
    /// review — more than four minutes on a 90 KiB file, which was simply how long the review
    /// was left open — while the process held no descriptor on the source and no temporary copy
    /// existed anywhere (finding F-04). An animating bar is the product asserting that bytes are
    /// moving, so a stage that means "waiting for you" must both say so and stop animating.
    /// </remarks>
    AwaitingReview,
    WritingRows,
    Verifying,
    CreatingArchive,
    ExtractingArchive,
    SavingToProvider,
    Publishing,
}

/// <summary>
/// Reports facts about one service stage. A missing total deliberately means indeterminate;
/// callers must not manufacture an overall percentage across unlike stages.
/// </summary>
public readonly record struct FileWorkProgress(
    FileWorkStage Stage,
    long Completed = 0,
    long? Total = null,
    string Unit = "items");
