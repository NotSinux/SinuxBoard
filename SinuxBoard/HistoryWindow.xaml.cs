using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SinuxBoard;

/// <summary>
/// Shows the most recent clipboard history and lets the user restore an
/// item to the clipboard with a double-click. Closing this window only
/// hides/closes the window itself; the application (tray icon and
/// clipboard monitor) keeps running because App.xaml sets
/// ShutdownMode="OnExplicitShutdown".
/// </summary>
public partial class HistoryWindow : Window
{
    private const int RecentCount = 100;

    private readonly DatabaseService _databaseService;
    private readonly ClipboardMonitor _clipboardMonitor;

    public HistoryWindow(DatabaseService databaseService, ClipboardMonitor clipboardMonitor)
    {
        InitializeComponent();

        _databaseService = databaseService;
        _clipboardMonitor = clipboardMonitor;

        LoadHistory();
    }

    private void LoadHistory()
    {
        try
        {
            List<ClipboardEntry> entries = _databaseService.GetRecent(RecentCount);
            HistoryListView.ItemsSource = entries.Select(e => new HistoryListItem(e)).ToList();
            StatusText.Text = $"{entries.Count} item(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to load history.";
            MessageBox.Show(
                $"Could not load clipboard history:\n{ex.Message}",
                "SinuxBoard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadHistory();
    }

    private void HistoryListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (HistoryListView.SelectedItem is not HistoryListItem item)
        {
            return;
        }

        RestoreToClipboard(item.Content);
    }

    private void RestoreToClipboard(string content)
    {
        try
        {
            // Suppress before writing so this programmatic write is not
            // re-captured as a brand-new clipboard entry.
            _clipboardMonitor.SuppressNextNotification();
            Clipboard.SetText(content);
            StatusText.Text = "Restored to clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed to restore item.";
            MessageBox.Show(
                $"Could not set the clipboard content:\n{ex.Message}",
                "SinuxBoard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Lightweight display wrapper for binding a ClipboardEntry to the
    /// ListView (adds a trimmed single-line preview and a local-time
    /// string). Intentionally kept as a private nested type rather than
    /// a separate ViewModel file, per the project's "no fake
    /// architecture" guidance.
    /// </summary>
    private sealed class HistoryListItem
    {
        public HistoryListItem(ClipboardEntry entry)
        {
            Content = entry.Content;
            LocalTimestamp = entry.CreatedAtUtc.ToLocalTime().ToString("g");

            string singleLine = entry.Content.Replace('\r', ' ').Replace('\n', ' ');
            Preview = singleLine.Length > 200 ? singleLine[..200] + "…" : singleLine;
        }

        public string Content { get; }
        public string Preview { get; }
        public string LocalTimestamp { get; }
    }
}
