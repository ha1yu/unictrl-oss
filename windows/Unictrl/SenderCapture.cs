using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Unictrl;

/// Captures this PC's input with low-level hooks and hands it to the Sender, the mirror of the Mac's
/// InputCapture. While the pointer is logically on the Mac the hooks swallow the input (Windows must
/// not also act on it), the cursor is clipped to the local screen and hidden; pushing the cursor
/// against the outer edge on the Mac's side crosses, and the Mac's `leave` (or a timeout, a
/// disconnect, or quitting Unictrl) brings it back. The hook thread runs its own message pump and
/// does nothing but seal-and-send: anything slower and Windows silently unhooks us.
internal sealed class SenderCapture : IDisposable
{
    private const uint WmQuit = 0x0012, WmInput = 0x00FF, WmDisplayChange = 0x007E;
    private const uint WmMouseMove = 0x0200, WmLDown = 0x0201, WmLUp = 0x0202, WmRDown = 0x0204, WmRUp = 0x0205,
        WmMDown = 0x0207, WmMUp = 0x0208, WmWheel = 0x020A, WmXDown = 0x020B, WmXUp = 0x020C, WmHWheel = 0x020E;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint RidevInputSink = 0x100, RidInput = 0x10000003, RimTypeMouse = 0;

    private readonly Settings settings;
    private readonly Sender sender;
    private readonly SenderCore.EdgePush push = new();
    // Rooted for as long as the hooks live; a collected delegate is the classic silent-unhook bug.
    private Native.HookProc? mouseProc, keyboardProc;
    private Native.WndProc? windowProc;
    private nint hwnd, mouseHook, keyboardHook;
    private volatile bool remote;
    /// UNICTRL_TESTDRIVE=1 lets software-injected input drive the capture path (it is normally
    /// ignored), so an automated test can play the mouse without hardware. Not for normal use.
    private static readonly bool testDrive = Environment.GetEnvironmentVariable("UNICTRL_TESTDRIVE") == "1";
    private bool panicCtrl, panicShift;
    private DateTime suppressUntil = DateTime.MinValue;
    private Native.Point lastPt;
    /// The monitor the (clipped) cursor sits on, cached on entering the remote state so the hook
    /// does no per-event display queries.
    private MonitorGeometry remoteMonitor;
    /// Raw counts since the last hook event: the edge push and `moveRaw` read them. Raw input is not
    /// clamped to the screen, so it still reports the push after the cursor hits the edge.
    private int rawDx, rawDy;

    public bool Remote => remote;

