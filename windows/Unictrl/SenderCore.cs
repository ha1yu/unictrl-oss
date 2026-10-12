namespace Unictrl;

/// Pure decision logic for the sender role (sharing this PC's keyboard and mouse with a Mac).
/// No WinUI and no P/Invoke, so it links into the test project like Arrangement and KeyTable.
internal static class SenderCore
{
    /// A cursor position, kept free of Native types so this file links into the test project.
    public readonly record struct Spot(int X, int Y);

    /// A local screen in physical pixels plus its scale, for edge detection and mm conversion.
    public readonly record struct Screen(int Left, int Top, int Right, int Bottom, double PixelsPerMm)
    {
        public int CenterY => (Top + Bottom) / 2;
    }

    /// The `enter` message fields when the pointer crosses to the receiver.
    public readonly record struct Entry(float YMm, byte Edge, bool LeftOpen, bool RightOpen);

    /// Whether `at` is on `screen`'s `right` (else left) edge, with no other screen just beyond it
    /// (an edge between two local monitors is not a way out). `screenExistsBeyond` answers whether
    /// any local screen covers a point.
    public static bool AtOuterEdge(Spot at, Screen screen, bool right, Func<int, int, bool> screenExistsBeyond)
    {
        if (at.Y < screen.Top || at.Y >= screen.Bottom) return false;
        // On the edge, or past it: a real cursor is clamped to the screen, but the position a hook
        // event reports can overshoot (fast flicks, injected input), and that is the same push.
        var onEdge = right ? at.X >= screen.Right - 2 : at.X <= screen.Left + 2;
        if (!onEdge) return false;
        var x = right ? screen.Right : screen.Left;
        return !screenExistsBeyond(right ? x : x - 1, at.Y);
    }

    /// The `enter` to send when the pointer has been pushed far enough past an open outer edge.
    /// `edge`/`open` flags are the receiver's: a receiver on this PC's right is entered through its
    /// left edge, and its edge facing this PC is the one that leads back. One receiver in this
    /// version, on the side `receiverOnRight` says; the far edge of the receiver is closed.
    public static Entry Cross(Screen screen, Spot at, bool receiverOnRight, double receiverOffsetMm) =>
        new((float)((at.Y - screen.CenterY) / screen.PixelsPerMm - receiverOffsetMm),
            Edge: (byte)(receiverOnRight ? 0 : 1),
            LeftOpen: receiverOnRight,
            RightOpen: !receiverOnRight);

    /// Where to put the cursor when the pointer comes back: at the edge that faces the receiver,
    /// at the height the receiver left at (its mm from its centre, plus how far below us it sits).
    public static Spot ReturnSpot(Screen screen, bool receiverOnRight, float yMm, double receiverOffsetMm)
    {
        var y = (int)Math.Clamp(screen.CenterY + (yMm + receiverOffsetMm) * screen.PixelsPerMm,
            screen.Top + 1, screen.Bottom - 1);
        return receiverOnRight ? new(screen.Right - 3, y) : new(screen.Left + 2, y);
    }

    /// Wheel deltas as the wire wants them: lines, + = content up. Windows reports multiples of 120
    /// per notch (finer for high-resolution wheels); the horizontal axis is inverted relative to the
    /// wire (the receiver negates it again when injecting, see Injector.Wheel).
    public static (float DyLines, float DxLines) Wheel(int wheelDelta, int hWheelDelta) =>
        (wheelDelta / 120f, -hWheelDelta / 120f);

    /// Accumulates outward motion while the cursor rests on an open outer edge. Windows clamps the
    /// cursor to the screen, so position deltas die at the edge; raw input counts do not, and the
    /// push they represent is what "push the pointer past the edge" means. Grazing the edge while
    /// moving along it, or resting there, does not cross. Raw counts arrive in packets that are not
    /// aligned with the hook events that drain them, so a drained zero is NOT a pause in the push;
    /// only motion inward resets it, and accumulated push goes stale after a moment.
    public sealed class EdgePush(double threshold = 40)
    {
        private double push;
        private DateTime lastOutward = DateTime.MinValue;

        /// Feed the outward raw counts since the last event while pinned at the edge.
        public bool Add(double outwardCounts)
        {
            if (outwardCounts < 0) { push = 0; return false; } // the hand turned inward
            if (outwardCounts == 0)
            {
                // Counts drained between packets; the push only goes stale with real time.
                if (DateTime.UtcNow - lastOutward > TimeSpan.FromMilliseconds(180)) push = 0;
                return false;
            }
            if (DateTime.UtcNow - lastOutward > TimeSpan.FromMilliseconds(180)) push = 0;
            push += outwardCounts;
            lastOutward = DateTime.UtcNow;
            if (push < threshold) return false;
            push = 0;
            return true;
        }

        /// The cursor left the edge.
        public void Reset() => push = 0;
    }
}
