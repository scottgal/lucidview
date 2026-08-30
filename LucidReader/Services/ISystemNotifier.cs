namespace LucidReader.Services;

/// <summary>
/// Somewhere to post a short "n new articles" message.
///
/// An interface rather than one class with platform branches inside it,
/// because the interesting property is which route is live on the machine in
/// front of you and whether it is the real one. <see cref="Route"/> exists to
/// be shown to a human: a notification feature that silently degrades to
/// nothing is indistinguishable from one that is broken.
/// </summary>
public interface ISystemNotifier
{
    /// <summary>
    /// False when this route cannot post on this machine. Callers must check
    /// it rather than posting and hoping: a route that reports false is
    /// expected to be unusable, not merely unlucky.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A short name for where notifications actually go, for the status bar
    /// and for anyone reading a bug report.
    /// </summary>
    string Route { get; }

    /// <summary>
    /// Posts one notification. Never throws: a failed notification is not
    /// worth an exception reaching a caller that is, by construction, in the
    /// middle of something else.
    /// </summary>
    void Post(string title, string body);
}
