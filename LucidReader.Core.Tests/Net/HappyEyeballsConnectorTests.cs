using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LucidReader.Core.Net;
using Xunit;

namespace LucidReader.Core.Tests.Net;

/// <summary>
/// The connect race that ends the IPv6 hang.
///
/// Nothing here touches the internet. Reachable addresses are loopback
/// listeners this fixture starts and stops itself; unreachable ones are either
/// a loopback port with nothing bound to it (refused immediately, so a test can
/// assert on the failure rather than wait for one) or an injected connect step
/// that never completes. The timing tests use the injected step for the same
/// reason: how long a real SYN takes to go unanswered is a property of the
/// machine, not of this class.
/// </summary>
public sealed class HappyEyeballsConnectorTests
{
    private static readonly IPAddress FakeV4 = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress FakeV4Other = IPAddress.Parse("192.0.2.2");
    private static readonly IPAddress FakeV6 = IPAddress.Parse("2001:db8::1");

    /// <summary>
    /// A loopback listener plus the port it is on. Also used to mint a port
    /// that is guaranteed to have had nothing on it: bind, read the port,
    /// close.
    /// </summary>
    private sealed class Listener : IDisposable
    {
        private readonly Socket _socket;

        public Listener()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _socket.Listen(16);
            Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        }

        public int Port { get; }

        public Task<Socket> AcceptAsync() => _socket.AcceptAsync();

