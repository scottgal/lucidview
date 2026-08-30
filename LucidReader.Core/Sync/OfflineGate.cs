namespace LucidReader.Core.Sync;

/// <summary>
/// What the app should do about refreshing when the network comes and goes.
///
/// ReaderSettings.PauseWhenOffline has existed since the first version of the
/// settings dialog, defaults to on, is written to settings.json, is read back
/// out of it, and until now was read by nothing else at all: no code anywhere
/// observed network availability, so the switch was a label. A setting that
/// does nothing is worse than a missing one, because the user believes it.
///
/// The rule itself is small enough to state here and be tested without a
/// network stack. What it buys with the setting on: while the machine has no
/// network, refreshing is paused rather than run and failed. That is not
/// cosmetic. Every attempt made while offline is recorded as a failure,
/// advances the backoff curve, and counts toward the twenty consecutive
/// failures that auto-pause a feed. A laptop closed on a train for a long
/// enough journey could come back with feeds paused not because anything is
/// wrong with them, but because the reader kept trying while there was no
/// route to anywhere and counted every one of those against them.
/// </summary>
public static class OfflineGate
{
    public static bool ShouldPauseRefreshing(bool pauseWhenOffline, bool networkAvailable) =>
        pauseWhenOffline && !networkAvailable;

    /// <summary>
    /// The status line for a change of state, or an empty string when there
    /// is nothing worth saying. Empty is the normal answer: the status bar
    /// carries the result of whatever the user last did, and stamping over it
    /// to announce that the network is, as usual, present would make it
    /// useless.
    /// </summary>
    public static string DescribeTransition(bool pauseWhenOffline, bool networkAvailable)
    {
        if (!pauseWhenOffline) return string.Empty;

        return networkAvailable
            ? "Back online. Refreshing has resumed."
            : "No network. Refreshing is paused until the connection comes back.";
    }
}
