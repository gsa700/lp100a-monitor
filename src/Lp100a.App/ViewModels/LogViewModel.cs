using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Media;
using Lp100a.App.Services;
using Lp100a.Core;

namespace Lp100a.App.ViewModels;

/// <summary>
/// Backs the TX log window: reads the CSV and presents it newest-first, refreshes on demand, and
/// hands the file to the OS for a spreadsheet. Read-only — the window never writes the log.
/// </summary>
public sealed class LogViewModel : ViewModelBase
{
    private readonly TxLoggingService _logging;

    public LogViewModel(TxLoggingService logging)
    {
        _logging = logging;
        RefreshCommand = new RelayCommand(Refresh);
        OpenInExcelCommand = new RelayCommand(OpenInExcel, () => File.Exists(_logging.LogPath));
        ClearCommand = new RelayCommand(Clear, () => File.Exists(_logging.LogPath));
        // A newly logged over should show up without the user hunting for Refresh.
        _logging.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<TxLogEntry> Rows { get; } = new();
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenInExcelCommand { get; }
    public RelayCommand ClearCommand { get; }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _statusBrush = Palette.DimBrush;
    public IBrush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    public void Refresh()
    {
        try
        {
            var rows = TxLogReader.Read(_logging.LogPath);
            Rows.Clear();
            // Chronological, like the CSV itself and like a paper log: newest at the BOTTOM. The
            // window scrolls to the last row so the most recent over is still what you land on.
            foreach (var row in rows) Rows.Add(row);

            StatusText = rows.Count == 0
                ? File.Exists(_logging.LogPath)
                    ? "No transmissions logged yet."
                    : "No log file yet — enable logging in Setup, then transmit."
                : $"{rows.Count} transmission(s), oldest first.";
            StatusBrush = Palette.DimBrush;
        }
        catch (Exception ex)
        {
            // A log open in Excel with an exclusive lock is the likely case; say so instead of
            // throwing on the UI thread.
            StatusText = $"Could not read the log: {ex.Message}";
            StatusBrush = Palette.RedBrush;
        }
        OpenInExcelCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }

    private void Clear()
    {
        try
        {
            var archived = _logging.ClearLog();   // renames aside; ClearLog raises Changed -> Refresh
            if (archived is not null)
            {
                // Name the archive explicitly: it's the difference between "my log is gone" and
                // "my log is over there".
                StatusText = $"Cleared — previous log saved as {Path.GetFileName(archived)}";
                StatusBrush = Palette.GreenBrush;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Could not clear the log: {ex.Message}";
            StatusBrush = Palette.RedBrush;
        }
    }

    private void OpenInExcel()
    {
        try { Process.Start(new ProcessStartInfo(_logging.LogPath) { UseShellExecute = true }); }
        catch { /* no handler registered for .csv */ }
    }

    public void Detach() => _logging.Changed -= Refresh;
}
