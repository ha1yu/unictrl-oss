import AppKit
import CoreGraphics

/// Replays a sender's input on this Mac, the mirror of the Windows app's Injector. Mouse motion
/// arrives in millimetres (physical mode) or raw counts (native mode) and moves a virtual position
/// that is posted as absolute events: macOS has no public relative-motion API and never runs its
/// pointer ballistics on injected input, so the physical mode is the honest one. Keys arrive as HID
/// usages of the physical key (KeyMap inverted) and are posted as virtual-key events, so the Mac's
/// own layout and IME decide the character. Everything posted is tagged with ScrollPoster.tag so
/// Unictrl's own tap ignores it.
///
/// All state lives on one serial `userInteractive` queue (CGEventPost is thread-safe): the pointer
/// must never wait behind AppKit or the UI, which is what the sender's tap thread does for the
/// other direction.
final class InputInjector {
    /// Sends a message back to the peer that is driving us (`leave`).
    var send: ((MessageWriter) -> Void)?
    /// The pointer arrived here / went home, for the menu UI. Always called on the main thread.
    var onActiveChange: ((Bool) -> Void)?
    /// The Mac's wheel settings, applied to what the sender forwards (set from AppController).
    var reverseWheel = false
    var smoothWheel = true
    /// Where the smoother's glide lands; AppController points it at ScrollPoster.
    var localScrollHandler: (Double, Double) -> Void = { _, _ in }

    /// Raw mouse counts to a millimetre, for the native pointer mode. A typical mouse reports
    /// 800-1600 counts an inch; 40 counts/mm is the middle of that range.
    static let rawCountsPerMm = 40.0

    private let queue = DispatchQueue(label: "unictrl.inject", qos: .userInteractive)
    private let localScroll = ScrollSmoother()
    private var active = false
    private var screen = CGRect.zero
    private var screenID: CGDirectDisplayID = 0
    private var pointsPerMm = 5.0
    private var x = 0.0, y = 0.0
    private var openLeft = false, openRight = false
    private var suppressEdgeUntil = Date.distantPast
    private var flags: CGEventFlags = []
    private var heldKeys: Set<UInt16> = []
    private var heldButtons: [Bool] = [false, false, false, false, false]
    private var source: CGEventSource?
    /// While the pointer is away, a local mouse or trackpad brings the cursor back (as CursorHider does).
    private var parked: CGPoint?
    private var localWatch: DispatchSourceTimer?

    init() {
        localScroll.emit = { [weak self] dx, dy in
            DispatchQueue.main.async { self?.localScrollHandler(dx, dy) }
        }
        queue.sync { self.source = CGEventSource(stateID: .privateState) }
    }

    /// Whether the pointer is here right now; safe from any thread.
    var isActive: Bool { queue.sync { active } }

    // MARK: Arriving and leaving

    func enter(yMm: Float, fromRight: Bool, leftOpen: Bool, rightOpen: Bool, resume: Bool) {
        queue.async { self.enterNow(yMm: yMm, fromRight: fromRight, leftOpen: leftOpen, rightOpen: rightOpen, resume: resume) }
    }

    /// The pointer is no longer ours: the edge it left through, or why (disconnect, quit).
    func gone(_ reason: String) {
        queue.async { self.goneNow(reason) }
    }

    private func enterNow(yMm: Float, fromRight: Bool, leftOpen: Bool, rightOpen: Bool, resume: Bool) {
        guard let geometry = DisplayGeometry.main else { return }
        screen = geometry.bounds
        screenID = geometry.id
        pointsPerMm = geometry.pointsPerMm
        // No edge flags (a v1 sender): only the entry edge leads back.
        if leftOpen || rightOpen {
            openLeft = leftOpen
            openRight = rightOpen
        } else {
            openLeft = !fromRight
            openRight = fromRight
        }
        if resume, let here = CGEvent(source: nil)?.location, screen.contains(here) {
            x = here.x
            y = here.y
        } else {
            x = fromRight ? screen.maxX - 2 : screen.minX + 2
            y = InjectGeometry.entryY(yMm: Double(yMm), screen: screen, pointsPerMm: pointsPerMm)
        }
        heldKeys = []
        heldButtons = [false, false, false, false, false]
        flags = []
        localScroll.stop()
        stopLocalWatch()
        CursorVisibility.show()
        CursorShield.hideLater()
        active = true
        suppressEdgeUntil = Date().addingTimeInterval(0.25) // let the hand finish the gesture
        if let source {
            postMouseMoved(dx: 0, dy: 0, source: source)
        }
        announce(true)
        Log.write("inject_enter", "from_right=\(fromRight) y_mm=\(yMm) points_per_mm=\(String(format: "%.2f", pointsPerMm))")
    }

