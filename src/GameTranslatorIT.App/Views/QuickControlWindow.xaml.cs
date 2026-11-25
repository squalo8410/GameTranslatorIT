using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameTranslatorIT.App.Services;
using GameTranslatorIT.App.ViewModels;
using GameTranslatorIT.Core.Models;

namespace GameTranslatorIT.App.Views;

public partial class QuickControlWindow : Window
{
    private readonly MainViewModel _viewModel;

    public QuickControlWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowCaptureExclusion.Exclude(this);
        Closed += (_, _) => WindowCaptureExclusion.Restore(this);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _viewModel.HideQuickControl();

    private void VoiceSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is Control { IsLoaded: true }) _viewModel.SaveVoiceSettings();
    }

    private void RegionMute_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsLoaded: true } check && check.DataContext is CaptureRegionConfig region)
            _viewModel.SetRegionMute(region, check.IsChecked == true);
    }
}
