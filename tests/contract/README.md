# Protocol contract fixtures

JSON exactly as each side sends it, checked by both test suites so the Swift and C# encoders stay in step:

- `arrangement-message.json`: Mac → PC `arrangement` (0x03), decoded by `ArrangementInfo.Parse` on Windows.
- `arrangement-edit.json`: PC → Mac `arrangementEdit` (0x04), decoded as `Arrangement` on the Mac and
  compared with what `ArrangementInfo.Edit` produces on Windows.
- `rename-request.json`: PC → Mac `rename` (0x07), made by `ArrangementInfo.Rename` on Windows and
  decoded as `RenameRequest` on the Mac.
- `pc-stats.json`: PC → Mac `stats` (0x22), made by `PcStatsCollector` on Windows and decoded as `PcStats` on the Mac.
- `quality-report.json`: Mac → PC `quality` (0x23), encoded from `QualityReport` on the Mac and parsed by
  `QualityInfo.Parse` on Windows. `tips` is left out when unchanged since the last report.
- `binary-messages.json`: byte-level samples (hex, little-endian payloads) of the fixed-width messages
  `enter`, `move`, `moveRaw`, `button`, `wheel`, `key`, `leave`, `action` and `ping`. Both suites encode
  the same fields with their own writers and must produce these exact bytes, and the decoding side must
  read the fields back. With a Windows sender (input flows PC → Mac) both platforms now encode and decode
  messages they never touched before, so this pins the numeric formats.
