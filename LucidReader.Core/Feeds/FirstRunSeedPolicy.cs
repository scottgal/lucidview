namespace LucidReader.Core.Feeds;

/// <summary>
/// Decides whether a profile should be given the starter subscriptions in
/// <see cref="DefaultFeeds"/>.
///
/// The whole question is "is this the first run", and getting that wrong in
/// one particular direction is the failure worth guarding against: re-seeding
/// a profile whose owner has unsubscribed from everything would mean an empty
/// reader is a state the app refuses to stay in. Somebody who clears their
/// subscriptions and restarts must find their reader still empty.
///
/// So an empty feed table is never on its own a reason to seed. Two other
/// facts have to agree: nothing was seeded before (a flag persisted in
/// settings.json), and there was no settings file on disk when the app
/// started. The second is what makes a profile "new" rather than merely
/// "empty" - a profile that has ever been used has written settings.json,
/// whether or not it ever had a feed in it.
///
/// A plain static function taking three facts rather than a method that reads
/// the database and the filesystem itself: the decision is the part worth
/// testing, and it should be testable without a profile, a database or a disk.
/// </summary>
public static class FirstRunSeedPolicy
{
    /// <param name="settingsFileExisted">
    /// Whether settings.json was on disk when the app started. False only on
    /// a genuinely new profile directory.
    /// </param>
    /// <param name="alreadySeeded">
    /// The persisted flag. True once the starter feeds have been written,
    /// forever after, regardless of what happens to them next.
    /// </param>
    /// <param name="existingFeedCount">How many feeds the profile already has.</param>
    public static bool ShouldSeed(bool settingsFileExisted, bool alreadySeeded, int existingFeedCount)
    {
        if (settingsFileExisted) return false;
        if (alreadySeeded) return false;
        return existingFeedCount == 0;
    }

    /// <summary>
    /// The status line a seeded first run opens with. Named here rather than
    /// composed at the call site so the wording can be asserted without a
    /// window.
    /// </summary>
    public static string DescribeSeed(int count) => count == 1
        ? "Added one feed to get you started. Unsubscribe from it any time."
        : $"Added {count} feeds to get you started. Unsubscribe from any of them at any time.";
}
