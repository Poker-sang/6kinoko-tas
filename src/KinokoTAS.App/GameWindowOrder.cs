using System;
using System.Runtime.InteropServices;

namespace KinokoTAS.App;

// Avalonia has no cross-process owner API. This narrow Windows adapter sets an
// owned game's owner, not a global topmost flag, and never reparents rendering.
internal static partial class GameWindowOrder
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint process);

    public static void GrantActivation(int process)
    {
        if (OperatingSystem.IsWindows() && process > 0)
            AllowSetForegroundWindow((uint) process);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint from, uint to, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint window);

    public static bool Activate(nint window)
    {
        if (!OperatingSystem.IsWindows() || window == 0)
            return false;

        if (IsIconic(window))
            ShowWindow(window, 9);

        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
            return true;

        var current = GetCurrentThreadId();
        var foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var target = GetWindowThreadProcessId(window, out _);
        bool joinedForeground = false, joinedTarget = false;
        try
        {
            if (foreground != 0 && foreground != current)
                joinedForeground = AttachThreadInput(current, foreground, true);

            if (target != 0 && target != current && target != foreground)
                joinedTarget = AttachThreadInput(current, target, true);

            SetForegroundWindow(window);
            SetFocus(window);
            return GetForegroundWindow() == window;
        }
        finally
        {
            if (joinedTarget)
                AttachThreadInput(current, target, false);

            if (joinedForeground)
                AttachThreadInput(current, foreground, false);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetOwner64(nint window, int index, nint owner);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetOwner32(nint window, int index, int owner);

    public static bool Attach(nint game, nint editor)
    {
        if (!OperatingSystem.IsWindows() || game == 0 || editor == 0)
            return false;

        Marshal.SetLastPInvokeError(0);
        var previous = nint.Size == 8 ? SetOwner64(game, -8, editor) : SetOwner32(game, -8, (int) editor);
        return previous != 0 || Marshal.GetLastPInvokeError() == 0;
    }
}
