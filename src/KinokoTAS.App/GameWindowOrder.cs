using System.Runtime.InteropServices;
namespace KinokoTAS.App;
// Avalonia has no cross-process owner API. This narrow Windows adapter sets an
// owned game's owner, not a global topmost flag, and never reparents rendering.
internal static class GameWindowOrder {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint process);
    public static void GrantActivation(int process){if(OperatingSystem.IsWindows() && process>0)AllowSetForegroundWindow((uint)process);}
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint window,int command);
    [DllImport("user32.dll")] static extern nint SetFocus(nint window);
    public static bool Activate(nint window) {
        if(!OperatingSystem.IsWindows() || window==0)return false;
        if(IsIconic(window))ShowWindow(window,9);
        if(SetForegroundWindow(window) && GetForegroundWindow()==window)return true;
        uint current=GetCurrentThreadId();
        uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _);
        uint target=GetWindowThreadProcessId(window,out _);
        bool joinedForeground=false,joinedTarget=false;
        try {
            if(foreground!=0 && foreground!=current)joinedForeground=AttachThreadInput(current,foreground,true);
            if(target!=0 && target!=current && target!=foreground)joinedTarget=AttachThreadInput(current,target,true);
            SetForegroundWindow(window);SetFocus(window);
            return GetForegroundWindow()==window;
        }finally {
            if(joinedTarget)AttachThreadInput(current,target,false);
            if(joinedForeground)AttachThreadInput(current,foreground,false);
        }
    }
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern nint SetOwner64(nint window,int index,nint owner);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] static extern int SetOwner32(nint window,int index,int owner);
    public static bool Attach(nint game,nint editor) {
        if(!OperatingSystem.IsWindows() || game==0 || editor==0)return false;
        Marshal.SetLastPInvokeError(0);
        var previous=IntPtr.Size==8?SetOwner64(game,-8,editor):(nint)SetOwner32(game,-8,(int)editor);
        return previous!=0 || Marshal.GetLastPInvokeError()==0;
    }
}
