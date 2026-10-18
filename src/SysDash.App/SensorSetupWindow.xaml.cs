using System.ComponentModel;
using System.Windows;
using SysDash.Core.Diagnostics;

namespace SysDash.App;

public partial class SensorSetupWindow : Window
{
    private bool _installing;
    public SensorSetupWindow() => InitializeComponent();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        _installing = true;
        InstallButton.IsEnabled = ContinueButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "Installing the sensor driver…";
        try
        {
            bool installed = await SensorDriver.InstallPawnIOAsync();
            StatusText.Text = PawnIOInstaller.LastMessage;
            if (installed) CloseAfterInstall();
            else if (PawnIOInstaller.RestartRequired)
            {
                InstallButton.Content = "Restart Windows to enable sensors";
                ContinueButton.Content = "Continue for now";
            }
            else InstallButton.Content = "Retry driver installation";
        }
        finally
        {
            _installing = false;
            InstallButton.IsEnabled = !PawnIOInstaller.RestartRequired;
            ContinueButton.IsEnabled = true;
        }
    }

    private void CloseAfterInstall()
    {
        _installing = false;
        Close();
    }

    private void OnContinue(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_installing) e.Cancel = true;
        base.OnClosing(e);
    }
}
