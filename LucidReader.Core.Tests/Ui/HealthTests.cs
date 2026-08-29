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
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, null, 0, 0));
    }

    [Fact]
    public void A_stopped_scheduler_is_reported()
    {
        var text = MainWindow.DescribeHealth(false, null, 0, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_scheduler_that_is_running_but_failing_every_tick_is_reported_as_failing()
    {
        var text = MainWindow.DescribeHealth(true, "database is locked", 5, 0);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database is locked", text);
    }

    [Fact]
    public void One_isolated_tick_failure_is_not_shouted_about()
    {
        // A single blip is not worth alarming the user; a sustained streak is.
        Assert.Equal(string.Empty, MainWindow.DescribeHealth(true, "transient", 1, 0));
    }

    [Fact]
    public void Auto_paused_feeds_are_reported_with_a_count()
    {
        var text = MainWindow.DescribeHealth(true, null, 0, 3);

        Assert.Contains("3", text);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_auto_paused_feed_reads_in_the_singular()
    {
        var text = MainWindow.DescribeHealth(true, null, 0, 1);

        Assert.Contains("1 feed", text);
        Assert.DoesNotContain("1 feeds", text);
    }

    [Fact]
    public void Both_problems_at_once_are_both_reported()
    {
        var text = MainWindow.DescribeHealth(true, "boom", 4, 2);

        Assert.Contains("failing", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paused", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_stopped_scheduler_does_not_also_claim_the_ticks_are_failing()
    {
        // "Not running" already says nothing is being refreshed. Adding a
        // failing-tick line on top would describe a loop that is not turning.
        var text = MainWindow.DescribeHealth(false, "boom", 9, 0);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failing", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_pause_count_is_reported_even_while_the_scheduler_is_stopped()
    {
        var text = MainWindow.DescribeHealth(false, null, 0, 2);

        Assert.Contains("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 feeds", text);
    }
}