    private func goneNow(_ reason: String) {
        guard active else { return }
        active = false
        releaseHeld()
        if reason.hasSuffix("_edge") {
            // Gone back to the sender: hide our cursor while the pointer is there, as the Mac does
            // in the sender role and the PC's CursorHider does in the receiver role.
            CursorVisibility.hide()
            CursorShield.showLater(on: screenID)
            startLocalWatch()
        } else {
            CursorVisibility.show()
            CursorShield.hideLater()
            stopLocalWatch()
        }
        announce(false)
        Log.write("inject_leave", "reason=\(reason)")
    }

    /// UI callbacks back on the main thread.
    private func announce(_ value: Bool) {
        DispatchQueue.main.async { [weak self] in self?.onActiveChange?(value) }
    }

    // MARK: Motion

    func move(dxMm: Float, dyMm: Float) {
        queue.async { self.moveNow(dxMm: dxMm, dyMm: dyMm) }
    }

    func moveRaw(dx: Float, dy: Float) {
        queue.async { self.moveRawNow(dx: dx, dy: dy) }
    }

    private func moveNow(dxMm: Float, dyMm: Float) {
        applyMotion(dx: Double(dxMm) * pointsPerMm, dy: Double(dyMm) * pointsPerMm)
    }

    private func moveRawNow(dx: Float, dy: Float) {
        applyMotion(dx: Double(dx) / Self.rawCountsPerMm * pointsPerMm,
                    dy: Double(dy) / Self.rawCountsPerMm * pointsPerMm)
    }

    private func applyMotion(dx: Double, dy: Double) {
        guard active, let source else { return }
        x += dx
        y += dy
        x = x.clamped(to: screen.minX...screen.maxX - 1)
        y = y.clamped(to: screen.minY...screen.maxY - 1)
        if Date() >= suppressEdgeUntil {
            if openRight, x >= screen.maxX - 1 { exitEdge(right: true); return }
            if openLeft, x <= screen.minX { exitEdge(right: false); return }
        }
        postMouseMoved(dx: dx, dy: dy, source: source)
    }

    private func exitEdge(right: Bool) {
        var leave = MessageWriter(.leave)
        leave.f32(Float((y - screen.midY) / pointsPerMm))
        leave.u8(right ? 1 : 0)
        send?(leave)
        goneNow(right ? "right_edge" : "left_edge")
    }

    private func postMouseMoved(dx: Double, dy: Double, source: CGEventSource) {
        guard let event = CGEvent(mouseEventSource: source, mouseType: .mouseMoved,
                                  mouseCursorPosition: CGPoint(x: x, y: y), mouseButton: .center) else { return }
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.setDoubleValueField(.mouseEventDeltaX, value: dx)
        event.setDoubleValueField(.mouseEventDeltaY, value: dy)
        event.post(tap: .cghidEventTap)
    }

    // MARK: Buttons and wheel

    func button(_ button: UInt8, down: Bool) {
        queue.async { self.buttonNow(button, down: down) }
    }

