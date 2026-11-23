using System.Reflection;
using System.Runtime.InteropServices;

namespace SpectraPlayer.Core.Playback.Mpv;

/// <summary>Raw libmpv C API (client.h). Only the calls the player needs.</summary>
internal static class MpvNative
{
    private const string Lib = "libmpv-2";

    // mpv_format
    public const int FormatNone = 0, FormatString = 1, FormatFlag = 3, FormatInt64 = 4, FormatDouble = 5;

    // mpv_event_id
    public const int EventShutdown = 1, EventEndFile = 7, EventFileLoaded = 8, EventClientMessage = 16, EventPropertyChange = 22;

    static MpvNative() => NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, Resolve);

    /// <summary>Looks for libmpv next to the exe, then in a "libmpv" subfolder, under any of its usual file names.</summary>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Lib) return IntPtr.Zero;
        string[] folders = [AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "libmpv")];
        string[] names = ["libmpv-2.dll", "mpv-2.dll", "libmpv.dll"];
        foreach (var folder in folders)
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)) return handle;
            }
        return IntPtr.Zero; // fall back to default probing; failure surfaces as DllNotFoundException
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    /// <summary>mpv_event_end_file (first two fields): reason 0 = end of file, 2 = stopped, 3 = quit, 4 = error.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventEndFile
    {
        public int Reason;
        public int Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventClientMessage
    {
        public int NumArgs;
        public IntPtr Args;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    /// <param name="args">Pointer to a NULL-terminated array of UTF-8 C strings.</param>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(IntPtr ctx, ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);

    public static string ErrorText(int code) => Marshal.PtrToStringUTF8(mpv_error_string(code)) ?? "unknown error";

    /// <summary>Runs an mpv command given as separate arguments (no quoting needed).</summary>
    public static int RunCommand(IntPtr ctx, params string[] args)
    {
        var strings = new IntPtr[args.Length + 1]; // last slot stays NULL: the array terminator
        try
        {
            for (var i = 0; i < args.Length; i++) strings[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            var pin = GCHandle.Alloc(strings, GCHandleType.Pinned);
            try { return mpv_command(ctx, pin.AddrOfPinnedObject()); }
            finally { pin.Free(); }
        }
        finally
        {
            foreach (var s in strings) if (s != IntPtr.Zero) Marshal.FreeCoTaskMem(s);
        }
    }
}
