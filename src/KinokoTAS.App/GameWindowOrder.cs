using System.Runtime.InteropServices;
namespace KinokoTAS.App;
// Avalonia has no cross-process owner API. This narrow Windows adapter sets an
// owned game's owner, not a global topmost flag, and never reparents rendering.
internal static class GameWindowOrder {
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern nint SetOwner64(nint window,int index,nint owner);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] static extern int SetOwner32(nint window,int index,int owner);
    public static bool Attach(nint game,nint editor) {
        if(!OperatingSystem.IsWindows() || game==0 || editor==0)return false;
        Marshal.SetLastPInvokeError(0);
        var previous=IntPtr.Size==8?SetOwner64(game,-8,editor):(nint)SetOwner32(game,-8,(int)editor);
        return previous!=0 || Marshal.GetLastPInvokeError()==0;
    }
}
