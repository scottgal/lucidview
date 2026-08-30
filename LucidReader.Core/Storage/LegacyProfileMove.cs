namespace LucidReader.Core.Storage;

/// <summary>
/// What should happen to a profile directory left behind by the old product
/// name when the app starts.
/// </summary>
public enum LegacyProfileAction
{
    /// <summary>No old directory to worry about. Start normally.</summary>
    None,

    /// <summary>Only the old directory exists. Rename it to the new name.</summary>
    Move,

    /// <summary>
    /// Both exist. The new one wins and the old one is left exactly as it is,
    /// because merging two databases is not something a startup path should be
    /// guessing at, and deleting one is not something it should ever do.
    /// </summary>
    KeepBoth
}

/// <summary>
/// Moves the profile directory across when the product was renamed from
/// lucidREADER to mylo. Split into a decision and an application step so the
/// decision can be tested without a filesystem.
///
/// This is deliberately a directory rename and nothing more. It never copies,
/// never merges and never deletes: if anything about the situation is not the
/// simple "old profile, no new profile" case, it leaves both directories alone
/// and the app carries on against the new path.
/// </summary>
public static class LegacyProfileMove
{
    /// <summary>
    /// The whole rule, in one place, with no filesystem behind it.
    /// </summary>
    public static LegacyProfileAction Decide(bool legacyExists, bool currentExists)
    {
        if (!legacyExists) return LegacyProfileAction.None;
        return currentExists ? LegacyProfileAction.KeepBoth : LegacyProfileAction.Move;
    }

    /// <summary>
    /// Applies <see cref="Decide"/> to two real directories and reports what it
    /// did. A failed move is reported rather than thrown: an unreadable or
    /// locked old directory is not a reason to stop the app from starting
    /// against the new one, which is empty but usable.
    /// </summary>
    public static LegacyProfileMoveResult Apply(string legacyDirectory, string currentDirectory)
    {
        var action = Decide(
            Directory.Exists(legacyDirectory),
            Directory.Exists(currentDirectory));

        if (action != LegacyProfileAction.Move)
            return new LegacyProfileMoveResult(action, Moved: false, Error: null);

        try
        {
            var parent = Path.GetDirectoryName(currentDirectory);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            Directory.Move(legacyDirectory, currentDirectory);
            return new LegacyProfileMoveResult(action, Moved: true, Error: null);
        }
        catch (Exception ex)
        {
            return new LegacyProfileMoveResult(action, Moved: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// One line, suitable for a log, describing what <see cref="Apply"/> did.
    /// </summary>
    public static string Describe(
        LegacyProfileMoveResult result,
        string legacyDirectory,
        string currentDirectory) => result.Action switch
    {
        LegacyProfileAction.None =>
            $"No {ReaderPaths.LegacyAppFolderName} profile to move. Using {currentDirectory}.",
        LegacyProfileAction.KeepBoth =>
            $"Both {legacyDirectory} and {currentDirectory} exist. Using {currentDirectory} and leaving the older directory alone.",
        _ when result.Moved =>
            $"Moved {legacyDirectory} to {currentDirectory} after the rename to {ReaderPaths.AppFolderName}.",
        _ =>
            $"Could not move {legacyDirectory} to {currentDirectory}: {result.Error}. Starting against {currentDirectory} and leaving the older directory alone."
    };
}

/// <param name="Action">What the rule said to do.</param>
/// <param name="Moved">True only when a directory was actually renamed.</param>
/// <param name="Error">Set when a move was attempted and failed.</param>
public readonly record struct LegacyProfileMoveResult(
    LegacyProfileAction Action,
    bool Moved,
    string? Error);
