using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using TypingMimic.Core;

namespace TypingMimic.App.Services;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT
    { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
    { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
}

public sealed class KeyboardInputService : IKeyboardInputService
{
    public void SendText(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (char c in text)
        {
            if (c == '\n') Add(inputs, 0x0D, 0, 0);
            else if (c == '\t') Add(inputs, 0x09, 0, 0);
            else Add(inputs, 0, c, 0x0004);
        }
        Send(inputs.ToArray());
    }

    public void SendNewLine(NewLineShortcut shortcut = NewLineShortcut.Enter)
    {
        var inputs = new List<Native.INPUT>();
        ushort[] modifiers = shortcut switch
        {
            NewLineShortcut.ShiftEnter => [0x10],
            NewLineShortcut.CtrlEnter => [0x11],
            NewLineShortcut.AltEnter => [0x12],
            _ => []
        };
        foreach (var mod in modifiers) KeyDown(inputs, mod);
        Add(inputs, 0x0D, 0, 0);
        foreach (var mod in modifiers.Reverse()) KeyUp(inputs, mod);
        Send(inputs.ToArray());
    }

    public void Backspace()
    {
        var inputs = new List<Native.INPUT>();
        Add(inputs, 0x08, 0, 0);
        Send(inputs.ToArray());
    }

    private static void KeyDown(List<Native.INPUT> inputs, ushort key)
    {
        inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Scan = 0, Flags = 0 } } });
    }

    private static void KeyUp(List<Native.INPUT> inputs, ushort key)
    {
        inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Scan = 0, Flags = 0x0002 } } });
    }

    private static void Add(List<Native.INPUT> inputs, ushort key, ushort scan, uint flags)
    {
        inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Scan = scan, Flags = flags } } });
        inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Scan = scan, Flags = flags | 0x0002 } } });
    }

    private static void Send(Native.INPUT[] inputs)
    {
        if (Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>()) != inputs.Length)
            throw new InvalidOperationException("Windows did not accept all keyboard input. The target may be elevated or reject simulated input. Check the target text before restarting.");
    }
}

public sealed class ForegroundWindowService : IForegroundWindowService
{
    public nint GetForegroundWindow() => Native.GetForegroundWindow();
    public bool IsOwnWindow(nint window)
    {
        Native.GetWindowThreadProcessId(window, out uint process);
        return process == Environment.ProcessId;
    }
    public bool AreModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }
        .Any(key => (Native.GetAsyncKeyState(key) & 0x8000) != 0);
}

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Action pause, stop;
    private bool disposed;
    private ShortcutGesture? pauseGesture, stopGesture;
    public bool IsReady { get; private set; }
    public GlobalHotkeyService(nint handle, Action pause, Action stop)
    {
        this.pause = pause; this.stop = stop;
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window is unavailable.");
        source.AddHook(WndProc);
    }

    public void Configure(ShortcutGesture nextPause, ShortcutGesture nextStop)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (nextPause == nextStop) throw new ArgumentException("Pause and Stop must use different shortcuts.");
        if (IsReady && nextPause == pauseGesture && nextStop == stopGesture) return;
        var oldPause = pauseGesture;
        var oldStop = stopGesture;
        Unregister();
        try
        {
            RegisterPair(nextPause, nextStop);
        }
        catch (Win32Exception ex)
        {
            Unregister();
            if (oldPause is not null && oldStop is not null)
            {
                try { RegisterPair(oldPause, oldStop); }
                catch (Win32Exception) { Unregister(); }
            }
            throw new InvalidOperationException(ex.Message + (IsReady
                ? " Your previous shortcuts are still active."
                : " Typing is disabled until two available shortcuts are applied."), ex);
        }
    }

    private void RegisterPair(ShortcutGesture nextPause, ShortcutGesture nextStop)
    {
        if (!Native.RegisterHotKey(source.Handle, 1, nextPause.Modifiers | 0x4000, nextPause.Key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not register {nextPause}. It may be reserved or already in use.");
        if (!Native.RegisterHotKey(source.Handle, 2, nextStop.Modifiers | 0x4000, nextStop.Key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not register {nextStop}. It may be reserved or already in use.");
        pauseGesture = nextPause; stopGesture = nextStop; IsReady = true;
    }

    private void Unregister()
    {
        Native.UnregisterHotKey(source.Handle, 1);
        Native.UnregisterHotKey(source.Handle, 2);
        pauseGesture = null; stopGesture = null; IsReady = false;
    }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            if (IsReady && wParam == 1) { pause(); handled = true; }
            if (IsReady && wParam == 2) { stop(); handled = true; }
        }
        return 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Unregister();
        source.RemoveHook(WndProc);
    }
}
