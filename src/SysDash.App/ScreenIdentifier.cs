using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// DeskStats' own "Identify": shows a large label in the middle of each screen
/// for a few seconds, using exactly the names from the settings list — so users
/// on any setup can see which entry is which screen without screen numbers.
/// </summary>
internal static class ScreenIdentifier
{
    private static readonly List<Window> Open = new();
    private static DispatcherTimer? _timer;

    public static void Show(IEnumerable<(MonitorDescriptor Monitor, string Label)> screens, double seconds = 2.5)
    {
        CloseAll();

        foreach ((MonitorDescriptor monitor, string label) in screens)
        {
            Rect work = monitor.WorkAreaDip;
            var text = new TextBlock
            {
                Text = label,
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(200, work.Width * 0.7),
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                Left = work.Left + work.Width / 2 - 150,
                Top = work.Top + work.Height / 2 - 50,
                Content = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x16, 0x16, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(36, 22, 36, 22),
                    Child = text,
                },
            };

            // Centre on that screen once the real size is known.
            window.Loaded += (_, _) =>
            {
                window.Left = work.Left + (work.Width - window.ActualWidth) / 2;
                window.Top = work.Top + (work.Height - window.ActualHeight) / 2;
            };
            window.MouseDown += (_, _) => window.Close();

            Open.Add(window);
            window.Closed += (_, _) => Open.Remove(window);
            window.Show();
        }

        // One timer for the latest call, so a quick re-pick never cuts it short.
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            CloseAll();
        };
        _timer.Start();
    }

    private static void CloseAll()
    {
        foreach (Window window in Open.ToArray())
        {
            window.Close();
        }

        Open.Clear();
    }
}
