using MarkdownViewer.Services;

namespace MarkdownViewer.Tests;

/// <summary>
/// Image-sizing rules baked into MarkdownService — locked down because the
/// in-cell clipping regression in v3.0.0/v3.0.1 came from breaking these
/// invariants. The actual on-screen layout depends on Avalonia and lives
/// behind the UI-test harness, but the emit contract these tests cover is
/// what the renderer's downstream paths (HtmlInlineNode + AsyncImageLoader)
/// rely on.
/// </summary>
public class ImageSizingTests
{
    // ---------- ClampToContainerSize ----------

    [Fact]
    public void Clamp_SmallImage_StaysAtNaturalSize()
    {
        // Shields and badges (well under the column cap) MUST render at their
        // intrinsic dims so we get pixel parity with the browser.
        var (w, h) = MarkdownService.ClampToContainerSize(80, 20);
        Assert.Equal(80, w);
        Assert.Equal(20, h);
    }

    [Fact]
    public void Clamp_MediumImage_StaysAtNaturalSize()
    {
        // A 400x300 thumbnail fits inside 900x700 — no clamping.
        var (w, h) = MarkdownService.ClampToContainerSize(400, 300);
        Assert.Equal(400, w);
        Assert.Equal(300, h);
    }

    [Fact]
    public void Clamp_AtExactCap_StaysAtNaturalSize()
    {
        var (w, h) = MarkdownService.ClampToContainerSize(900, 700);
        Assert.Equal(900, w);
        Assert.Equal(700, h);
    }

    [Fact]
    public void Clamp_WideImage_ShrinksPreservingAspect()
    {
        // A 1470×867 user-manual screenshot must clamp to 900 wide and a
        // proportional 531 high — that's what made the welcome image render
        // at a sensible size in v3.0.1+ instead of filling the viewport.
        var (w, h) = MarkdownService.ClampToContainerSize(1470, 867);
        Assert.Equal(900, w);
        // 867 * (900/1470) = 530.816 → rounds to 531
        Assert.Equal(531, h);
    }

    [Fact]
    public void Clamp_TallImage_ShrinksPreservingAspect()
    {
        // A 600×1400 portrait clamps to height=700 (the cap), width scales
        // proportionally.
        var (w, h) = MarkdownService.ClampToContainerSize(600, 1400);
        // 600 * (700/1400) = 300
        Assert.Equal(300, w);
        Assert.Equal(700, h);
    }

    [Fact]
    public void Clamp_BothDimsOversize_ShrinksToTighterRatio()
    {
        // Bigger than cap in both dims — use whichever ratio shrinks more.
        var (w, h) = MarkdownService.ClampToContainerSize(2000, 1500);
        // widthRatio = 900/2000 = 0.45; heightRatio = 700/1500 = 0.467
        // minRatio = 0.45 → 2000*0.45=900, 1500*0.45=675
        Assert.Equal(900, w);
        Assert.Equal(675, h);
    }

    [Fact]
    public void Clamp_NeverReturnsZero()
    {
        // Defensive: even pathological inputs must round to at least 1px.
        var (w, h) = MarkdownService.ClampToContainerSize(1_000_000, 1);
        Assert.True(w >= 1);
        Assert.True(h >= 1);
    }

    // ---------- Aspect ratio invariant ----------

    [Theory]
    [InlineData(1470, 867)]
    [InlineData(2000, 1500)]
    [InlineData(80, 20)]
    [InlineData(400, 300)]
    [InlineData(1200, 800)]
    public void Clamp_PreservesAspectRatio_WithinHalfPercent(int srcW, int srcH)
    {
        // Aspect must be preserved within rounding noise. A drifting aspect
        // ratio means images render stretched or squashed — the exact
        // regression the in-cell rendering was tripping on before MinHeight
        // was wired through to the host TextBlock.
        var (clampW, clampH) = MarkdownService.ClampToContainerSize(srcW, srcH);
        var srcAspect = (double)srcW / srcH;
        var clampAspect = (double)clampW / clampH;
        var driftPct = Math.Abs(clampAspect - srcAspect) / srcAspect;
        Assert.True(driftPct < 0.005,
            $"aspect drifted {driftPct:P3}: src {srcW}×{srcH} (aspect {srcAspect:F4}) → clamp {clampW}×{clampH} (aspect {clampAspect:F4})");
    }

