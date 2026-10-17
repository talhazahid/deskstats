using System.Windows;
using System.Windows.Input;

namespace SysDash.App;

/// <summary>Single-line text input dialog (used for "Save layout as…").</summary>
public partial class NameDialog : Window
{
    public NameDialog(string prompt, string initialValue = "")
    {
        InitializeComponent();

        PromptText.Text = prompt;
        Title = prompt;
        NameBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>Trimmed text entered by the user (valid only when ShowDialog returned true).</summary>
    public string ResultName => NameBox.Text.Trim();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Please enter a name.");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            OnSave(sender, e);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}