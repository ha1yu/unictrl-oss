import Foundation
import Testing
@testable import Unictrl

/// Byte samples from tests/contract/binary-messages.json, shared with the Windows tests. Both
/// platforms encode these fields and must produce the same bytes; the Mac receives exactly these
/// messages from a Windows sender, so the reader side is the one that matters here.
@Suite struct BinaryContractTests {
    private struct Sample: Codable { let name: String; let hex: String }

    private static let samples: [String: Data] = {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("tests/contract/binary-messages.json")
        let list = (try? JSONDecoder().decode([Sample].self, from: Data(contentsOf: url))) ?? []
        return Dictionary(uniqueKeysWithValues: list.map { ($0.name, Data(hexString: $0.hex)) })
    }()

    private static func sample(_ name: String) -> Data { samples[name] ?? Data() }
    @Test func writerProducesTheFixtureBytes() {
        var enter = MessageWriter(.enter)
        enter.f32(12.5); enter.u8(0); enter.u8(0b100)
        #expect(enter.data == Self.sample("enter-left-openright"))

        var resume = MessageWriter(.enter)
        resume.f32(-3.25); resume.u8(1); resume.u8(0b111)
        #expect(resume.data == Self.sample("enter-right-resume-all-open"))

        var move = MessageWriter(.move)
        move.f32(1.5); move.f32(-2.25); move.u32(0x00ABCDEF)
        #expect(move.data == Self.sample("move"))

        var raw = MessageWriter(.moveRaw)
        raw.f32(3); raw.f32(-1); raw.u32(0xFFFFFFFF)
        #expect(raw.data == Self.sample("move-raw"))

        var button = MessageWriter(.button)
        button.u8(3); button.u8(1)
        #expect(button.data == Self.sample("button-back-down"))

        var wheel = MessageWriter(.wheel)
        wheel.f32(1); wheel.f32(-0.5)
        #expect(wheel.data == Self.sample("wheel"))

        var key = MessageWriter(.key)
        key.u8(0x07); key.u16(0x0004); key.u8(1)
        #expect(key.data == Self.sample("key-a-down"))

        var leave = MessageWriter(.leave)
        leave.f32(100); leave.u8(1)
        #expect(leave.data == Self.sample("leave-right"))

        var action = MessageWriter(.action)
        action.u8(2)
        #expect(action.data == Self.sample("action-start"))

        var ping = MessageWriter(.ping)
        ping.u64(0x0123456789ABCDEF)
        #expect(ping.data == Self.sample("ping"))
    }

    @Test func readerParsesTheFixtureBytes() {
        var enter = MessageReader(Self.sample("enter-right-resume-all-open"))
        #expect(enter.type == .enter)
        #expect(enter.f32() == -3.25)
        #expect(enter.u8() == 1)
        #expect(enter.u8() == 0b111)
        #expect(enter.u8() == nil)

        var move = MessageReader(Self.sample("move"))
        #expect(move.type == .move)
        #expect(move.f32() == 1.5)
        #expect(move.f32() == -2.25)
        #expect(move.u32() == 0x00ABCDEF)
        #expect(move.u32() == nil) // past the end

        var raw = MessageReader(Self.sample("move-raw"))
        #expect(raw.type == .moveRaw)
        #expect(raw.f32() == 3)
        #expect(raw.f32() == -1)
        #expect(raw.u32() == 0xFFFFFFFF)

        var key = MessageReader(Self.sample("key-volup-up"))
        #expect(key.type == .key)
        #expect(key.u8() == 0x0C)
        #expect(key.u16() == 0x00E9)
        #expect(key.u8() == 0)

        var short = MessageReader(Self.sample("action-start"))
        #expect(short.u16() == nil) // only one byte follows; short reads return nil, not garbage

        var leave = MessageReader(Self.sample("leave-right"))
        #expect(leave.type == .leave)
        #expect(leave.f32() == 100)
        #expect(leave.u8() == 1)
    }
}

private extension Data {
    init(hexString: String) {
        var data = Data(capacity: hexString.count / 2)
        var index = hexString.startIndex
        while index < hexString.endIndex {
            let next = hexString.index(after: index)
            data.append(UInt8(hexString[index...next], radix: 16) ?? 0)
            index = hexString.index(index, offsetBy: 2)
        }
        self = data
    }
}
