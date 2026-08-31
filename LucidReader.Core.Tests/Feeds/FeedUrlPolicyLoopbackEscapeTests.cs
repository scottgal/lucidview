using LucidReader.Core.Feeds;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// The Debug-only loopback allowance the scrape UI script needs, and the
/// limits on it.
///
/// The point of these tests is not that the flag works. It is that the flag
/// does not open anything except loopback: link-local (where the cloud
/// metadata endpoint lives) and the RFC1918 private ranges stay refused with
/// the flag set, and everything stays refused with it unset.
///
/// The environment variable is set and cleared inside each test rather than in
/// a fixture. These run in the same process as every other test, and a flag
/// left on would silently change what FeedUrlPolicyTests is asserting.
/// </summary>
[Collection(nameof(FeedUrlPolicyLoopbackEscapeTests))]
public class FeedUrlPolicyLoopbackEscapeTests : IDisposable
{
    private const string Flag = "MYLO_ALLOW_LOOPBACK_FEEDS";

    public void Dispose() => Environment.SetEnvironmentVariable(Flag, null);

    private static void Enable() => Environment.SetEnvironmentVariable(Flag, "1");

    [Theory]
    [InlineData("http://127.0.0.1:8099/blog")]
    [InlineData("http://localhost:8099/blog")]
    [InlineData("http://[::1]:8099/blog")]
    public void Loopback_is_refused_without_the_flag(string url)
    {
        Assert.False(FeedUrlPolicy.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8099/blog")]
    [InlineData("http://localhost:8099/blog")]
    [InlineData("http://[::1]:8099/blog")]
    public void Loopback_is_allowed_with_the_flag(string url)
    {
        Enable();

        Assert.True(FeedUrlPolicy.IsAllowed(url));
    }

    /// <summary>
    /// The addresses this policy exists for. None of them is loopback, so none
    /// of them is affected by the flag, and the metadata endpoint that made
    /// OPML import an SSRF primitive is refused either way.
    /// </summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://172.16.4.4/service")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fc00::1]/")]
    public void The_flag_does_not_open_link_local_or_private_addresses(string url)
    {
        Enable();

        Assert.False(FeedUrlPolicy.IsAllowed(url));
    }

    /// <summary>
    /// The IPv6 shapes that hide a loopback address inside a routable-looking
    /// one. The flag allows loopback deliberately, so these are permitted with
    /// it set - but they must still be refused without it, which is what this
    /// pins down.
    /// </summary>
    [Theory]
    [InlineData("http://[64:ff9b::7f00:1]/")]
    [InlineData("http://[2002:7f00:1::]/")]
    public void Disguised_loopback_is_still_refused_without_the_flag(string url)
    {
        Assert.False(FeedUrlPolicy.IsAllowed(url));
    }

    [Fact]
    public void Any_value_other_than_one_leaves_the_policy_closed()
    {
        Environment.SetEnvironmentVariable(Flag, "true");
        Assert.False(FeedUrlPolicy.IsAllowed("http://127.0.0.1:8099/blog"));

        Environment.SetEnvironmentVariable(Flag, "yes");
        Assert.False(FeedUrlPolicy.IsAllowed("http://127.0.0.1:8099/blog"));

        Environment.SetEnvironmentVariable(Flag, "");
        Assert.False(FeedUrlPolicy.IsAllowed("http://127.0.0.1:8099/blog"));
    }

    /// <summary>
    /// The flag opens the host check and nothing else. An address with
    /// embedded credentials or an unsupported scheme is still refused, so
    /// turning this on for a test run does not turn off the rest of the policy.
    /// </summary>
    [Fact]
    public void The_flag_does_not_relax_any_other_rule()
    {
        Enable();

        Assert.False(FeedUrlPolicy.IsAllowed("http://user:token@127.0.0.1:8099/blog"));
        Assert.False(FeedUrlPolicy.IsAllowed("file://127.0.0.1/blog"));
        Assert.False(FeedUrlPolicy.IsAllowed("ftp://127.0.0.1/blog"));
    }
}

/// <summary>
/// Keeps these tests off the parallel path. They mutate a process-wide
/// environment variable, and xunit runs collections in parallel by default, so
/// without this a run of FeedUrlPolicyTests could observe the flag set.
/// </summary>
[CollectionDefinition(nameof(FeedUrlPolicyLoopbackEscapeTests), DisableParallelization = true)]
public class FeedUrlPolicyLoopbackEscapeCollection;
