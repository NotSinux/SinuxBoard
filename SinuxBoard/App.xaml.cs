using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfWindowState = System.Windows.WindowState;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SinuxBoard;

/// <summary>
/// SinuxBoard entry point. Runs entirely as a background/tray
/// application: no MainWindow is shown, and ShutdownMode is set to
/// OnExplicitShutdown in App.xaml so the app stays alive purely because
/// of its tray icon and clipboard listener, not because of any window.
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true
    };

    private DatabaseService? _databaseService;
    private ClipboardMonitor? _clipboardMonitor;
    private TrayIconService? _trayIconService;
    private HistoryWindow? _historyWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Must run before the first SqliteConnection is created.
            SQLitePCL.Batteries.Init();

            _databaseService = new DatabaseService();
            _databaseService.Initialize();

            _clipboardMonitor = new ClipboardMonitor();
            _clipboardMonitor.ClipboardTextChanged += OnClipboardTextChanged;
            _clipboardMonitor.Start();

            RestoreLatestClipboardEntry();

            _trayIconService = new TrayIconService();
            _trayIconService.HistoryRequested += (_, _) => ShowHistoryWindow();
            _trayIconService.ExportRequested += (_, _) => ExportHistory();
            _trayIconService.ImportRequested += (_, _) => ImportHistory();
            _trayIconService.ExitRequested += (_, _) => Shutdown();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"SinuxBoard failed to start:\n{ex.Message}",
                "SinuxBoard - Startup Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private void OnClipboardTextChanged(object? sender, string text)
    {
        // Runs on the UI thread (native message hook), but SQLite writes
        // are fast local I/O; still keep it off the message pump.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _databaseService?.InsertIfNotDuplicate(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SinuxBoard: failed to store clipboard entry: {ex.Message}");
            }
        });
    }

    private void RestoreLatestClipboardEntry()
    {
        if (_databaseService is null || _clipboardMonitor is null)
        {
            return;
        }

        try
        {
            ClipboardEntry? latest = _databaseService.GetLatest();
            if (latest is null)
            {
                return;
            }

            // Prevent the restore itself from being recorded as a new entry.
            _clipboardMonitor.SuppressNextNotification();
            WpfClipboard.SetText(latest.Content);
        }
        catch (Exception ex)
        {
            // Clipboard may be briefly unavailable at startup; not fatal.
            System.Diagnostics.Debug.WriteLine($"SinuxBoard: failed to restore clipboard: {ex.Message}");
        }
    }

    private void ShowHistoryWindow()
    {
        if (_databaseService is null || _clipboardMonitor is null)
        {
            return;
        }

        if (_historyWindow is not null)
        {
            _historyWindow.Activate();
            if (_historyWindow.WindowState == WpfWindowState.Minimized)
            {
                _historyWindow.WindowState = WindowState.Normal;
            }

            return;
        }

        _historyWindow = new HistoryWindow(_databaseService, _clipboardMonitor);
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
        _historyWindow.Activate();
    }

    private void ExportHistory()
    {
        if (_databaseService is null)
        {
            return;
        }

        try
        {
            var dialog = new WpfSaveFileDialog
            {
                Title = "Export Clipboard History",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"sinuxboard-export-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            List<ClipboardEntry> entries = _databaseService.GetAll();
            string json = JsonSerializer.Serialize(entries, ExportJsonOptions);
            File.WriteAllText(dialog.FileName, json);

            WpfMessageBox.Show(
                $"Exported {entries.Count} item(s) to:\n{dialog.FileName}",
                "SinuxBoard",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Export failed:\n{ex.Message}",
                "SinuxBoard",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }

    private void ImportHistory()
    {
        if (_databaseService is null)
        {
            return;
        }

        try
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "Import Clipboard History",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string json = File.ReadAllText(dialog.FileName);

            List<ClipboardEntry>? entries;
            try
            {
                entries = JsonSerializer.Deserialize<List<ClipboardEntry>>(json);
            }
            catch (JsonException jsonEx)
            {
                WpfMessageBox.Show(
                    $"The selected file is not a valid SinuxBoard export:\n{jsonEx.Message}",
                    "SinuxBoard",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning);
                return;
            }

            if (entries is null || entries.Count == 0)
            {
                WpfMessageBox.Show(
                    "The selected file did not contain any clipboard entries.",
                    "SinuxBoard",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Warning);
                return;
            }

            int imported = _databaseService.Import(entries);

            WpfMessageBox.Show(
                $"Imported {imported} of {entries.Count} item(s).",
                "SinuxBoard",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                $"Import failed:\n{ex.Message}",
                "SinuxBoard",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _clipboardMonitor?.Dispose();
        _trayIconService?.Dispose();

        base.OnExit(e);
    }
}
