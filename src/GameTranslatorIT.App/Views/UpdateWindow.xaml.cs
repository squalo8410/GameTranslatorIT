using System.Diagnostics;
using System.Windows;
using GameTranslatorIT.App.Services;
using GameTranslatorIT.App.ViewModels;

namespace GameTranslatorIT.App.Views;

/// <summary>Finestra di notifica e conferma di un aggiornamento GitHub Releases.</summary>
public sealed partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly MainViewModel _viewModel;
    private bool _busy;

    public UpdateWindow(UpdateInfo info, MainViewModel viewModel)
    {
        InitializeComponent();
        _info = info;
        _viewModel = viewModel;

        var when = _info.PublishedAt?.LocalDateTime.ToString("dd/MM/yyyy HH:mm") ?? "data non disponibile";
        SummaryText.Text = $"Versione installata {UpdateService.CurrentVersion} → disponibile {_info.Version}. Pubblicata il {when}. " +
                           $"Asset: {_info.Asset.Name}. L'integrità verrà verificata prima dell'installazione.";
        NotesBox.Text = string.IsNullOrWhiteSpace(_info.Notes)
            ? "Nessuna nota rilasciata per questa versione."
            : _info.Notes;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetButtonsEnabled(false);

        var progress = new Progress<string>(message =>
        {
            StatusText.Text = message;
            ProgressBar.IsIndeterminate = message.Contains("Preparazione", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("avviato", StringComparison.OrdinalIgnoreCase);
            if (message.Contains("Download", StringComparison.OrdinalIgnoreCase) &&
                message.EndsWith("%", StringComparison.Ordinal) &&
                int.TryParse(message[^4..^1], out var percent))
                ProgressBar.Value = percent;
        });

        try
        {
            await _viewModel.ApplyUpdateAsync(_info, progress);
            ProgressBar.Value = 100;
            StatusText.Text = "Aggiornamento applicato. Riavvio in corso...";
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Aggiornamento non riuscito: {ex.Message}";
            MessageBox.Show(this, $"Aggiornamento non riuscito: {ex.Message}\n\nL'app continua a funzionare con la versione corrente.",
                "Aggiornamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetButtonsEnabled(true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_info.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile aprire la pagina release: {ex.Message}",
                "Aggiornamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IgnoreUpdate(_info.Version);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SetButtonsEnabled(bool enabled)
    {
        ApplyButton.IsEnabled = enabled;
        OpenButton.IsEnabled = enabled;
        IgnoreButton.IsEnabled = enabled;
        CancelButton.IsEnabled = enabled;
    }
}
