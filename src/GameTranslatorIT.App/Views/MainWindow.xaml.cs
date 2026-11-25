using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GameTranslatorIT.App.ViewModels;
using GameTranslatorIT.Core.Models;
using GameTranslatorIT.Core.Services;

namespace GameTranslatorIT.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new JsonSettingsService());
        DataContext = _viewModel;
        SourceInitialized += (_, _) => _viewModel.InitializeHotkeys(new WindowInteropHelper(this).Handle);
        Closing += (_, _) => _viewModel.Dispose();
    }

    private void LanguageSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, SelectedValue: not null }) return;
        _viewModel?.SaveLanguageSettings();
    }

    private void VoiceSetting_Changed(object sender, RoutedEventArgs e)
        => _viewModel?.SaveVoiceSettings();

    private void CaptureSetting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { IsLoaded: true })
            _viewModel?.SaveSettings();
    }

    private void ActiveAudioFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check)
            _viewModel?.ToggleActiveAudioDevices(check.IsChecked == true);
    }

    private void RegionEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check && check.DataContext is CaptureRegionConfig region)
            _viewModel?.SetRegionEnabled(region, check.IsChecked == true);
    }

    private void RegionMute_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check && check.DataContext is CaptureRegionConfig region)
            _viewModel?.SetRegionMute(region, check.IsChecked == true);
    }

    private void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureRegionConfig region)
            _viewModel?.SelectRegion(region);
    }

    private void CycleMode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureRegionConfig region)
            _viewModel?.CycleRegionMode(region);
    }
}
