using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace SinuxBoard;

/// <summary>
/// Watches the system clipboard using the native
/// AddClipboardFormatListener/WM_CLIPBOARDUPDATE mechanism, hosted by a
/// hidden, message-only Win32 window. No timers or polling are used.
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int HWND_MESSAGE = -3;
    private const int MaxReadAttempts = 3;
    private const int ReadRetryDelayMs = 40;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private HwndSource? _hwndSource;
    private bool _listenerRegistered;
    private volatile bool _suppressNext;
    private bool _disposed;

    /// <summary>
    /// Raised on the UI thread whenever the clipboard changes and
    /// contains readable text that was not the result of the
    /// application's own <see cref="SuppressNextNotification"/> write.
    /// </summary>
    public event EventHandler<string>? ClipboardTextChanged;

    /// <summary>
    /// Creates the hidden message-only window and registers the
    /// clipboard format listener. Must be called once from the UI
    /// thread before clipboard changes will be reported.
    /// </summary>
    public void Start()
    {
        if (_hwndSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("SinuxBoardClipboardListener")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(HWND_MESSAGE),
            Width = 0,
            Height = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        _listenerRegistered = AddClipboardFormatListener(_hwndSource.Handle);
        if (!_listenerRegistered)
        {
            // Non-fatal: log via Debug output, keep the app running.
            // The window still exists, so a later manual retry is possible
            // if desired, but for v1 we simply continue without live updates.
            System.Diagnostics.Debug.WriteLine(
                $"SinuxBoard: AddClipboardFormatListener failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>
    /// Call immediately before the application writes to the clipboard
    /// itself (e.g. restoring an item), so the resulting
    /// WM_CLIPBOARDUPDATE notification is not mistaken for a new user
    /// copy and re-inserted into history.
    /// </summary>
    public void SuppressNextNotification()
    {
        _suppressNext = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            bool suppress = _suppressNext;
            _suppressNext = false;

            if (!suppress)
            {
                string? text = TryReadClipboardTextWithRetry();
                if (!string.IsNullOrEmpty(text))
                {
                    ClipboardTextChanged?.Invoke(this, text);
                }
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Reads text from the clipboard, tolerating the common case where
    /// another process briefly owns/locks the clipboard by retrying a
    /// few times with a short delay before giving up quietly.
    /// </summary>
    private static string? TryReadClipboardTextWithRetry()
    {
        for (int attempt = 1; attempt <= MaxReadAttempts; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    return Clipboard.GetText();
                }

                return null;
            }
            catch (COMException)
            {
                // Clipboard temporarily owned/locked by another process.
                if (attempt < MaxReadAttempts)
                {
                    Thread.Sleep(ReadRetryDelayMs);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SinuxBoard: clipboard read failed: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hwndSource is not null)
        {
            if (_listenerRegistered)
            {
                RemoveClipboardFormatListener(_hwndSource.Handle);
            }

            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        _disposed = true;
    }
}
