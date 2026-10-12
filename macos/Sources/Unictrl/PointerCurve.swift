import Foundation

/// Apple-style pointer acceleration for counts received from a Windows sender, as a pure function
/// so it can be tested. macOS applies its pointer curve to real hardware events only — events we
/// post are placed absolutely and never accelerated — so the curve is applied here, before
/// placement. Speeds are device counts per millisecond; the gain multiplies the displacement.
///
/// The tables follow the shape of Apple's pointer acceleration (gain 1 at rest, rising non-linearly
/// with speed; higher sensitivity levels rise sooner and further). Values are tuned to that shape
/// rather than byte-exact copies of the system tables; the tracking-speed preference selects the
/// level, interpolated continuously.
enum PointerCurve {
    /// One sensitivity level: (speed in counts/ms, gain) pairs, ascending by speed.
    typealias Level = [(speed: Double, gain: Double)]

    /// Levels 0-8 as the tracking-speed preference spans 0.0...3.0 (level = scaling × 8/3).
    static let levels: [Level] = [
        // 0: no acceleration at all.
        [(0, 1), (1_000, 1)],
        [(0, 1), (0.5, 1), (2, 1.4), (6, 2.0), (20, 3.0)],
        [(0, 1), (0.4, 1.1), (1.5, 1.6), (5, 2.4), (16, 3.6), (40, 5.0)],
        [(0, 1), (0.35, 1.15), (1.2, 1.8), (4, 2.8), (12, 4.2), (32, 6.0), (80, 8.0)],
        [(0, 1), (0.3, 1.2), (1.0, 2.0), (3, 3.2), (9, 4.8), (24, 7.0), (60, 9.5), (120, 11)],
        [(0, 1), (0.25, 1.3), (0.8, 2.3), (2.5, 3.7), (7, 5.6), (18, 8.2), (45, 11), (90, 13)],
        [(0, 1), (0.2, 1.4), (0.6, 2.6), (2, 4.3), (5.5, 6.6), (14, 9.6), (34, 12.5), (70, 15)],
        [(0, 1), (0.15, 1.6), (0.45, 3.0), (1.5, 5.0), (4, 7.8), (10, 11), (24, 14), (50, 17)],
        [(0, 1), (0.1, 1.9), (0.3, 3.5), (1.0, 5.8), (2.5, 9.0), (6, 12.5), (14, 16), (30, 19)],
    ]

    /// The gain for a hand speed, at a sensitivity level. `level` is 0.0...8.0; fractional values
    /// interpolate between the two neighbouring tables. Beyond the last entry of a table the last
    /// gain holds (fast flicks get the maximum multiplier, no runaway).
    static func gain(velocityCountsPerMs velocity: Double, level: Double) -> Double {
        let clamped = max(0, min(8, level))
        let lower = Int(clamped)
        let fraction = clamped - Double(lower)
        let low = gain(velocity: velocity, table: levels[lower])
        guard fraction > 0, lower + 1 < levels.count else { return low }
        let high = gain(velocity: velocity, table: levels[lower + 1])
        return low + (high - low) * fraction
    }

    /// The system's pointer tracking speed (System Settings › Mouse › Tracking speed) as a curve
    /// level. The preference is a global-domain number, 0.0...3.0; it is absent until the user
    /// moves the slider, and -1 means acceleration off. Missing or off map to the quiet middle.
    static func systemLevel() -> Double {
        let defaults = UserDefaults.standard.persistentDomain(forName: UserDefaults.globalDomain)
        let scaling = (defaults?["com.apple.mouse.scaling"] as? NSNumber)?.doubleValue ?? 1.0
        guard scaling >= 0 else { return 3 } // acceleration off: the gentlest curve with some gain
        return min(8, max(0, scaling * 8 / 3))
    }

    private static func gain(velocity: Double, table: Level) -> Double {
        if velocity <= table[0].speed { return table[0].gain }
        for (i, entry) in table.enumerated() where i > 0 {
            if velocity <= entry.speed {
                let previous = table[i - 1]
                let span = entry.speed - previous.speed
                let fraction = span > 0 ? (velocity - previous.speed) / span : 0
                return previous.gain + (entry.gain - previous.gain) * fraction
            }
        }
        return table.last!.gain
    }
}
