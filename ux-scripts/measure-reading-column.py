#!/usr/bin/env python3
"""Measures the reading column's left and right margins from a pane snip.

Called by run-reading-column.sh. Given the reading-pane.png the harness
captures, it finds the hairline Rectangle that sits between the article header
and the rendered markdown: that is the only element in the column that spans
the column's whole width, so its two ends give both margins exactly. Article
text would not, because every line ends where its last word ends.

The hairline is found without knowing its colour or its y position: for every
row it takes the span between the first and last pixel that differs from the
pane background (sampled from the top-left corner), and keeps the widest row.

Prints one line of JSON so the shell script can compare two runs.
"""

import json
import sys

from PIL import Image

# A pixel counts as content when any channel differs from the background by
# more than this. Antialiasing on a hairline against a near-matching
# background is faint, so the threshold is low; it still rejects the JPEG-free
# PNG's exact-match background.
CHANNEL_TOLERANCE = 6


def measure(path):
    image = Image.open(path).convert("RGB")
    width, height = image.size
    pixels = image.load()

    background = pixels[0, 0]

    def is_content(pixel):
        return any(abs(a - b) > CHANNEL_TOLERANCE for a, b in zip(pixel, background))

    best = None
    content_top = None
    content_bottom = None
    for y in range(height):
        first = None
        last = None
        for x in range(width):
            if is_content(pixels[x, y]):
                if first is None:
                    first = x
                last = x
        if first is None:
            continue
        if content_top is None:
            content_top = y
        content_bottom = y
        span = last - first
        if best is None or span > best[0]:
            best = (span, y, first, last)

    if best is None:
        raise SystemExit(f"{path}: no content pixels at all, background {background}")

    span, y, first, last = best
    return {
        "image": path,
        "image_width": width,
        "image_height": height,
        # The hairline: the widest row, and so the column's true extent.
        "widest_row_y": y,
        "left_margin": first,
        "right_margin": width - 1 - last,
        "column_span": span + 1,
        # How far down the article reaches. Bigger type and looser lines both
        # push this down, which is how the typography settings are checked
        # without asserting a property that a Style could be overriding.
        "content_top": content_top,
        "content_bottom": content_bottom,
    }


if __name__ == "__main__":
    print(json.dumps(measure(sys.argv[1])))
