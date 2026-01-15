using Avalonia;
using System;
using System.Threading;

namespace WisprClone;

class Program
{
    private const string MutexName = "WisprClone_SingleInstance_Mutex";
    private static Mutex? _mutex;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
    {
        // Check for existing instance using a named mutex
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running
            ShowAlreadyRunningMessage();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    private static void ShowAlreadyRunningMessage()
    {
        if (OperatingSystem.IsWindows())
        {
            // Use Windows MessageBox
            _ = NativeMethods.MessageBox(
                IntPtr.Zero,
                "WisprClone is already running.\n\nCheck the system tray for the existing instance.",
                "WisprClone",
                0x00000040 | 0x00000000); // MB_ICONINFORMATION | MB_OK
        }
        else
        {
            // For other platforms, write to console
            Console.WriteLine("WisprClone is already running. Check the system tray for the existing instance.");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

// Native Windows methods for MessageBox
internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    public static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
