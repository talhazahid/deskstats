using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SysDash.App;

internal static class StartupUpdateChecks
{
    public static bool Run(string output)
    {
        var checks = new Dictionary<string, bool>();
        foreach (string choice in new[] { "Later", "Update now", "Close" })
        {
            var window = new UpdateWindow();
            Task<bool> answer = window.AskBeforeStartupAsync("9.0.0");
            checks[choice + "WaitsForDecision"] = !answer.IsCompleted;
            var buttons = ((StackPanel)window.Content).Children.OfType<Button>().ToArray();
            checks[choice + "OffersBothChoices"] = buttons.Any(b => Equals(b.Content, "Later")) && buttons.Any(b => Equals(b.Content, "Update now"));
            if (choice == "Close") window.Close();
            else buttons.Single(b => Equals(b.Content, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            checks[choice + "ResolvesCorrectly"] = answer.IsCompletedSuccessfully && answer.Result == (choice == "Update now");
            checks[choice + "CancellationMatchesChoice"] = window.DismissalToken.IsCancellationRequested == (choice != "Update now");
            window.Finish();
        }
        File.WriteAllText(Path.Combine(output, "startup-update-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
}
