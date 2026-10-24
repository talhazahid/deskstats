using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysDash.App;

public sealed class UpdateWindow : Window
{
    private readonly TextBlock _status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Height = 4, IsIndeterminate = true, Margin = new Thickness(0, 18, 0, 0) };
    private readonly Button _install = new() { Content = "Install update and restart DeskStats", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 18, 0, 0) };
    private readonly Button _later = new() { Content = "Dismiss", Margin = new Thickness(0, 8, 0, 0) };
    private TaskCompletionSource<bool>? _answer;
    private bool _canClose;
    public UpdateWindow()
    {
        Title = "DeskStats updates"; Width = 390; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "WindowBackdropBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _progress.SetResourceReference(ProgressBar.ForegroundProperty, "AccentBrush");
        var panel = new StackPanel { Margin = new Thickness(24) };
        var title = new TextBlock { Text = "DeskStats updates", FontSize = 20, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 14) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        panel.Children.Add(title); panel.Children.Add(_status); panel.Children.Add(_progress); panel.Children.Add(_install); panel.Children.Add(_later);
        Content = panel;
        _install.Click += (_, _) => _answer?.TrySetResult(true);
        _later.Click += (_, _) => { _answer?.TrySetResult(false); if (_canClose) Close(); else Hide(); };
        Closing += (_, e) => { if (!_canClose) { e.Cancel = true; _answer?.TrySetResult(false); Hide(); } };
        SetStatus("Checking for updates…");
    }
    public void SetStatus(string text, double? percent = null)
    {
        _status.Text = text;
        _progress.IsIndeterminate = percent is null;
        if (percent is { } value) _progress.Value = Math.Clamp(value, 0, 100);
    }
    public async Task<bool> AskToInstallAsync(string version)
    {
        _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetStatus($"Version {version} is ready. Install the update and restart the DeskStats app? Your widgets and positions will be kept. Windows may request administrator approval.", 100);
        _install.Visibility = Visibility.Visible; _later.Content = "Later";
        ShowInTaskbar = true; ShowActivated = true; Topmost = true;
        Show(); Activate();
        return await _answer.Task;
    }
    public void ShowResult(string text)
    {
        _canClose = true;
        _status.Text = text; _progress.Visibility = Visibility.Collapsed;
        _install.Visibility = Visibility.Collapsed; _later.Content = "Close";
        ShowInTaskbar = true; Topmost = true; Show(); Activate();
    }
    public void Finish() { _canClose = true; Close(); }
}
