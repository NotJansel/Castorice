using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace Castorice.Desktop.Services;

/// <summary>
/// Sets the Dock icon on macOS. The Dock ignores <c>Window.Icon</c> and, for an app started
/// outside an <c>.app</c> bundle (e.g. <c>dotnet run</c>), shows the generic executable icon. The
/// only way in from there is <c>NSApplication.applicationIconImage</c>, reached through the
/// Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacDockIcon
{
    private const string ObjCRuntime = "/usr/lib/libobjc.A.dylib";

    private static readonly Uri DockImage = new("avares://Castorice/Assets/castorice-dock.png");

    /// <summary>Best effort: a missing image or a runtime surprise leaves the default icon.</summary>
    public static void Apply()
    {
        try
        {
            using var stream = AssetLoader.Open(DockImage);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            SetApplicationIcon(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Trace.WriteLine($"Could not set the Dock icon: {ex.Message}");
        }
    }

    private static void SetApplicationIcon(byte[] png)
    {
        var bytes = Marshal.AllocHGlobal(png.Length);
        try
        {
            Marshal.Copy(png, 0, bytes, png.Length);

            // NSData copies the bytes, so the buffer can be freed straight after.
            var data = SendBytes(Class("NSData"), Selector("dataWithBytes:length:"), bytes, (nuint)png.Length);
            if (data == IntPtr.Zero)
            {
                return;
            }

            var image = Send(Send(Class("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
            if (image == IntPtr.Zero)
            {
                return;
            }

            var app = Send(Class("NSApplication"), Selector("sharedApplication"));
            Send(app, Selector("setApplicationIconImage:"), image);
        }
        finally
        {
            Marshal.FreeHGlobal(bytes);
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [DllImport(ObjCRuntime)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjCRuntime)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // objc_msgSend is bound once per call shape: on arm64 it must be called with the exact
    // signature of the method it dispatches to, never as a variadic function.
    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);
}
