import CoreGraphics
import Foundation
import Testing
@testable import Unictrl

/// The receiver role's pure logic: the usage inverse the injector posts keys through, and the
/// millimetre conversions of the injected pointer.
@Suite struct InputInjectorTests {
    @Test func inverseCoversEveryKeyboardEntry() {
        // Every keycode the Mac can send must decode back to the same usage, and the inverse is
        // exactly the forward table flipped (usages are unique).
        #expect(KeyMap.inverse.count == KeyMap.keyboard.count)
        for (keycode, usage) in KeyMap.keyboard {
            #expect(KeyMap.inverse[usage] == keycode)
        }
    }

    @Test func inverseHoldsForWhatThePcSends() {
        // A sample of what the Windows sender's KeyTable inverse produces for hook scan codes.
        let common: [UInt16] = [0x04, 0x05, 0x1D, 0x1E, 0x28, 0x2A, 0x39, 0x3A, 0x3B, 0x4C, 0x4D, 0x52,
                                0x58, 0x64, 0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7]
        for usage in common {
            #expect(KeyMap.inverse[usage] != nil, "usage 0x\(String(usage, radix: 16)) has no Mac keycode")
        }
        // PrintScreen has no Mac key; the injector maps it to F13 (0x69) itself. F13's own usage
        // (0x68) decodes to that same keycode.
        #expect(KeyMap.inverse[0x46] == nil)
        #expect(KeyMap.inverse[0x68] == 0x69)
    }

    @Test func entryYConvertsAndClamps() {
        let screen = CGRect(x: 0, y: 0, width: 1512, height: 982)
        let pointsPerMm = 1512.0 / 344.0 // a typical 14" panel
        #expect(InjectGeometry.entryY(yMm: 0, screen: screen, pointsPerMm: pointsPerMm)
                == Double(screen.midY))
        let below = InjectGeometry.entryY(yMm: 30, screen: screen, pointsPerMm: pointsPerMm)
        #expect(abs(below - (Double(screen.midY) + 30 * pointsPerMm)) < 0.001)
        #expect(InjectGeometry.entryY(yMm: -10_000, screen: screen, pointsPerMm: pointsPerMm) == Double(screen.minY + 1))
        #expect(InjectGeometry.entryY(yMm: 10_000, screen: screen, pointsPerMm: pointsPerMm) == Double(screen.maxY - 1))
    }

    @Test func exitYMmIsEntryInverted() {
        let screen = CGRect(x: -100, y: 50, width: 800, height: 600)
        let pointsPerMm = 4.0
        let y = InjectGeometry.entryY(yMm: -12.5, screen: screen, pointsPerMm: pointsPerMm)
        #expect(abs(InjectGeometry.exitYMm(y: y, screen: screen, pointsPerMm: pointsPerMm) - -12.5) < 1e-9)
    }

    @Test func rawCountsConvertToMillimetres() {
        // 40 counts per mm: a count is a fortieth of a millimetre.
        #expect(InputInjector.rawCountsPerMm == 40)
    }
}

/// The Apple-style acceleration applied to counts from a Windows sender.
@Suite struct PointerCurveTests {
    @Test func restAndSlowMotionStayLinear() {
        #expect(PointerCurve.gain(velocityCountsPerMs: 0, level: 4) == 1)
        #expect(PointerCurve.gain(velocityCountsPerMs: 0, level: 0) == 1)
        // A slow deliberate hand barely gains, at any sensitivity.
        #expect(PointerCurve.gain(velocityCountsPerMs: 0.05, level: 4) < 1.5)
    }

    @Test func gainRisesWithSpeedAndLevel() {
        #expect(PointerCurve.gain(velocityCountsPerMs: 5, level: 4) > 2)
        #expect(PointerCurve.gain(velocityCountsPerMs: 20, level: 4) > PointerCurve.gain(velocityCountsPerMs: 5, level: 4))
        // Higher sensitivity levels gain more at the same speed.
        #expect(PointerCurve.gain(velocityCountsPerMs: 3, level: 6) > PointerCurve.gain(velocityCountsPerMs: 3, level: 2))
    }

    @Test func fractionalLevelsInterpolate() {
        let low = PointerCurve.gain(velocityCountsPerMs: 3, level: 3)
        let high = PointerCurve.gain(velocityCountsPerMs: 3, level: 4)
        let middle = PointerCurve.gain(velocityCountsPerMs: 3, level: 3.5)
        #expect(middle > low && middle < high)
    }

    @Test func extremesAreClamped() {
        #expect(PointerCurve.gain(velocityCountsPerMs: 1_000, level: 4).isFinite)
        #expect(PointerCurve.gain(velocityCountsPerMs: 1_000, level: 4) == PointerCurve.gain(velocityCountsPerMs: 5_000, level: 4)) // past the last entry the gain holds
        #expect(PointerCurve.gain(velocityCountsPerMs: 1, level: -5) == PointerCurve.gain(velocityCountsPerMs: 1, level: 0))
        #expect(PointerCurve.gain(velocityCountsPerMs: 1, level: 99) == PointerCurve.gain(velocityCountsPerMs: 1, level: 8))
    }

    @Test func systemLevelStaysInRange() {
        let level = PointerCurve.systemLevel()
        #expect(level >= 0 && level <= 8)
    }
}