    private func buttonNow(_ button: UInt8, down: Bool) {
        guard active, button < 5, heldButtons[Int(button)] != down, let source else { return }
        heldButtons[Int(button)] = down
        let types: [(CGEventType, CGEventType)] = [(.leftMouseDown, .leftMouseUp), (.rightMouseDown, .rightMouseUp),
                                                   (.otherMouseDown, .otherMouseUp), (.otherMouseDown, .otherMouseUp),
                                                   (.otherMouseDown, .otherMouseUp)]
        let mouseButton: CGMouseButton = button == 0 ? .left : button == 1 ? .right : .center
        guard let event = CGEvent(mouseEventSource: source, mouseType: down ? types[Int(button)].0 : types[Int(button)].1,
                                  mouseCursorPosition: CGPoint(x: x, y: y), mouseButton: mouseButton) else { return }
        if button >= 2 { event.setIntegerValueField(.mouseEventButtonNumber, value: Int64(button)) }
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.post(tap: .cghidEventTap)
    }

    func wheel(dyLines: Float, dxLines: Float) {
        queue.async { self.wheelNow(dyLines: dyLines, dxLines: dxLines) }
    }

    private func wheelNow(dyLines: Float, dxLines: Float) {
        guard active else { return }
        var dy = Double(dyLines)
        var dx = Double(dxLines)
        if reverseWheel { dy = -dy; dx = -dx }
        let pixelsPerLine = 10.0 * (NSScreen.main?.backingScaleFactor ?? 2)
        if smoothWheel {
            localScroll.add(dx: dx * pixelsPerLine, dy: dy * pixelsPerLine)
        } else {
            ScrollPoster.post(dx: dx * pixelsPerLine, dy: dy * pixelsPerLine)
        }
    }

    // MARK: Keys

    func key(page: UInt8, usage: UInt16, down: Bool) {
        queue.async { self.keyNow(page: page, usage: usage, down: down) }
    }

