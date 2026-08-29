using LucidReader.Views;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// MainWindow.DescribeHealth is a static pure function precisely so the
/// wording can be tested without constructing an Avalonia Window.
/// </summary>
public class HealthTests
{
    [Fact]
    public void A_healthy_scheduler_reports_nothing()
    {
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, true, null, 0, 0));
    }

    [Fact]
    public void A_stopped_scheduler_is_reported()
    {
        var text = MainWindow.DescribeHealth(true, false, null, 0, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_scheduler_that_is_running_but_failing_every_tick_is_reported_as_failing()
    {
        var text = MainWindow.DescribeHealth(true, true, "database is locked", 5, 0);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database is locked", text);
    }

    [Fact]
    public void One_isolated_tick_failure_is_not_shouted_about()
    {
        // A single blip is not worth alarming the user; a sustained streak is.
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, true, "transient", 1, 0));
    }

    [Fact]
    public void Auto_paused_feeds_are_reported_with_a_count()
    {
        var text = MainWindow.DescribeHealth(true, true, null, 0, 3);

        Assert.Contains("3", text);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_auto_paused_feed_reads_in_the_singular()
    {
        var text = MainWindow.DescribeHealth(true, true, null, 0, 1);

        Assert.Contains("1 feed", text);
        Assert.DoesNotContain("1 feeds", text);
    }

    [Fact]
    public void Both_problems_at_once_are_both_reported()
    {
        var text = MainWindow.DescribeHealth(true, true, "boom", 4, 2);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_stopped_scheduler_does_not_also_claim_the_ticks_are_failing()
    {
        // "Not running" already says nothing is being refreshed. Adding a
        // failing-tick line on top would describe a loop that is not turning.
        var text = MainWindow.DescribeHealth(true, false, "boom", 9, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failing", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_pause_count_is_reported_even_while_the_scheduler_is_stopped()
    {
        var text = MainWindow.DescribeHealth(true, false, null, 0, 2);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 feeds", text);
    }

    [Fact]
    public void A_scheduler_stopped_because_the_user_turned_refresh_on_startup_off_is_not_a_fault()
    {
        // ReaderServices only starts the scheduler when RefreshOnStartup is
        // set, so unchecking that box in settings makes IsRunning false for
        // the whole session. Reporting it every 30 seconds told the user the
        // app was broken and stamped over every message their own actions
        // produced ("12 articles", "Feed settings saved.", "Exported to ...").
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(false, false, null, 0, 0));
    }

    [Fact]
    public void Turning_refresh_on_startup_off_does_not_hide_auto_paused_feeds()
    {
        // The setting says nothing about paused feeds, which are still a
        // problem the user has to act on.
        var text = MainWindow.DescribeHealth(false, false, null, 0, 2);

        Assert.DoesNotContain("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 feeds", text);
    }

    [Fact]
    public void A_scheduler_that_should_be_running_and_is_not_is_still_reported()
    {
        var text = MainWindow.DescribeHealth(true, false, null, 0, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
    }
}
