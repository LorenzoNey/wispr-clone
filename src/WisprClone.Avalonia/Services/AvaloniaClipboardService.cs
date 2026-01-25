using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using WisprClone.Services.Interfaces;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace WisprClone.Services;

/// <summary>
/// Cross-platform clipboard service using Avalonia's clipboard API.
/// Falls back to native Windows API when Avalonia clipboard is unavailable.
/// </summary>
public class AvaloniaClipboardService : IClipboardService
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
#endif

    private IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Try MainWindow first
            if (desktop.MainWindow?.Clipboard != null)
            {
                return desktop.MainWindow.Clipboard;
            }

            // Fallback: try to get clipboard from any open window
            foreach (var window in desktop.Windows)
            {
                if (window.Clipboard != null)
                {
                    return window.Clipboard;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task SetTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            // Retry logic for clipboard access
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await clipboard.SetTextAsync(text);
                    return;
                }
                catch (Exception)
                {
                    if (i < 4)
                        await Task.Delay(50);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> GetTextAsync()
    {
        // First try Avalonia clipboard
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            try
            {
                var text = await clipboard.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
            catch
            {
                // Fall through to native API
            }
        }

#if WINDOWS
        // Fallback to native Windows clipboard API
        return await Task.Run(() => GetClipboardTextNative());
#else
        return string.Empty;
#endif
    }

#if WINDOWS
    private string GetClipboardTextNative()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return string.Empty;

            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                    return string.Empty;

                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return string.Empty;

                try
                {
                    return Marshal.PtrToStringUni(pointer) ?? string.Empty;
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return string.Empty;
        }
    }
#endif
}
