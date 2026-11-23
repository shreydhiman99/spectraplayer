using System.Runtime.InteropServices;
using System.Windows.Interop;
using static SpectraPlayer.App.NativeMethods;

namespace SpectraPlayer.App.Controls;

/// <summary>
/// A plain native child window inside the WPF layout. libmpv is told to render into its HWND, so video is drawn
/// by the GPU straight into this rectangle (WPF content can't be layered on top of it: keep controls beside it).
/// </summary>
public sealed class VideoHost : HwndHost
{
    private IntPtr _hwnd;

    /// <summary>Raised once the native window exists and can be handed to the player engine.</summary>
    public event EventHandler? HandleCreated;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "static", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create the video window (Win32 error {Marshal.GetLastWin32Error()}).");

        HandleCreated?.Invoke(this, EventArgs.Empty);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }
}
