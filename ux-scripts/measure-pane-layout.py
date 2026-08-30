#!/usr/bin/env python3
"""Measures where lucidREADER's three panes actually are, from a screenshot.

Called by run-pane-layout.sh with the four screenshots of the collapse cycle
and the one from the restarted process. Nothing here reads a property: the
panes are found by their colours, which is the only way to tell "the pane is
collapsed" from "the pane's contents are hidden but its column still holds
260 pixels open".

The three surfaces are distinguishable because the sidebar is the one
translucent one (SidebarBackgroundBrush over white, which lands on 236,236,238
in the light theme) and each GridSplitter paints a 4px band at 240,240,240.
So the pane boundaries are literally colour changes along a scanline taken low
enough in the window to be below the sidebar's sections and below the article
list's last row, where each pane is a flat field of its own background.

Run with --report to print the measurements without asserting, which is what
the numbers in the commit message came from.
"""
import sys
from PIL import Image

SIDEBAR = (236, 236, 238)
SPLITTER = (240, 240, 240)
WHITE = (255, 255, 255)

# The screenshot is captured at 1x, so these are device-independent pixels and
# a couple of pixels of tolerance is enough for antialiasing at a corner.
TOLERANCE = 3


def near(pixel, target):
    return all(abs(a - b) <= TOLERANCE for a, b in zip(pixel, target))


def measure(path):
    image = Image.open(path).convert("RGB")
    width, height = image.size
    pixels = image.load()

    # Low in the pane band: below the toolbar, above the status bar, and below
    # the content of every pane, so each pane is its own flat colour here.
    scanline = int(height * 0.9)
    row = [pixels[x, scanline] for x in range(width)]

    sidebar_columns = [x for x, c in enumerate(row) if near(c, SIDEBAR)]

    # Runs of splitter grey, kept only if they are about the 4px a GridSplitter
    # actually paints; a stray antialiased pixel is not a splitter.
    splitters = []
    run_start = None
    for x in range(width + 1):
        is_splitter = x < width and near(row[x], SPLITTER)
        if is_splitter and run_start is None:
            run_start = x
        elif not is_splitter and run_start is not None:
            if x - run_start >= 3:
                splitters.append((run_start, x - 1))
            run_start = None

    # The reading pane starts after the last splitter, or at 0 when there is
    # none left to find.
    reading_left = splitters[-1][1] + 1 if splitters else 0

    # The hairline the reading pane draws under the article's byline. It is
    # the one element that spans the reading column edge to edge, so it is a
    # direct reading of where ReadingColumnMetrics put the column and how wide
    # it made it. Article text will not do: the lines are left-aligned, so
    # their right-hand extent is a fact about the prose, not the layout.
    #
    # Found as the longest unbroken run of non-white pixels anywhere in the
    # reading pane rather than at a known y, because the y it sits at moves
    # with the font size and with whether the article has a hero image.
    best = (0, None, None)
    for y in range(60, height - 40):
        run = 0
        start = None
        for x in range(reading_left, width):
            if pixels[x, y] != WHITE:
                if run == 0:
                    start = x
                run += 1
                if run > best[0]:
                    best = (run, start, start + run - 1)
            else:
                run = 0

    column_length, column_left, column_right = best

    return {
        "image": path,
        "width": width,
        "sidebar_left": min(sidebar_columns) if sidebar_columns else None,
        "sidebar_right": max(sidebar_columns) if sidebar_columns else None,
        "sidebar_span": len(sidebar_columns),
        "splitters": splitters,
        "reading_left": reading_left,
        "reading_span": width - reading_left,
        "column_left": column_left,
        "column_right": column_right,
        "column_width": column_length,
        "column_centre": (column_left + column_right) / 2 if column_length else None,
        "left_margin": column_left - reading_left if column_length else None,
        "right_margin": width - 1 - column_right if column_length else None,
    }


def describe(label, m):
    print(
        f"{label}: window {m['width']}px  "
        f"sidebar {m['sidebar_left']}..{m['sidebar_right']} ({m['sidebar_span']}px)  "
        f"splitters {m['splitters']}  "
        f"reading pane starts x={m['reading_left']} ({m['reading_span']}px wide)  "
        f"reading column {m['column_left']}..{m['column_right']} "
        f"({m['column_width']}px, margins {m['left_margin']}/{m['right_margin']}) "
        f"centred at {m['column_centre']}"
    )


