#!/usr/bin/env python3
"""Turns a memory soak log into a table and a verdict.

Reads the CSV LucidReader.Services.MemorySampler writes (Debug builds only,
when MYLO_MEMORY_LOG is set) and reports the managed heap and the process
resident set at intervals, plus whether the trend is flat.

The verdict is deliberately crude and stated in the open rather than dressed
up as statistics. Samples inside the warm-up window are dropped and the rest
is split in half; if the second half is within a tenth of the first, the
trend is called flat. A tenth is wide enough that ordinary allocator noise
and a lazily-returned working set do not read as growth, and narrow enough
that anything accumulating per cycle over hundreds of cycles does.

Three things about how that comparison is made, each of which was wrong
first and corrected against real logs from this app.

The managed heap is compared at its TROUGH, not its median. A generational
heap sawtooths: it climbs with allocation and drops when a collection runs,
so the median is mostly a measure of how much garbage is in flight. The
trough - the least the heap held at any point in the window - is the closest
thing to the live set, and the live set growing is what a leak is. Process
RSS is compared at the median instead, because it has no equivalent
collection point; the allocator returns pages lazily and its floor means
much less.

A verdict needs several gen2 collections inside the window. This is the
guard that matters most and the one whose absence produced a false alarm: a
five-minute run of this app collects gen2 about once, the heap has not yet
been fully collected in the second half, and the trough duly reports
thirteen percent of growth that a twenty-minute run of the same code shows
as one and a half. Fewer than MINIMUM_GEN2_COLLECTIONS and this declines to
answer.

And samples inside the warm-up window are dropped, because a run whose first
half is mostly settling reports that settling as growth. The window is ten
minutes, which is much longer than it looks like it needs to be and was
arrived at by measurement rather than by taste. Two twenty-two minute runs
of the same build were compared: with a ninety second window one reported
the live set up one and a half percent and the other up twenty-one, and with
a ten minute window they agreed at three and a half and four and a bit.
Nothing about the app differed between them - what differed was how far each
had got up the same settling curve when the window opened. Both ended at the
same live set. Ninety seconds covered startup; it did not cover the heap
finding its size.

The whole table is printed either way, so the numbers are always visible and
the verdict is a summary of them rather than a substitute for them.
"""
import csv
import statistics
import sys


# Ten minutes, measured rather than guessed. See the module docstring.
WARM_UP_SECONDS = 600

# Below this many post-warm-up samples the split is decided by noise.
MINIMUM_SAMPLES_FOR_VERDICT = 24

# And below this many gen2 collections in the window the heap has not been
# fully collected in both halves, so its trough is not yet a live-set
# measurement. See the module docstring.
MINIMUM_GEN2_COLLECTIONS = 3


def megabytes(value):
    return value / (1024.0 * 1024.0)


def read(path):
    with open(path) as handle:
        return [
            (int(row["elapsed_seconds"]), int(row["managed_bytes"]),
             int(row["working_set_bytes"]), int(row["gen0"]),
             int(row["gen1"]), int(row["gen2"]))
            for row in csv.DictReader(handle)
        ]


def verdict(name, rows, index, statistic):
    values = [row[index] for row in rows]
    half = len(values) // 2
    first = statistic(values[:half])
    second = statistic(values[half:])

    if first <= 0:
        return f"{name}: no baseline to compare against"

    change = (second - first) / first
    shape = "flat" if abs(change) <= 0.10 else ("GROWING" if change > 0 else "falling")

    label = statistic.__name__ if statistic is min else "median"

    return (f"{name}: first-half {label} {megabytes(first):.1f} MB, "
            f"second-half {label} {megabytes(second):.1f} MB, "
            f"{change * 100:+.1f}% -> {shape}")


def main():
    if len(sys.argv) < 2:
        print("usage: summarise-memory.py <memory.csv>", file=sys.stderr)
        return 2

    rows = read(sys.argv[1])
    if len(rows) < 4:
        print(f"Only {len(rows)} samples; too few to say anything.", file=sys.stderr)
        return 1

    steady = [row for row in rows if row[0] >= WARM_UP_SECONDS]

    step = max(1, len(rows) // 12)

    print()
    print("  elapsed   managed heap   process RSS   gen0  gen1  gen2")
    print("  -------   ------------   -----------   ----  ----  ----")
    for row in rows[::step] + [rows[-1]]:
        elapsed, managed, rss, gen0, gen1, gen2 = row
        print(f"  {elapsed:>6}s   {megabytes(managed):>9.1f} MB   "
              f"{megabytes(rss):>8.1f} MB   {gen0:>4}  {gen1:>4}  {gen2:>4}")

    print()
    print(f"  samples: {len(rows)} over {rows[-1][0]}s "
          f"({len(steady)} after the {WARM_UP_SECONDS}s warm-up)")

    gen2 = steady[-1][5] - steady[0][5] if steady else 0
    print(f"  gen2 collections in that window: {gen2}")

    if len(steady) < MINIMUM_SAMPLES_FOR_VERDICT or gen2 < MINIMUM_GEN2_COLLECTIONS:
        print(f"  too short for a verdict: a verdict needs at least "
              f"{MINIMUM_SAMPLES_FOR_VERDICT} steady-state samples and "
              f"{MINIMUM_GEN2_COLLECTIONS} gen2 collections. Run more cycles.")
        print()
        return 0

    print("  " + verdict("managed heap (live set)", steady, 1, min))
    print("  " + verdict("process RSS", steady, 2, statistics.median))
    print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
