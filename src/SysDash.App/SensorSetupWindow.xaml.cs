using System.Windows;
using SysDash.Core.Diagnostics;

namespace SysDash.App;

public partial class SensorSetupWindow : Window
{
    public SensorSetupWindow() => InitializeComponent();

    public static string SensorLimit => SensorDriver.StoreSensorLimit;

    private void OnContinue(object sender, RoutedEventArgs e) => Close();
}