        public void Dispose() => _socket.Dispose();
    }

    private static int ClosedPort()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task Connects_to_a_listening_loopback_address()
    {
        using var listener = new Listener();
        var connector = new HappyEyeballsConnector();

        using var socket = await connector.ConnectToAnyAsync(
            [IPAddress.Loopback], listener.Port, CancellationToken.None);

        Assert.True(socket.Connected);
    }

    [Fact]
    public async Task Every_address_unreachable_surfaces_a_socket_error_rather_than_hanging()
    {
        var port = ClosedPort();
        var connector = new HappyEyeballsConnector();

        // Bounded so a regression that reintroduces the hang fails the test
        // instead of stalling the run.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<SocketException>(() => connector.ConnectToAnyAsync(
            [IPAddress.Loopback, IPAddress.Loopback], port, bound.Token));
    }

    [Fact]
    public async Task An_empty_address_list_is_a_socket_error()
    {
        var connector = new HappyEyeballsConnector();

        var ex = await Assert.ThrowsAsync<SocketException>(
            () => connector.ConnectToAnyAsync([], 80, CancellationToken.None));

        Assert.Equal(SocketError.HostNotFound, ex.SocketErrorCode);
    }

    [Fact]
    public async Task One_reachable_address_among_unreachable_ones_still_connects()
    {
        using var listener = new Listener();

        // Every address of a host shares one port, so the unreachable entries
        // cannot simply be given a dead port of their own. Injecting the
        // connect step keeps the shape honest instead: two addresses fail, one
        // succeeds, and the caller gets the one that succeeded.
        var connector = new HappyEyeballsConnector(
            TimeSpan.Zero,
            resolve: null,
            connect: async (address, port, ct) =>
            {
                if (!address.Equals(IPAddress.Loopback))
                    throw new SocketException((int)SocketError.HostUnreachable);

                return await ConnectRealAsync(port, ct);
            });

        using var socket = await connector.ConnectToAnyAsync(
            [FakeV4, IPAddress.Loopback, FakeV4Other], listener.Port, CancellationToken.None);

        Assert.True(socket.Connected);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_stops_before_any_attempt()
    {
        var attempted = 0;
        var connector = new HappyEyeballsConnector(
            TimeSpan.Zero,
            resolve: null,
            connect: (_, _, _) =>
            {
                Interlocked.Increment(ref attempted);
                return Task.FromException<Socket>(new SocketException((int)SocketError.HostUnreachable));
            });

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.ConnectToAnyAsync([FakeV4], 80, cancelled.Token));

        Assert.Equal(0, Volatile.Read(ref attempted));
    }

    [Fact]
    public async Task Cancelling_while_attempts_are_in_flight_is_honoured()
    {
        var connector = new HappyEyeballsConnector(
            TimeSpan.Zero,
            resolve: null,
            connect: async (_, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new UnreachableException();
            });

        using var cancelling = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.ConnectToAnyAsync([FakeV4, FakeV4Other], 80, cancelling.Token));
    }

    [Fact]
    public async Task Losing_sockets_are_disposed_rather_than_left_open()
    {
        using var listener = new Listener();

        // Both addresses are IPv4, so there is no head start between them and
        // both attempts are launched together. Both connect; only one can be
        // returned, and the test is that the other one is actually closed
        // rather than leaked into the pool of things nobody owns.
        var connector = new HappyEyeballsConnector(
            TimeSpan.Zero,
            resolve: null,
            connect: (_, port, ct) => ConnectRealAsync(port, ct));

        var firstAccept = listener.AcceptAsync();
        var secondAccept = listener.AcceptAsync();

        using var winner = await connector.ConnectToAnyAsync(
            [FakeV4, FakeV4Other], listener.Port, CancellationToken.None);

        using var acceptedA = await firstAccept.WaitAsync(TimeSpan.FromSeconds(10));
        using var acceptedB = await secondAccept.WaitAsync(TimeSpan.FromSeconds(10));

        // Exactly one of the two connections should have gone away. A socket
        // that was disposed is seen from the other end as a clean end of
        // stream, so a zero-length read is the observation.
        var closedCount =
            (await ReadsEndOfStreamAsync(acceptedA) ? 1 : 0) +
            (await ReadsEndOfStreamAsync(acceptedB) ? 1 : 0);

        Assert.Equal(1, closedCount);
        Assert.True(winner.Connected);
    }

    [Fact]
    public async Task The_preferred_family_gets_a_head_start_before_the_other_one_is_dialled()
    {
        using var listener = new Listener();

        var startedAt = new Dictionary<AddressFamily, TimeSpan>();
        var gate = new object();
        var clock = Stopwatch.StartNew();

        var connector = new HappyEyeballsConnector(
            TimeSpan.FromMilliseconds(400),
            resolve: null,
            connect: async (address, port, ct) =>
            {
                lock (gate) startedAt.TryAdd(address.AddressFamily, clock.Elapsed);

                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // The whole bug in one line: an address that accepts the
                    // connect attempt and never answers it.
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    throw new UnreachableException();
                }

                return await ConnectRealAsync(port, ct);
            });

        using var socket = await connector.ConnectToAnyAsync(
            [FakeV6, IPAddress.Loopback], listener.Port, CancellationToken.None);

        Assert.True(socket.Connected);
        Assert.Contains(AddressFamily.InterNetworkV6, startedAt.Keys);
        Assert.Contains(AddressFamily.InterNetwork, startedAt.Keys);

        // The IPv6 attempt went first, and the IPv4 one waited for the head
        // start rather than being fired alongside it.
        Assert.True(startedAt[AddressFamily.InterNetwork] >= TimeSpan.FromMilliseconds(300),
            $"IPv4 started after only {startedAt[AddressFamily.InterNetwork].TotalMilliseconds}ms");
    }

    [Fact]
    public async Task The_other_family_starts_at_once_when_the_preferred_one_fails_quickly()
    {
        using var listener = new Listener();
        var clock = Stopwatch.StartNew();

        // A head start long enough that waiting it out would be obvious.
        var connector = new HappyEyeballsConnector(
            TimeSpan.FromSeconds(5),
            resolve: null,
            connect: (address, port, ct) => address.AddressFamily == AddressFamily.InterNetworkV6
                ? Task.FromException<Socket>(new SocketException((int)SocketError.NetworkUnreachable))
                : ConnectRealAsync(port, ct));

        using var socket = await connector.ConnectToAnyAsync(
            [FakeV6, IPAddress.Loopback], listener.Port, CancellationToken.None);

        Assert.True(socket.Connected);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3),
            $"waited {clock.Elapsed.TotalSeconds}s for a family that had already failed");
    }

    [Fact]
    public void IPv6_leads_when_the_host_publishes_both_families()
    {
        var (first, second) = HappyEyeballsConnector.SplitByFamily(
            [IPAddress.Loopback, FakeV6, FakeV4]);

        Assert.Equal(new[] { FakeV6 }, first);
        Assert.Equal(new[] { IPAddress.Loopback, FakeV4 }, second);
    }

    [Fact]
    public void An_IPv4_only_host_leads_with_IPv4_and_has_nothing_to_follow_with()
    {
        var (first, second) = HappyEyeballsConnector.SplitByFamily([IPAddress.Loopback, FakeV4]);

        Assert.Equal(new[] { IPAddress.Loopback, FakeV4 }, first);
        Assert.Empty(second);
    }

    private static async Task<Socket> ConnectRealAsync(int port, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), ct);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<bool> ReadsEndOfStreamAsync(Socket accepted)
    {
        var buffer = new byte[1];
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            return await accepted.ReceiveAsync(buffer, SocketFlags.None, bound.Token) == 0;
        }
        catch (OperationCanceledException)
        {
            // Still open and still silent, which is what the winner looks like.
            return false;
        }
        catch (SocketException)
        {
            // A reset counts as gone too.
            return true;
        }
    }
}
