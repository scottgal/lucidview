#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LucidReader.Services;

/// <summary>
/// Writes managed heap size and process resident set to a CSV at a fixed
/// interval, so a long soak can be read as a trend rather than described as
/// a feeling.
///
/// Debug-only, in the same way MYLO_DATA_DIR and the UI testing harness are:
/// the Release build has no sampler and no timer, and the file does not
/// compile into it at all. Off unless MYLO_MEMORY_LOG names a file, so even
/// a Debug run costs nothing unless it is asked for.
///
/// The two numbers, and why both:
///
/// - GC.GetTotalMemory(false) is the managed heap. It answers "is this app
///   retaining objects it should have let go", which is the question a leak
///   hunt is actually asking. Passing false rather than true is deliberate:
///   forcing a collection on every sample would flatten exactly the sawtooth
///   that shows how much is garbage and how much is retained.
/// - Process.WorkingSet64 is what the operating system says the process is
///   using. It includes the native side - decoded bitmaps, SQLite's page
///   cache, the renderer - none of which the managed number can see, and it
///   is the number a user would quote. It is also noisier and reclaimed
///   lazily, so a rise in RSS with a flat heap is usually the allocator
///   holding on, not a leak.
///
/// A third column records the collection counts, because a heap that grows
/// while gen2 never runs is a different story from one that grows across
/// collections.
/// </summary>
public sealed class MemorySampler : IDisposable
{
    private const string PathVariable = "MYLO_MEMORY_LOG";
    private const string IntervalVariable = "MYLO_MEMORY_LOG_SECONDS";

    private readonly string _path;
    private readonly Timer _timer;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    private int _disposed;

    private MemorySampler(string path, TimeSpan interval)
    {
        _path = path;

        File.WriteAllText(_path,
            "elapsed_seconds,managed_bytes,working_set_bytes,gen0,gen1,gen2\n");

        _timer = new Timer(_ => Sample(), null, interval, interval);
    }

    /// <summary>
    /// Returns a running sampler, or null when MYLO_MEMORY_LOG is not set.
    /// Never throws: a soak that cannot write its log is a soak with no log,
    /// not an app that will not start.
    /// </summary>
    public static MemorySampler? StartIfRequested()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable(PathVariable);
            if (string.IsNullOrWhiteSpace(path)) return null;

            var seconds = 5.0;
            var configured = Environment.GetEnvironmentVariable(IntervalVariable);
            if (!string.IsNullOrWhiteSpace(configured) &&
                double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > 0)
            {
                seconds = parsed;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            return new MemorySampler(path, TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Memory] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void Sample()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        try
        {
            _process.Refresh();

            var line = new StringBuilder()
                .Append((DateTimeOffset.UtcNow - _startedUtc).TotalSeconds.ToString("0", CultureInfo.InvariantCulture))
                .Append(',').Append(GC.GetTotalMemory(false).ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(_process.WorkingSet64.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(GC.CollectionCount(0))
                .Append(',').Append(GC.CollectionCount(1))
                .Append(',').Append(GC.CollectionCount(2))
                .Append('\n')
                .ToString();

            File.AppendAllText(_path, line);
        }
        catch (Exception)
        {
            // A sample that could not be taken or written is one missing row
            // in a soak log. It is never worth interrupting the run.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _timer.Dispose();
        _process.Dispose();
    }
}
#endif
