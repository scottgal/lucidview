using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The starter subscriptions exist so a brand new profile is not three empty
/// panes. The failure this file is mostly about is the opposite one: a reader
/// that hands the list back every time it is opened empty, so that having no
/// feeds becomes a state the app will not let its owner stay in.
/// </summary>
public class FirstRunSeedPolicyTests
{
    [Fact]
    public void A_new_profile_is_seeded()
    {
        Assert.True(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: false, alreadySeeded: false, existingFeedCount: 0));
    }

    [Fact]
    public void A_profile_that_already_has_feeds_is_not_seeded()
    {
        Assert.False(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: false, alreadySeeded: false, existingFeedCount: 3));
    }

    [Fact]
    public void A_profile_that_was_seeded_before_and_is_now_empty_is_not_seeded_again()
    {
        Assert.False(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: true, alreadySeeded: true, existingFeedCount: 0));
    }

    /// <summary>
    /// The flag alone is not enough. A profile written before the flag
    /// existed has no such value in its settings.json, so it deserializes as
    /// false; if an empty feed table plus a false flag were sufficient, every
    /// long-standing profile whose owner had unsubscribed from everything
    /// would be re-seeded by the first build that shipped this feature.
    /// </summary>
    [Fact]
    public void An_existing_profile_with_no_feeds_and_no_flag_is_not_seeded()
    {
        Assert.False(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: true, alreadySeeded: false, existingFeedCount: 0));
    }

    [Fact]
    public void A_settings_file_on_disk_always_wins_over_everything_else()
    {
        Assert.False(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: true, alreadySeeded: false, existingFeedCount: 0));
        Assert.False(FirstRunSeedPolicy.ShouldSeed(
            settingsFileExisted: true, alreadySeeded: true, existingFeedCount: 5));
    }

    [Fact]
    public void The_seed_message_is_singular_for_one_feed()
    {
        Assert.Contains("one feed", FirstRunSeedPolicy.DescribeSeed(1));
        Assert.Contains("5 feeds", FirstRunSeedPolicy.DescribeSeed(5));
    }
}
