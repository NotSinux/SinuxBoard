using System;
using System.IO;
using System.Windows.Forms;

namespace SinuxBoard;

/// <summary>
/// Owns the Windows Forms NotifyIcon and its context menu. WinForms is
/// used here specifically because it gives a simpler, more reliable
/// tray icon than hosting one from WPF directly.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startWithWindowsItem;
    private bool _disposed;

    public event EventHandler? HistoryRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();

        var historyItem = new ToolStripMenuItem("History");
        historyItem.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);

        var exportItem = new ToolStripMenuItem("Export History");
        exportItem.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);

        var importItem = new ToolStripMenuItem("Import History");
        importItem.Click += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = false,
            Checked = StartupManager.IsEnabled()
        };
        _startWithWindowsItem.Click += OnToggleStartupClicked;

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(historyItem);
        menu.Items.Add(exportItem);
        menu.Items.Add(importItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "SinuxBoard",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SinuxBoard.ico");

        if (File.Exists(iconPath))
        {
            return new System.Drawing.Icon(iconPath);
        }

        // Fall back to the executable's embedded icon so the tray icon
        // is never missing even if the loose file was not deployed.
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            System.Drawing.Icon? extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void OnToggleStartupClicked(object? sender, EventArgs e)
    {
        try
        {
            if (StartupManager.IsEnabled())
            {
                StartupManager.Disable();
            }
            else
            {
                StartupManager.Enable();
            }

            _startWithWindowsItem.Checked = StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SinuxBoard: failed to toggle startup: {ex.Message}");
            MessageBox.Show(
                "Could not update the Windows startup setting. Please try again.",
                "SinuxBoard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public void RefreshStartupState()
    {
        _startWithWindowsItem.Checked = StartupManager.IsEnabled();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }
}
