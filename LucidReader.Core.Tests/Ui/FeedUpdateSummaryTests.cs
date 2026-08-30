using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The per-feed update line. All of it is a pure function of a handful of
/// timestamps and flags, which is the point: a Window cannot be constructed in
/// a unit test here, so the wording and the boundaries have to be assertable
/// without one.
/// </summary>
public class FeedUpdateSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static FeedUpdateLine Describe(
        bool isFeedSelected = true,
        bool isRefreshing = false,
        bool isAutoPaused = false,
        bool isEnabled = true,
        DateTimeOffset? lastFetchedUtc = null,
        DateTimeOffset? lastSuccessUtc = null,
        string? lastError = null,
        DateTimeOffset? nextDueUtc = null) =>
        FeedUpdateSummary.Describe(
            isFeedSelected, isRefreshing, isAutoPaused, isEnabled,
            lastFetchedUtc, lastSuccessUtc, lastError, nextDueUtc, Now);

    // ================= Relative time, the boundaries =================

    [Fact]
    public void Nothing_at_all_reads_as_never()
    {
        Assert.Equal("never", FeedUpdateSummary.DescribeElapsed(null, Now));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(1, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(119, "1 min ago")]
    [InlineData(120, "2 min ago")]
    public void Seconds_become_just_now_until_the_first_whole_minute(int seconds, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeElapsed(
            Now - TimeSpan.FromSeconds(seconds), Now));
    }

    [Theory]
    [InlineData(59, "59 min ago")]
    [InlineData(60, "1 hour ago")]
    [InlineData(90, "1 hour ago")]
    [InlineData(120, "2 hours ago")]
    public void Minutes_become_hours_on_the_hour(int minutes, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeElapsed(
            Now - TimeSpan.FromMinutes(minutes), Now));
    }

    [Theory]
    [InlineData(23, "23 hours ago")]
    [InlineData(24, "yesterday")]
    [InlineData(47, "yesterday")]
    [InlineData(48, "2 days ago")]
    [InlineData(72, "3 days ago")]
    [InlineData(24 * 40, "40 days ago")]
    public void Hours_become_yesterday_and_then_days(int hours, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeElapsed(
            Now - TimeSpan.FromHours(hours), Now));
    }

    [Fact]
    public void A_timestamp_in_the_future_reads_as_just_now_rather_than_a_negative_age()
    {
        Assert.Equal("just now", FeedUpdateSummary.DescribeElapsed(Now + TimeSpan.FromHours(3), Now));
    }

    // ================= Countdown, the boundaries =================

    [Theory]
    [InlineData(1, "under a minute")]
    [InlineData(59, "under a minute")]
    public void A_countdown_shorter_than_a_minute_does_not_claim_a_number(int seconds, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(1, "1 min")]
    [InlineData(26, "26 min")]
    [InlineData(59, "59 min")]
    public void A_countdown_in_minutes(int minutes, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeDuration(TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(60, "1 hour")]
    [InlineData(180, "3 hours")]
    [InlineData(60 * 24, "1 day")]
    [InlineData(60 * 24 * 3, "3 days")]
    public void A_countdown_in_hours_and_days(int minutes, string expected)
    {
        Assert.Equal(expected, FeedUpdateSummary.DescribeDuration(TimeSpan.FromMinutes(minutes)));
    }

    // ================= What the line says, state by state =================

    [Fact]
    public void A_selection_that_is_not_a_feed_hides_the_line_entirely()
    {
        var line = Describe(isFeedSelected: false, lastFetchedUtc: Now);

        Assert.False(line.IsVisible);
        Assert.Equal(string.Empty, line.Text);
        Assert.False(line.CanRefresh);
    }

    [Fact]
    public void A_feed_that_has_never_been_fetched_says_so()
    {
        var line = Describe(nextDueUtc: Now + TimeSpan.FromMinutes(5));

        Assert.True(line.IsVisible);
        Assert.Equal("Not updated yet   ·   Next in 5 min", line.Text);
        Assert.True(line.CanRefresh);
    }

    [Fact]
    public void An_ordinary_feed_reads_last_updated_then_next_due()
    {
        var line = Describe(
            lastFetchedUtc: Now - TimeSpan.FromMinutes(4),
            lastSuccessUtc: Now - TimeSpan.FromMinutes(4),
            nextDueUtc: Now + TimeSpan.FromMinutes(26));

        Assert.Equal("Updated 4 min ago   ·   Next in 26 min", line.Text);
        Assert.True(line.CanRefresh);
    }

    [Fact]
    public void A_feed_with_no_next_due_time_says_only_when_it_last_updated()
    {
        var line = Describe(
            lastFetchedUtc: Now - TimeSpan.FromHours(30),
            lastSuccessUtc: Now - TimeSpan.FromHours(30));

        Assert.Equal("Updated yesterday", line.Text);
    }

    [Fact]
    public void A_due_time_that_has_already_passed_says_due_now_rather_than_a_negative_countdown()
    {
        var line = Describe(
            lastFetchedUtc: Now - TimeSpan.FromMinutes(31),
            lastSuccessUtc: Now - TimeSpan.FromMinutes(31),
            nextDueUtc: Now - TimeSpan.FromMinutes(1));

        Assert.Equal("Updated 31 min ago   ·   Due now", line.Text);
    }

    [Fact]
    public void A_refresh_in_flight_says_so_and_withdraws_the_refresh_control()
    {
        var line = Describe(
            isRefreshing: true,
            lastFetchedUtc: Now - TimeSpan.FromHours(2),
            lastSuccessUtc: Now - TimeSpan.FromHours(2));

        Assert.True(line.IsVisible);
        Assert.Equal("Refreshing now...", line.Text);
        Assert.False(line.CanRefresh);
    }

    [Fact]
    public void A_failed_last_attempt_says_so_rather_than_reporting_the_older_success()
    {
        var line = Describe(
            lastFetchedUtc: Now - TimeSpan.FromMinutes(2),
            lastSuccessUtc: Now - TimeSpan.FromHours(6),
            lastError: "Name or service not known",
            nextDueUtc: Now + TimeSpan.FromMinutes(8));

        Assert.Equal("Last update failed   ·   Next in 8 min", line.Text);

        // Still offered: a manual retry is exactly what a user looking at this
        // line wants next.
        Assert.True(line.CanRefresh);
    }

    [Fact]
    public void A_stale_error_from_before_the_last_success_is_not_reported_as_a_failure()
    {
        var line = Describe(
            lastFetchedUtc: Now - TimeSpan.FromMinutes(3),
            lastSuccessUtc: Now - TimeSpan.FromMinutes(3),
            lastError: "Name or service not known",
            nextDueUtc: Now + TimeSpan.FromMinutes(27));

        Assert.Equal("Updated 3 min ago   ·   Next in 27 min", line.Text);
    }

    [Fact]
    public void An_auto_paused_feed_says_it_is_paused_and_leaves_resuming_to_the_resume_button()
    {
        var line = Describe(
            isAutoPaused: true,
            lastFetchedUtc: Now - TimeSpan.FromHours(9),
            lastError: "Name or service not known");

        Assert.True(line.IsVisible);
        Assert.Equal("Paused after repeated failures.", line.Text);
        Assert.False(line.CanRefresh);
    }

    [Fact]
    public void A_feed_the_user_turned_off_reads_differently_from_one_that_was_paused_for_failing()
    {
        var line = Describe(
            isEnabled: false,
            lastFetchedUtc: Now - TimeSpan.FromHours(9),
            lastSuccessUtc: Now - TimeSpan.FromHours(9));

        Assert.Equal("Updates are turned off for this feed.", line.Text);
        Assert.False(line.CanRefresh);
    }

    [Fact]
    public void A_refresh_that_has_started_on_a_paused_feed_beats_the_paused_wording()
    {
        var line = Describe(isRefreshing: true, isAutoPaused: true);

        Assert.Equal("Refreshing now...", line.Text);
    }

    // The short form is what actually renders; the long form is the tooltip.
    // These pin the abbreviations because the whole reason they exist is to fit
    // beside the filter control on one line, and a wording change that made one
    // longer would quietly bring the wrap back.

    [Fact]
    public void A_feed_that_has_never_been_fetched_reads_Never_in_the_short_form()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var line = FeedUpdateSummary.Describe(true, false, false, true, null, null, null, null, now);

        Assert.Equal("Never", line.ShortText);
        Assert.Equal("Not updated yet", line.Text);
    }

    [Fact]
    public void A_failed_attempt_reads_Failed_in_the_short_form()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var line = FeedUpdateSummary.Describe(
            true, false, false, true, now.AddMinutes(-5), null, "boom", null, now);

        Assert.Equal("Failed", line.ShortText);
        Assert.Contains("Last update failed", line.Text);
    }

    [Fact]
    public void A_successful_fetch_shows_only_the_elapsed_time_in_the_short_form()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var line = FeedUpdateSummary.Describe(
            true, false, false, true, now.AddMinutes(-4), now.AddMinutes(-4), null, now.AddMinutes(26), now);

        Assert.Equal("4 min ago", line.ShortText);
        Assert.Contains("Updated 4 min ago", line.Text);
        Assert.Contains("Next", line.Text);
    }

    [Theory]
    [InlineData(true, false, true, "Refreshing...")]
    [InlineData(false, true, true, "Paused")]
    [InlineData(false, false, false, "Updates off")]
    public void The_short_form_stays_short_in_every_state(
        bool refreshing, bool paused, bool enabled, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var line = FeedUpdateSummary.Describe(
            true, refreshing, paused, enabled, now.AddMinutes(-5), now.AddMinutes(-5), null, null, now);

        Assert.Equal(expected, line.ShortText);
        Assert.True(line.ShortText.Length <= line.Text.Length);
    }
}
