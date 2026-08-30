using System.Net.NetworkInformation;

namespace LucidReader.Services;

/// <summary>
/// Reports whether the machine has a network, and raises an event when that
/// changes.
///
/// Built on System.Net.NetworkInformation, which is part of the base class
/// library and needs no package: NetworkChange.NetworkAvailabilityChanged is
/// implemented on macOS, Windows and Linux alike. No third-party dependency
/// and no platform branch is involved, which is the whole reason this is the
/// route taken rather than reachability APIs per platform.
///
/// What it can and cannot tell you, stated plainly because the difference
/// matters to what is built on top: GetIsNetworkAvailable answers "is there
/// an interface up that is not loopback or a tunnel". It does not answer "can
/// this machine reach the internet". A laptop on a captive-portal wifi, or
/// on a LAN with no route out, reads as available here and every fetch still
/// fails. That is the right trade for what this is used for - suppressing
/// pointless fetches while there is provably no network - and it is why the
/// gate it feeds only ever pauses refreshing rather than reporting feeds as
/// broken.
///
/// The event is raised on whatever thread the platform delivers the change
/// on, which is not the UI thread. Subscribers marshal for themselves.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly NetworkAvailabilityChangedEventHandler _onAvailabilityChanged;
    private readonly NetworkAddressChangedEventHandler _onAddressChanged;
    private int _disposed;

    public NetworkMonitor()
    {
        IsAvailable = SafeIsAvailable();

        // Both events, not just the first. NetworkAvailabilityChanged fires
        // on the transition between "some interface up" and "none", which a
        // machine switching from wifi to a cable may never make; the address
        // change is what catches an interface swap. Both funnel into the same
        // re-read, and the re-read is what decides, so a duplicate is free.
        _onAvailabilityChanged = (_, _) => Reread();
        _onAddressChanged = (_, _) => Reread();

        try
        {
            NetworkChange.NetworkAvailabilityChanged += _onAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += _onAddressChanged;
        }
        catch (NetworkInformationException)
        {
            // A platform that cannot deliver the notifications leaves this
            // reporting whatever the first read said, forever. That degrades
            // to the behaviour before this class existed rather than to a
            // crash, which is the point.
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    /// <summary>
    /// The last observed answer. Reading a field rather than calling into the
    /// platform on every access: the refresh path asks this often enough that
    /// an interface enumeration per question would be silly.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Raised with the new value, only when it actually changed.
    /// </summary>
    public event Action<bool>? AvailabilityChanged;

    private void Reread()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        var now = SafeIsAvailable();
        if (now == IsAvailable) return;

        IsAvailable = now;
        AvailabilityChanged?.Invoke(now);
    }

    /// <summary>
    /// Treats a failed interrogation as "available". Being wrong in that
    /// direction means the app tries to refresh and the fetch fails, which is
    /// exactly what happened before any of this existed; being wrong in the
    /// other direction would silently stop a working reader from ever
    /// refreshing again.
    /// </summary>
    private static bool SafeIsAvailable()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); }
        catch (NetworkInformationException) { return true; }
        catch (PlatformNotSupportedException) { return true; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            NetworkChange.NetworkAvailabilityChanged -= _onAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= _onAddressChanged;
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
    }
}