def main():
    paths = [a for a in sys.argv[1:] if not a.startswith("--")]
    report_only = "--report" in sys.argv

    three, two, one, back, restart = (measure(p) for p in paths[:5])

    labels = ("three-pane", "list-and-reading", "reading-only",
              "back-to-three-pane", "restart")
    for label, m in zip(labels, (three, two, one, back, restart)):
        describe(label, m)

    if report_only:
        return 0

    failures = []

    # Mode 1. The sidebar is a real 260px column and both splitters are there.
    if not (250 <= three["sidebar_span"] <= 265):
        failures.append(
            f"three-pane: the sidebar covers {three['sidebar_span']}px, expected about 260")
    if three["sidebar_left"] != 0:
        failures.append(
            f"three-pane: the sidebar starts at x={three['sidebar_left']}, expected 0")
    if len(three["splitters"]) != 2:
        failures.append(
            f"three-pane: found {len(three['splitters'])} splitters, expected 2")

    # Mode 2. The sidebar's pixels are gone, not merely blank: no grey column
    # at all, one splitter left, and the article list now starts at x=0.
    if two["sidebar_span"] != 0:
        failures.append(
            f"list-and-reading: {two['sidebar_span']}px of sidebar is still on screen, "
            "so the column was not collapsed")
    if len(two["splitters"]) != 1:
        failures.append(
            f"list-and-reading: found {len(two['splitters'])} splitters, expected 1")
    if two["splitters"] and two["splitters"][0][0] > 360:
        failures.append(
            f"list-and-reading: the remaining splitter is at x={two['splitters'][0][0]}, "
            "so the article list did not move to the left edge")

    # Mode 3. One pane, edge to edge, no splitter anywhere.
    if one["sidebar_span"] != 0:
        failures.append("reading-only: the sidebar is still on screen")
    if one["splitters"]:
        failures.append(
            f"reading-only: {len(one['splitters'])} splitter(s) still on screen, "
            "so a drag handle is attached to nothing")
    if one["reading_left"] != 0:
        failures.append(
            f"reading-only: the reading pane starts at x={one['reading_left']}, expected 0")

    # The reading pane genuinely got wider each time, which is the thing an
    # IsVisible assertion cannot see: a pane whose content was hidden but whose
    # column still held its width open would leave this centre exactly where it
    # was.
    centres = [three["column_centre"], two["column_centre"], one["column_centre"]]
    if any(c is None for c in centres):
        failures.append("the reading column was not found in one of the modes, "
                        "so nothing was measured")
    else:
        if not centres[0] > centres[1] > centres[2]:
            failures.append(
                f"the reading column did not move left as panes collapsed: centres {centres}")
        # Half a pixel out is a centred odd-width column, not a bug.
        if abs(centres[2] - (one["width"] - 1) / 2) > 2:
            failures.append(
                f"reading-only: the column is centred at {centres[2]}, "
                f"not on the window's own centre {(one['width'] - 1) / 2}")

    # The centred variable-margin column has to keep working as the pane
    # widens, not just survive it. In the three-pane layout the pane is too
    # narrow for the 760px the settings ask for, so the column is clamped;
    # collapsing a pane must hand it the full 760 and put the extra space into
    # the margins rather than into the text measure.
    for label, m in (("three-pane", three), ("list-and-reading", two),
                     ("reading-only", one)):
        if m["left_margin"] is None or abs(m["left_margin"] - m["right_margin"]) > 2:
            failures.append(
                f"{label}: the reading column's margins are "
                f"{m['left_margin']} and {m['right_margin']}, so it is not centred")
    if two["column_width"] <= three["column_width"]:
        failures.append(
            f"the reading column did not widen when the sidebar collapsed: "
            f"{three['column_width']}px then {two['column_width']}px")
    if one["left_margin"] <= two["left_margin"]:
        failures.append(
            "the reading column's margin did not grow when the article list collapsed, "
            "so the extra width went into the text measure instead")

    # A fourth click puts everything back exactly where it started.
    for key in ("sidebar_left", "sidebar_right", "sidebar_span", "reading_left"):
        if three[key] != back[key]:
            failures.append(
                f"the cycle did not return to where it started: {key} went "
                f"from {three[key]} to {back[key]}")

    # The restarted process came up in the collapsed layout, measured the same
    # way as mode 3 rather than taken on trust from the settings file.
    if restart["sidebar_span"] != 0 or restart["splitters"] or restart["reading_left"] != 0:
        failures.append(
            "restart: the window did not come back up as reading-pane-only")

    if failures:
        for f in failures:
            print("FAIL: " + f)
        return 1

    print(
        "PASS: the sidebar's 260px column is gone in mode 2 and the article list "
        "starts at the left edge, only the reading pane and no splitter is left in "
        "mode 3, the article moved left both times, a fourth click restored the "
        "starting layout exactly, and a restarted process came up collapsed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
