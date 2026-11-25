using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameTranslatorIT.App.ViewModels;
using GameTranslatorIT.Tts;

namespace GameTranslatorIT.App.Views;

/// <summary>
/// Finestra dedicata alla scelta dell'uscita audio: elenco reale dei dispositivi,
/// prova voce e ripristino dell'uscita consigliata.
/// </summary>
public sealed partial class AudioDeviceWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AudioOutputDevice? _original;

    public AudioDeviceWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _original = viewModel.SelectedAudioOutputDevice;
        // La selezione della finestra è locale: così anche i dispositivi non attivi
        // restano selezionabili e possono essere richiesti a ConnectAudioDeviceAsync.
        DeviceList.Loaded += (_, _) => DeviceList.SelectedItem = _original;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check)
            _viewModel.ToggleActiveAudioDevices(check.IsChecked == true);
    }

    private async void DeviceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => await ApplySelectionAsync();

    private async void Confirm_Click(object sender, RoutedEventArgs e)
        => await ApplySelectionAsync();

    private async Task ApplySelectionAsync()
    {
        if (DeviceList.SelectedItem is not AudioOutputDevice device)
        {
            MessageBox.Show("Seleziona un dispositivo dall'elenco.", "Dispositivo audio",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!device.IsActive)
        {
            // Non chiude la finestra: l'utente vede lo stato "in attesa" e può aggiornare l'elenco.
            var connected = await _viewModel.ConnectAudioDeviceAsync(device);
            DeviceList.Items.Refresh();
            if (connected) DialogResult = true;
            return;
        }

        _viewModel.ClearPendingDevice();
        _viewModel.SelectedAudioOutputDevice = device;
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var recommended = _viewModel.AudioOutputDevices.FirstOrDefault(d => d.IsActive &&
                              d.Name.Contains("M8", StringComparison.OrdinalIgnoreCase))
                          ?? _viewModel.AudioOutputDevices.FirstOrDefault(d => d.IsActive && d.IsDefault)
                          ?? _viewModel.AudioOutputDevices.FirstOrDefault(d => d.IsActive &&
                                 d.Name.Contains("Cuffie", StringComparison.OrdinalIgnoreCase))
                          ?? _viewModel.AudioOutputDevices.FirstOrDefault(d => d.IsActive &&
                                 !d.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                          ?? _viewModel.AudioOutputDevices.FirstOrDefault(d => d.IsActive);

        if (recommended is null)
        {
            MessageBox.Show("Nessuna uscita audio attiva trovata. Controlla Windows e riprova.",
                "Dispositivo audio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewModel.ClearPendingDevice();
        _viewModel.SelectedAudioOutputDevice = recommended;
        DeviceList.SelectedItem = recommended;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_original is not null && !ReferenceEquals(_original, _viewModel.SelectedAudioOutputDevice))
            _viewModel.SelectedAudioOutputDevice = _original;
        DialogResult = false;
    }
}