    [Theory]
    [InlineData(800, 600)]   // exactly under cap
    [InlineData(900, 700)]   // exactly at cap
    [InlineData(901, 701)]   // 1px over each
    [InlineData(1920, 1080)] // standard HD
    public void Clamp_ResultFitsWithinCap(int srcW, int srcH)
    {
        // Hard invariant: nothing emitted ever exceeds the cap. If this
        // breaks, large images escape the inline measure rebuild and render
        // outside their container — the symptom the v3.0.0/v3.0.1 release
        // train shipped.
        var (w, h) = MarkdownService.ClampToContainerSize(srcW, srcH);
        Assert.True(w <= 900, $"width {w} exceeded 900 cap");
        Assert.True(h <= 700, $"height {h} exceeded 700 cap");
    }

    // ---------- Table-column-aware clamping ----------

    [Fact]
    public void Clamp_InTwoColumnTable_HalvesTheCap()
    {
        // 2-column table → each cell gets ~MaxContentWidth/2 minus a small
        // inter-cell padding allowance. A 1470×867 screenshot must clamp
        // tighter than the standalone 900 cap so the table doesn't push out
        // past the document edge.
        var (w, h) = MarkdownService.ClampToContainerSize(1470, 867, tableColumns: 2);
        Assert.True(w <= 434, $"2-col cell width {w} exceeded 434 budget");
        Assert.True(h <= 334, $"2-col cell height {h} exceeded 334 budget");
    }

    [Fact]
    public void Clamp_InThreeColumnTable_ThirdsTheCap()
    {
        var (w, h) = MarkdownService.ClampToContainerSize(1470, 867, tableColumns: 3);
        Assert.True(w <= 284, $"3-col cell width {w} exceeded 284 budget");
        // Aspect preserved
        var srcAspect = 1470.0 / 867;
        var clampAspect = (double)w / h;
        Assert.True(Math.Abs(clampAspect - srcAspect) / srcAspect < 0.01,
            $"aspect drifted in 3-col case: src {srcAspect:F3} → clamp {clampAspect:F3}");
    }

    [Fact]
    public void Clamp_ShieldInTable_StaysAtNaturalSize()
    {
        // 80×20 shield in a 2-col table → 80×20 still fits under the 434 budget.
        var (w, h) = MarkdownService.ClampToContainerSize(80, 20, tableColumns: 2);
        Assert.Equal(80, w);
        Assert.Equal(20, h);
    }

    [Fact]
    public void Clamp_TableColumnsZero_BehavesAsStandalone()
    {
        // The default — no table context — must produce the same result as
        // calling the two-arg overload. Guards against accidentally narrowing
        // every standalone image when this parameter is added.
        var standalone = MarkdownService.ClampToContainerSize(1470, 867);
        var zeroCols = MarkdownService.ClampToContainerSize(1470, 867, tableColumns: 0);
        Assert.Equal(standalone, zeroCols);
    }

    [Fact]
    public void Clamp_TableColumnsOne_BehavesAsStandalone()
    {
        // A single-column table isn't really a "table" for layout purposes —
        // it's a vertical stack. Don't narrow.
        var standalone = MarkdownService.ClampToContainerSize(1470, 867);
        var oneCol = MarkdownService.ClampToContainerSize(1470, 867, tableColumns: 1);
        Assert.Equal(standalone, oneCol);
    }

    [Fact]
    public void Clamp_NeverShrinksBelowFloor()
    {
        // 20-column "table" (pathological) — budget would be MaxContentWidth/20 = 45px,
        // but the floor keeps cells legibly sized rather than collapsing.
        var (w, _) = MarkdownService.ClampToContainerSize(1470, 867, tableColumns: 20);
        Assert.True(w >= 120, $"floor breached: {w}");
    }
}