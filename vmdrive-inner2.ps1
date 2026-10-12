[Console]::OutputEncoding=[Text.Encoding]::UTF8
$log = "C:\Windows\Temp\drive-result.txt"
"start $(Get-Date -Format HH:mm:ss.fff)" | Out-File $log
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Drive {
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static void Move(int dx, int dy) {
        var inp = new INPUT[1];
        inp[0].type = 0; inp[0].mi.dx = dx; inp[0].mi.dy = dy; inp[0].mi.dwFlags = 0x0001;
        SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@
$p = New-Object Drive+POINT
[Drive]::GetCursorPos([ref]$p)
Add-Content $log "cursor-before $($p.X),$($p.Y)"
[Drive]::SetCursorPos(512, 384)
Start-Sleep -Milliseconds 200
# glide to the right edge, then push against it
for ($i = 0; $i -lt 4; $i++) { [Drive]::Move(500, 0); Start-Sleep -Milliseconds 12 }
for ($i = 0; $i -lt 40; $i++) { [Drive]::Move(30, 0); Start-Sleep -Milliseconds 8 }
Add-Content $log "cross-push done"
Start-Sleep -Milliseconds 1500
[Drive]::GetCursorPos([ref]$p)
Add-Content $log "cursor-after-cross $($p.X),$($p.Y)"
for ($i = 0; $i -lt 20; $i++) { [Drive]::Move(-15, 0); Start-Sleep -Milliseconds 10 }
Add-Content $log "inward-glide done"
Start-Sleep -Milliseconds 800
[Drive]::GetCursorPos([ref]$p)
Add-Content $log "cursor-after-glide $($p.X),$($p.Y)"
for ($i = 0; $i -lt 25; $i++) { [Drive]::Move(40, 0); Start-Sleep -Milliseconds 8 }
Add-Content $log "return-push done"
Start-Sleep -Milliseconds 800
[Drive]::GetCursorPos([ref]$p)
Add-Content $log "cursor-end $($p.X),$($p.Y)"
Add-Content $log "done"
