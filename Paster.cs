using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Blitztext;

/// <summary>
/// Zwischenablage (Win32, ohne WinForms) + simuliertes Strg+V am Cursor. Läuft auf dem UI-/STA-Thread.
/// </summary>
public static class Paster
{
    // ── Zwischenablage über Win32 ───────────────────────────────────────────────
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static async Task<bool> TrySetClipboardAsync(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (SetClipboardText(text)) return true;
            await Task.Delay(30); // Zwischenablage evtl. kurz von anderem Prozess gesperrt
        }
        return false;
    }

    /// <summary>
    /// Wartet, bis kein anderer Prozess die Zwischenablage offen hält (z.B. der Win+V-Verlauf, der jede
    /// neue Ablage kurz einliest). Sonst findet das Zielprogramm beim Strg+V eine gesperrte Ablage und
    /// fügt still nichts ein. false = nach <paramref name="max"/> immer noch belegt.
    /// </summary>
    public static async Task<bool> WaitForClipboardFreeAsync(TimeSpan max)
    {
        var deadline = DateTime.UtcNow + max;
        while (true)
        {
            if (OpenClipboard(IntPtr.Zero)) { CloseClipboard(); return true; }
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(10);
        }
    }

    public static bool ClipboardContains(string text) => GetClipboardText() == text;

    public static async Task ClearIfUnchangedAsync(string text, TimeSpan delay)
    {
        if (string.IsNullOrEmpty(text)) return; // nie wegen "" == leerer/fremder Ablage löschen
        try
        {
            await Task.Delay(delay).ConfigureAwait(true);
            if (GetClipboardText() == text)
                EmptyClipboardSafe();
        }
        catch
        {
            // Clipboard-Aufräumen ist Datenschutz-Politur, kein Grund die App zu stören.
        }
    }

    private static bool SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            int bytes = (text.Length + 1) * 2; // UTF-16 + Null-Terminator
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return false;

            IntPtr target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero) { GlobalFree(hGlobal); return false; }
            try { Marshal.Copy((text + '\0').ToCharArray(), 0, target, text.Length + 1); }
            finally { GlobalUnlock(hGlobal); }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal); // bei Misserfolg behalten WIR den Besitz
                return false;
            }
            return true; // bei Erfolg übernimmt das System hGlobal
        }
        finally { CloseClipboard(); }
    }

    private static string GetClipboardText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return "";
        if (!OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return "";
            IntPtr ptr = GlobalLock(h);
            if (ptr == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(ptr) ?? ""; }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    private static void EmptyClipboardSafe()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    // ── Zielfenster merken / reaktivieren ───────────────────────────────────────
    public static IntPtr GetActiveWindow() => GetForegroundWindow();

    public static bool RefocusIfNeeded(IntPtr target)
    {
        if (target == IntPtr.Zero) return false;
        if (GetForegroundWindow() == target) return true;
        SetForegroundWindow(target);
        return GetForegroundWindow() == target;
    }

    // ── Diagnose: wohin geht die Tastatureingabe, welche Sondertasten sind gedrückt? ──
    public static string DescribeFocus()
    {
        var fg = GetForegroundWindow();
        uint tid = GetWindowThreadProcessId(fg, out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(tid, ref info)) return "unbekannt";
        var cls = new System.Text.StringBuilder(64);
        GetClassName(info.hwndFocus, cls, cls.Capacity);
        return $"{info.hwndFocus}[{cls}]";
    }

    public static string HeldModifiers()
    {
        var held = new System.Collections.Generic.List<string>();
        if (GetAsyncKeyState(0x10) < 0) held.Add("Shift");
        if (GetAsyncKeyState(0x11) < 0) held.Add("Strg");
        if (GetAsyncKeyState(0x12) < 0) held.Add("Alt");
        if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) held.Add("Win");
        return held.Count == 0 ? "keine" : string.Join("+", held);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    // ── Strg+V simulieren --- Rückgabe = Zahl der eingespeisten Ereignisse (0 = blockiert) ──
    public static uint SendCtrlV()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;
        var inputs = new[] { KeyDown(VK_CONTROL), KeyDown(VK_V), KeyUp(VK_V), KeyUp(VK_CONTROL) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyDown(ushort vk) => MakeInput(vk, 0);
    private static INPUT KeyUp(ushort vk) => MakeInput(vk, KEYEVENTF_KEYUP);

    private static INPUT MakeInput(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } }
    };

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── Win32-Zwischenablage ────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
