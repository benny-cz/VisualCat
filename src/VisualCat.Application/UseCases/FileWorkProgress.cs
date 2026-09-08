namespace VisualCat.Application.UseCases;

/// <summary>Machine-readable stages reported by finite file application services.</summary>
public enum FileWorkStage : byte
{
    Preparing,
    Copying,
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