    public SenderCapture(Settings settings, Sender sender)
    {
        this.settings = settings;
        this.sender = sender;
    }

    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Unictrl sender capture", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private void Run()
    {
        mouseProc = MouseHook;
        keyboardProc = KeyboardHook;
        windowProc = WindowProc;
        var wc = new Native.WndClass { WndProc = Marshal.GetFunctionPointerForDelegate(windowProc), ClassName = "UnictrlSender" };
        if (Native.RegisterClassW(ref wc) == 0)
        {
            Log.Write("capture_failed", "RegisterClassW");
            return;
        }
        hwnd = Native.CreateWindowExW(0, "UnictrlSender", "", 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, 0, 0);
        if (hwnd == 0)
        {
            Log.Write("capture_failed", "CreateWindowExW");
            return;
        }
        var devices = new[]
        {
            new Native.RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidevInputSink, Target = hwnd }, // mouse
            new Native.RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevInputSink, Target = hwnd }, // keyboard
        };
        if (!Native.RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Native.RawInputDevice>()))
        {
            Log.Write("capture_failed", "RegisterRawInputDevices");
            return;
        }
        var module = Native.GetModuleHandleW(null);
        mouseHook = Native.SetWindowsHookExW(Native.WhMouseLl, mouseProc, module, 0);
        keyboardHook = Native.SetWindowsHookExW(Native.WhKeyboardLl, keyboardProc, module, 0);
        if (mouseHook == 0 || keyboardHook == 0)
        {
            Log.Write("capture_failed", $"mouse={mouseHook != 0} keyboard={keyboardHook != 0}");
            return;
        }
        Log.Write("capture_ready", $"testdrive={testDrive}");
        try
        {
            while (Native.GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
        }
        finally
        {
            if (remote) ReturnFromMac(0, "shutdown");
            if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
            if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook);
        }
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            uint size = 0;
            var headerSize = (uint)Marshal.SizeOf<Native.RawInputHeader>();
            Native.GetRawInputData(lParam, RidInput, 0, ref size, headerSize);
            if (size is > 0 and <= 256)
            {
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (Native.GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) == size)
                    {
                        var header = Marshal.PtrToStructure<Native.RawInputHeader>(buffer);
                        // Injected input arrives with no device handle; only real devices count.
                        if (header.Type == RimTypeMouse && (header.Device != 0 || testDrive))
                        {
                            var mouse = Marshal.PtrToStructure<Native.RawMouse>(buffer + (int)headerSize);
                            if ((mouse.Flags & 1) == 0) // relative only
                            {
                                rawDx += mouse.LastX;
                                rawDy += mouse.LastY;
                            }
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        else if (message == WmDisplayChange)
        {
            push.Reset(); // the screens and their edges just changed
            if (remote && Native.GetCursorPos(out var at)) remoteMonitor = Displays.At(at.X, at.Y);
        }
        return Native.DefWindowProcW(window, message, wParam, lParam);
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code < 0) return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<Native.MsLlHookStruct>(lParam);
        // Input other software synthesised is not ours to take.
        if ((data.Flags & Native.LlInjected) != 0 && !testDrive) return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        var message = (uint)wParam;
        if (remote)
        {
            ForwardMouse(message, data);
            return 1; // Windows must not also act on it
        }
        if (message == WmMouseMove) WatchEdge(data);
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !remote) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        var key = Marshal.PtrToStructure<Native.KbdLlHookStruct>(lParam);
        if ((key.Flags & Native.LlInjected) != 0 && !testDrive) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        var message = (uint)wParam;
        var down = message == WmKeyDown || message == WmSysKeyDown;
        // Escape hatch: the pointer is trapped on the receiver (it cannot leave, Unictrl is stuck),
        // so a chord the hook always answers brings it home. Everything real is still swallowed.
        if (KeyTable.TryGetUsage((ushort)key.ScanCode, (key.Flags & Native.LlExtended) != 0, key.VirtualKey,
                out var panicPage, out var panicUsage))
        {
            if (panicPage == 0x07 && panicUsage is 0xE0 or 0xE4) panicCtrl = down;
            else if (panicPage == 0x07 && panicUsage is 0xE1 or 0xE5) panicShift = down;
            else if (panicPage == 0x07 && panicUsage == 0x14 && down && panicCtrl && panicShift)
            {
                ForceReturn("panic");
                return 1;
            }
        }
        if (KeyTable.TryGetUsage((ushort)key.ScanCode, (key.Flags & Native.LlExtended) != 0, key.VirtualKey,
                out var page, out var usage))
        {
            sender.Send(Messages.Key(page, usage, down));
        }
        // Everything real is swallowed while the pointer is away, mapped or not (fn, vendor keys),
        // so Windows never also types what the Mac is typing.
        return 1;
    }

    private DateTime lastEdgeLog = DateTime.MinValue;

    /// Local mode: only the outward push against the open outer edge on the Mac's side is watched.
    private void WatchEdge(in Native.MsLlHookStruct data)
    {
        lastPt = data.Pt;
        if (!sender.Online || !settings.Enabled || DateTime.UtcNow < suppressUntil)
        {
            push.Reset();
            return;
        }
        if (DateTime.UtcNow - lastEdgeLog > TimeSpan.FromMilliseconds(500))
        {
            lastEdgeLog = DateTime.UtcNow;
            Log.Diagnostic("edge_watch", $"at={data.Pt.X},{data.Pt.Y} raw={rawDx},{rawDy} online={sender.Online} side={settings.MacSide}");
        }
        var macOnRight = settings.MacSide != "left";
        var screen = ScreenAt(data.Pt);
        if (!SenderCore.AtOuterEdge(new(data.Pt.X, data.Pt.Y), screen, macOnRight, Displays.Exists))
        {
            push.Reset();
            rawDx = rawDy = 0; // counts mean nothing once the hand moved elsewhere
            return;
        }
        var outward = macOnRight ? rawDx : -rawDx;
        rawDx = rawDy = 0;
        if (push.Add(outward)) EnterRemote(data, screen);
        else Log.Diagnostic("edge_push", $"outward={outward:F0}");
    }

    private void EnterRemote(in Native.MsLlHookStruct data, SenderCore.Screen screen)
    {
        remote = true;
        panicCtrl = panicShift = false;
        var clip = new Native.Rect32 { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom };
        Native.ClipCursor(ref clip); // the cursor stays on this screen while it is logically away
        CursorHider.Hide();
        remoteMonitor = Displays.At(data.Pt.X, data.Pt.Y);
        lastPt = data.Pt;
        rawDx = rawDy = 0;
        var entry = SenderCore.Cross(screen, new(data.Pt.X, data.Pt.Y), settings.MacSide != "left", settings.MacOffsetMm);
        sender.SendEnter(entry);
        Log.Write("cross", $"{(settings.MacSide != "left" ? "right" : "left")}_edge y_mm={entry.YMm:F1}");
    }

    private void ForwardMouse(uint message, in Native.MsLlHookStruct data)
    {
        switch (message)
        {
            case WmMouseMove:
                var scale = settings.MacPointerScale;
                // Raw counts are the reliable motion source: raw input keeps flowing while the local
                // cursor is frozen, and the counts are unclamped at the edges (hook cursor deltas
                // are neither). A Mac applies no ballistics to injected input, so the scale is the
                // speed control.
                if (rawDx != 0 || rawDy != 0)
                {
                    sender.Send(Messages.MoveRaw((float)(rawDx * scale), (float)(rawDy * scale), SentUs()));
                    rawDx = rawDy = 0;
                }
                lastPt = data.Pt;
                return;
            case WmLDown: sender.Send(Messages.Button(0, true)); return;
            case WmLUp: sender.Send(Messages.Button(0, false)); return;
            case WmRDown: sender.Send(Messages.Button(1, true)); return;
            case WmRUp: sender.Send(Messages.Button(1, false)); return;
            case WmMDown: sender.Send(Messages.Button(2, true)); return;
            case WmMUp: sender.Send(Messages.Button(2, false)); return;
            case WmXDown: sender.Send(Messages.Button(XButton(data), true)); return;
            case WmXUp: sender.Send(Messages.Button(XButton(data), false)); return;
            case WmWheel:
            {
                var (dy, _) = SenderCore.Wheel((short)(data.MouseData >> 16), 0);
                sender.Send(Messages.Wheel(dy, 0));
                return;
            }
            case WmHWheel:
            {
                var (_, dx) = SenderCore.Wheel(0, (short)(data.MouseData >> 16));
                sender.Send(Messages.Wheel(0, dx));
                return;
            }
        }
    }

    private static byte XButton(in Native.MsLlHookStruct data) => (data.MouseData >> 16) == 1 ? (byte)3 : (byte)4;

    /// The pointer came back: the Mac sent `leave` (or the link dropped, timed out or Unictrl quits).
    /// Called from the connection thread; the Win32 calls have no thread affinity.
    public void ReturnFromMac(float yMm, string reason)
    {
        if (!remote) return;
        remote = false;
        panicCtrl = panicShift = false;
        Native.ClipCursorReset();
        var monitor = Native.GetCursorPos(out var at) ? Displays.At(at.X, at.Y) : Displays.Primary();
        var spot = SenderCore.ReturnSpot(ToScreen(monitor), settings.MacSide != "left", yMm, settings.MacOffsetMm);
        Native.SetCursorPos(spot.X, spot.Y);
        CursorHider.Show(reason);
        suppressUntil = DateTime.UtcNow.AddMilliseconds(250); // let the hand finish the gesture
        push.Reset();
        Log.Write("return", $"reason={reason} y_mm={yMm:F1}");
    }

    /// Brings the pointer home without a `leave` (pong timeout, disabled, shutting down).
    public void ForceReturn(string reason) => ReturnFromMac(0, reason);

    private static SenderCore.Screen ToScreen(MonitorGeometry monitor) =>
        new(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Right, monitor.Bounds.Bottom, monitor.PixelsPerMm);

    private static SenderCore.Screen ScreenAt(Native.Point at) => ToScreen(Displays.At(at.X, at.Y));

    private static uint SentUs() =>
        (uint)(Stopwatch.GetTimestamp() * 1_000_000 / Stopwatch.Frequency);

    public void Dispose()
    {
        if (hwnd != 0) Native.PostMessageW(hwnd, WmQuit, 0, 0); // ends the pump; Run unhooks and returns the cursor
    }
}
