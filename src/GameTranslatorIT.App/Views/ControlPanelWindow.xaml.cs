using System.Windows;
using System.Windows.Controls;
using GameTranslatorIT.Core.Models;
using GameTranslatorIT.App.ViewModels;

namespace GameTranslatorIT.App.Views;

public partial class ControlPanelWindow : Window
{
    private readonly MainViewModel _viewModel;

    public ControlPanelWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RegionEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check && check.DataContext is CaptureRegionConfig region)
            _viewModel.SetRegionEnabled(region, check.IsChecked == true);
    }

    private void RegionMute_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check && check.DataContext is CaptureRegionConfig region)
            _viewModel.SetRegionMute(region, check.IsChecked == true);
    }

    private void RegionName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && box.DataContext is CaptureRegionConfig region)
            _viewModel.RenameRegion(region, box.Text);
    }

    private void RegionMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true } combo
            && combo.SelectedItem is RegionMuteMode mode
            && combo.DataContext is CaptureRegionConfig region)
            _viewModel.SetRegionMode(region, mode);
    }

    private void VoiceSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is Control { IsLoaded: true }) _viewModel.SaveVoiceSettings();
    }

    private void ActiveAudioFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check)
            _viewModel.ToggleActiveAudioDevices(check.IsChecked == true);
    }

    private void QuickControlSetting_Changed(object sender, RoutedEventArgs e) => _viewModel.SaveSettings();

    private void CaptureSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is Control { IsLoaded: true }) _viewModel.ApplyCaptureSettings();
    }

    private void CaptureSetting_LostFocus(object sender, RoutedEventArgs e) => _viewModel.ApplyCaptureSettings();

    private void TranslationSetting_LostFocus(object sender, RoutedEventArgs e) => _viewModel.ApplyTranslationSettings();
}