    private func keyNow(page: UInt8, usage: UInt16, down: Bool) {
        guard active, let source else { return }
        if page == 0x07 && usage == KeyMap.capsLock {
            setCapsLock(down, source: source)
            return
        }
        // Modifier keys change the flags everyone else sees, not a keyDown.
        if let modifier = Self.modifierUsages[usage], let keycode = KeyMap.inverse[usage] {
            if down { flags.formUnion([modifier, .maskNonCoalesced]) }
            else { flags.subtract([modifier, .maskNonCoalesced]) }
            postFlagsChanged(keycode: keycode, source: source)
            if down { heldKeys.insert(usage) } else { heldKeys.remove(usage) }
            return
        }
        guard page == 0x07 else {
            mediaKey(usage: usage, down: down, source: source)
            return
        }
        // PrintScreen has no Mac key; F13 is the usual stand-in.
        let keycode = KeyMap.inverse[usage] ?? (usage == 0x46 ? 0x69 : nil)
        guard let keycode else {
            Log.diagnostic("unmapped_usage", String(format: "0x%04X", usage))
            return
        }
        // A release for a key the Mac never saw pressed is dropped, as on Windows; a down for a key
        // already held is the sender's key repeat.
        let isRepeat = down && !heldKeys.insert(usage).inserted
        if !down, heldKeys.remove(usage) == nil {
            return
        }
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: keycode, keyDown: down) else { return }
        event.flags = flags.union([.maskNonCoalesced])
        if isRepeat { event.setIntegerValueField(.keyboardEventAutorepeat, value: 1) }
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.post(tap: .cghidEventTap)
    }

    /// HID modifier usages to the device-independent flag everyone reads.
    private static let modifierUsages: [UInt16: CGEventFlags] = [
        0xE0: .maskControl, 0xE4: .maskControl,
        0xE1: .maskShift, 0xE5: .maskShift,
        0xE2: .maskAlternate, 0xE6: .maskAlternate,
        0xE3: .maskCommand, 0xE7: .maskCommand,
    ]

    private func postFlagsChanged(keycode: CGKeyCode, source: CGEventSource) {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: keycode, keyDown: true) else { return }
        event.type = .flagsChanged
        event.flags = flags.union([.maskNonCoalesced])
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.post(tap: .cghidEventTap)
    }

    /// Caps Lock arrives as the sender's state: switch only when it differs, and the two never end
    /// up opposite.
    private func setCapsLock(_ on: Bool, source: CGEventSource) {
        let current = CGEventSource.flagsState(.combinedSessionState).contains(.maskAlphaShift)
        guard current != on, let event = CGEvent(keyboardEventSource: source, virtualKey: KeyMap.capsLock, keyDown: true) else { return }
        event.type = .flagsChanged
        if on { flags.formUnion(.maskAlphaShift) } else { flags.subtract(.maskAlphaShift) }
        event.flags = flags.union([.maskNonCoalesced])
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.post(tap: .cghidEventTap)
        Log.write("inject_caps", "sender=\(on ? "on" : "off") mac=\(current ? "on" : "off")")
    }

    /// Consumer-page usages. The sound keys have virtual keycodes the system honours; media and
    /// brightness keys would need NX event synthesis or private frameworks, and are dropped for now
    /// (noted in ADAPTING.md).
    private func mediaKey(usage: UInt16, down: Bool, source: CGEventSource) {
        let keycode: CGKeyCode
        switch usage {
        case 0xE9: keycode = 0x48 // sound up
        case 0xEA: keycode = 0x49 // sound down
        case 0xE2: keycode = 0x4A // mute
        default:
            Log.diagnostic("unmapped_media", String(format: "0x%04X", usage))
            return
        }
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: keycode, keyDown: down) else { return }
        event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
        event.post(tap: .cghidEventTap)
    }

    // MARK: Safety

    /// Releases everything still held so nothing sticks on the Mac.
    private func releaseHeld() {
        guard let source else {
            heldKeys = []
            heldButtons = [false, false, false, false, false]
            flags = []
            return
        }
        for usage in heldKeys {
            if let modifier = Self.modifierUsages[usage], let keycode = KeyMap.inverse[usage] {
                flags.subtract([modifier, .maskNonCoalesced])
                postFlagsChanged(keycode: keycode, source: source)
            } else if let keycode = KeyMap.inverse[usage] ?? (usage == 0x46 ? 0x69 : nil) {
                if let event = CGEvent(keyboardEventSource: source, virtualKey: keycode, keyDown: false) {
                    event.flags = flags.union([.maskNonCoalesced])
                    event.setIntegerValueField(.eventSourceUserData, value: ScrollPoster.tag)
                    event.post(tap: .cghidEventTap)
                }
            }
        }
        heldKeys = []
        for (i, isDown) in heldButtons.enumerated() where isDown {
            buttonNow(UInt8(i), down: false)
        }
        heldButtons = [false, false, false, false, false]
        flags = []
    }

    // MARK: Local input while the pointer is away

    private func startLocalWatch() {
        parked = CGEvent(source: nil)?.location
        localWatch?.cancel()
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now(), repeating: 0.2)
        timer.setEventHandler { [weak self] in
            guard let self, let parked = self.parked, let here = CGEvent(source: nil)?.location else { return }
            if abs(here.x - parked.x) > 2 || abs(here.y - parked.y) > 2 {
                self.showCursorAfterLocalMove()
            }
        }
        timer.resume()
        localWatch = timer
    }

    private func showCursorAfterLocalMove() {
        CursorVisibility.show()
        CursorShield.hideLater()
        stopLocalWatch()
        Log.write("cursor_local_move", "")
    }

    private func stopLocalWatch() {
        localWatch?.cancel()
        localWatch = nil
        parked = nil
    }
}

/// CursorShield is MainActor; the injector's queue reaches it through these hops.
extension CursorShield {
    nonisolated static func showLater(on displayID: CGDirectDisplayID) {
        DispatchQueue.main.async { MainActor.assumeIsolated { CursorShield.shared.show(on: displayID) } }
    }

    nonisolated static func hideLater() {
        DispatchQueue.main.async { MainActor.assumeIsolated { CursorShield.shared.hide() } }
    }
}

/// Pure geometry of the receiver, so the millimetre conversions can be tested.
enum InjectGeometry {
    static func entryY(yMm: Double, screen: CGRect, pointsPerMm: Double) -> Double {
        (screen.midY + yMm * pointsPerMm).clamped(to: screen.minY + 1...screen.maxY - 1)
    }

    static func exitYMm(y: Double, screen: CGRect, pointsPerMm: Double) -> Double {
        (y - screen.midY) / pointsPerMm
    }
}

private extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
