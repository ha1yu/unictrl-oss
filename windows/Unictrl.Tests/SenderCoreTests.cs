using Xunit;

namespace Unictrl.Tests;

/// The sender role's pure decision logic: edge crossing, the scan-code inverse of KeyTable, wheel
/// conversion and the edge-push accumulator.
public class SenderCoreTests
{
    private static readonly SenderCore.Screen Pc = new(0, 0, 1920, 1080, 1920 / 527.0); // ~527 mm wide

    [Fact]
    public void UsageInvertsTheBaseTable()
    {
        // Every (scan, E0) the receiver can inject must map back to a usage that means the same key,
        // except the documented 0x2B pair, which the VK separates.
        foreach (var usage in new ushort[] { 0x04, 0x1D, 0x28, 0x2C, 0x31, 0x33, 0x3A, 0x45, 0x58, 0x64, 0xE1, 0xE3, 0xE6 })
        {
            Assert.True(KeyTable.TryGetScanCode(usage, "mac-positions", out var scan, out var extended), $"0x{usage:X2}");
            Assert.True(KeyTable.TryGetUsage(scan, extended, 0, out var page, out var back), $"0x{usage:X2}");
            Assert.Equal(0x07, page);
            if (usage == 0x31) Assert.Equal(0x31, back); // the scan 0x2B pair keeps 0x31 without a VK
            else Assert.Equal(usage, back);
        }
    }

    [Fact]
    public void UsageComesFromScanCodesAndVirtualKeys()
    {
        Assert.True(KeyTable.TryGetUsage(0x1E, false, 0x41, out var page, out var usage));
        Assert.Equal((0x07, (ushort)0x04), (page, usage)); // A

        Assert.True(KeyTable.TryGetUsage(0x1D, true, 0xA3, out page, out usage));
        Assert.Equal((0x07, (ushort)0xE4), (page, usage)); // right ctrl (extended)

        Assert.True(KeyTable.TryGetUsage(0x2B, false, 0xDE, out page, out usage));
        Assert.Equal((0x07, (ushort)0x32), (page, usage)); // non-US #/~ picked by VK

        Assert.True(KeyTable.TryGetUsage(0, false, 0xAF, out page, out usage));
        Assert.Equal((0x0C, (ushort)0xE9), (page, usage)); // volume up arrives as a VK only

        Assert.False(KeyTable.TryGetUsage(0, false, 0xFF, out _, out _));
    }

    [Fact]
    public void CrossNamesTheReceiversEdges()
    {
        var entry = SenderCore.Cross(Pc, new(1919, 540), receiverOnRight: true, receiverOffsetMm: 30);
        Assert.Equal(0, entry.Edge);            // a receiver on my right is entered through its left edge
        Assert.True(entry.LeftOpen);            // its edge facing me is the way back
        Assert.False(entry.RightOpen);
        Assert.Equal(-30, entry.YMm, 1);        // its centre sits 30 mm below mine

        var left = SenderCore.Cross(Pc, new(1, 540), receiverOnRight: false, receiverOffsetMm: 0);
        Assert.Equal(1, left.Edge);
        Assert.True(left.RightOpen);
        Assert.False(left.LeftOpen);
    }

    [Fact]
    public void ReturnSpotClampsToTheScreen()
    {
        var spot = SenderCore.ReturnSpot(Pc, receiverOnRight: true, yMm: 0, receiverOffsetMm: 30);
        Assert.Equal(1917, spot.X);
        Assert.Equal(649, spot.Y); // centre + 30 mm × (1920/527) px/mm ≈ 109 px below centre

        var clamped = SenderCore.ReturnSpot(Pc, receiverOnRight: true, yMm: 5000, receiverOffsetMm: 0);
        Assert.Equal(1079, clamped.Y);
    }

    [Fact]
    public void WheelFollowsTheWireConventions()
    {
        Assert.Equal(1f, SenderCore.Wheel(120, 0).DyLines);        // one notch away from the user
        Assert.Equal(-2f, SenderCore.Wheel(-240, 0).DyLines);
        Assert.Equal(-1f, SenderCore.Wheel(0, 120).DxLines);       // horizontal is inverted on the wire
        Assert.Equal(0.5f, SenderCore.Wheel(60, 0).DyLines);       // high-resolution wheels send finer deltas
    }

    [Fact]
    public void PushAccumulatesUntilTheThreshold()
    {
        var push = new SenderCore.EdgePush(threshold: 40);
        Assert.False(push.Add(30));
        Assert.False(push.Add(5));       // 35, still short
        Assert.True(push.Add(10));       // 45 crosses
        Assert.False(push.Add(39));      // and starts over
        Assert.True(push.Add(1));

        Assert.False(push.Add(30));
        push.Reset();                    // movement turned inward or the cursor left the edge
        Assert.False(push.Add(5));
        Assert.False(push.Add(-3));      // inward counts cancel the push entirely
    }

    [Fact]
    public void OuterEdgesHaveNoScreenBeyond()
    {
        var monitors = new[] { Pc, new SenderCore.Screen(1920, 0, 3840, 1080, Pc.PixelsPerMm) };
        Assert.True(SenderCore.AtOuterEdge(new(0, 500), Pc, false, monitors));   // far left of the row
        Assert.True(SenderCore.AtOuterEdge(new(3839, 500), monitors[1], true, monitors));
        Assert.False(SenderCore.AtOuterEdge(new(1918, 500), Pc, true, monitors)); // a monitor continues here
        Assert.False(SenderCore.AtOuterEdge(new(900, 500), Pc, true, monitors));  // mid-screen
        Assert.False(SenderCore.AtOuterEdge(new(0, 1100), Pc, false, monitors));  // off the screen vertically
    }
}
