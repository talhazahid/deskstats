using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysDash.App;

/// <summary>
/// Small themed About dialog. Built in code (no XAML) because it is a single,
/// static read-only window.
/// </summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About DeskStats";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = FindResource("WindowBackdropBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x13));

        Content = BuildContent();
    }

    private FrameworkElement BuildContent()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        var title = new TextBlock
        {
            Text = "DeskStats",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        root.Children.Add(title);

        var versionText = new TextBlock
        {
            Text = $"Version {version}",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            Margin = new Thickness(0, 2, 0, 10),
        };
        root.Children.Add(versionText);

        root.Children.Add(new TextBlock
        {
            Text = "Live PC monitor widgets for your desktop — CPU, GPU, memory, network, disks and sensors, floating or docked in a sidebar.",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });

        root.Children.Add(new TextBlock
        {
            Text = "DeskStats is free software released under the GNU General Public License v3. It derives from SidebarDiagnostics (GPLv3) and uses LibreHardwareMonitor (MPL-2.0). See NOTICE and docs/COMPLIANCE.md.",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            Margin = new Thickness(0, 10, 0, 0),
        });

        var ok = new Button
        {
            Content = "OK",
            Width = 88,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            IsDefault = true,
            IsCancel = true,
        };
        ok.Click += (_, _) => Close();
        root.Children.Add(ok);

        return root;
    }
}