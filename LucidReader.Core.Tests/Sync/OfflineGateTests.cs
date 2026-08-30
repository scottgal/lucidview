using LucidReader.Core.Sync;
using Xunit;

namespace LucidReader.Core.Tests.Sync;

/// <summary>
/// PauseWhenOffline, which until this branch was a checkbox nothing read.
/// </summary>
public class OfflineGateTests
{
    [Fact]
    public void With_the_setting_on_a_missing_network_pauses_refreshing()
    {
        Assert.True(OfflineGate.ShouldPauseRefreshing(pauseWhenOffline: true, networkAvailable: false));
    }

    [Fact]
    public void A_present_network_never_pauses_refreshing()
    {
        Assert.False(OfflineGate.ShouldPauseRefreshing(pauseWhenOffline: true, networkAvailable: true));
        Assert.False(OfflineGate.ShouldPauseRefreshing(pauseWhenOffline: false, networkAvailable: true));
    }

    [Fact]
    public void With_the_setting_off_the_network_is_not_consulted()
    {
        // The user asked for attempts to be made regardless, which is a
        // legitimate thing to want on a machine whose interface reporting is
        // unreliable. See NetworkMonitor for what "available" can and cannot
        // actually tell you.
        Assert.False(OfflineGate.ShouldPauseRefreshing(pauseWhenOffline: false, networkAvailable: false));
    }

    [Fact]
    public void Going_offline_and_coming_back_both_say_so()
    {
        Assert.Contains("paused", OfflineGate.DescribeTransition(true, networkAvailable: false));
        Assert.Contains("resumed", OfflineGate.DescribeTransition(true, networkAvailable: true));
    }

    [Fact]
    public void With_the_setting_off_there_is_nothing_to_announce()
    {
        // The status bar carries the result of whatever the user last did.
        // Announcing an unrelated network change over it, when the setting
        // means nothing changed, would make it useless.
        Assert.Equal(string.Empty, OfflineGate.DescribeTransition(false, networkAvailable: false));
        Assert.Equal(string.Empty, OfflineGate.DescribeTransition(false, networkAvailable: true));
    }
}
