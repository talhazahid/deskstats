using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace SysDash.App;

/// <summary>A place returned by the city search.</summary>
public sealed record CityMatch(string Name, string Detail, double Latitude, double Longitude)
{
    /// <summary>"Karachi, Sindh, Pakistan".</summary>
    public string Display => Detail.Length > 0 ? $"{Name}, {Detail}" : Name;
}

/// <summary>
/// Type-ahead city box for the settings window: typing "kar" shows
/// "Karachi, Sindh, Pakistan" and similar places from Open-Meteo's free
/// geocoding API (no key). Arrow keys / Enter / mouse pick a place;
/// Esc closes the list. Works for every country and language Open-Meteo knows.
/// </summary>
internal sealed class CityLookup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly TextBlock _caption;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _cts;
    private bool _suppress;
    private Window? _host;

    public CityLookup(string text)
    {
        Box = new TextBox { Text = text, Margin = new Thickness(0, 6, 0, 0) };

        _list = new ListBox
        {
            Focusable = false,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);

        _status = new TextBlock { FontSize = 12, Margin = new Thickness(10, 8, 10, 8), Visibility = Visibility.Collapsed };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        var content = new StackPanel();
        content.Children.Add(_status);
        content.Children.Add(_list);

        var frame = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 10, 12),
            MaxHeight = 320,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        frame.SetResourceReference(UIElement.EffectProperty, "CardShadow");

        _popup = new Popup
        {
            Child = frame,
            PlacementTarget = Box,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            AllowsTransparency = true,
            StaysOpen = true,
            Focusable = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        _caption = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        _caption.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var view = new StackPanel();
        view.Children.Add(Box);
        view.Children.Add(_popup);
        view.Children.Add(_caption);
        View = view;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = SearchAsync(Box.Text.Trim());
        };

        Box.TextChanged += OnTextChanged;
        Box.PreviewKeyDown += OnKeyDown;
        Box.LostKeyboardFocus += (_, _) => Close();
        Box.Loaded += (_, _) =>
        {
            _host = Window.GetWindow(Box);
            if (_host is not null)
            {
                _host.LocationChanged += OnHostMoved;
                _host.Deactivated += OnHostMoved;
            }
        };
        Box.Unloaded += (_, _) =>
        {
            Close();
            if (_host is not null)
            {
                _host.LocationChanged -= OnHostMoved;
                _host.Deactivated -= OnHostMoved;
                _host = null;
            }
        };
    }

    /// <summary>The text box (the editor whose text is saved).</summary>
    public TextBox Box { get; }

    /// <summary>Box + suggestion list + "picked" caption.</summary>
    public FrameworkElement View { get; }

    /// <summary>The place picked from the list, while the text still matches it.</summary>
    public CityMatch? Picked { get; private set; }

    public event Action<CityMatch>? CityPicked;

    private void OnHostMoved(object? sender, EventArgs e) => Close();

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        if (Picked is not null && !string.Equals(Box.Text.Trim(), Picked.Name, StringComparison.OrdinalIgnoreCase))
        {
            Picked = null;
            _caption.Visibility = Visibility.Collapsed;
        }

        _debounce.Stop();
        if (Box.Text.Trim().Length < 2)
        {
            _cts?.Cancel();
            Close();
            return;
        }

        _debounce.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            if (e.Key == Key.Down && Box.Text.Trim().Length >= 2)
            {
                _ = SearchAsync(Box.Text.Trim());
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                Move(+1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                // The highlighted place, or the top match when none is highlighted.
                object? target = _list.SelectedItem ?? (_list.Items.Count > 0 ? _list.Items[0] : null);
                if (target is ListBoxItem { Tag: CityMatch chosen })
                {
                    Pick(chosen);
                    e.Handled = true; // don't trigger the window's Save button
                }

                break;
            case Key.Escape:
                Close();
                e.Handled = true; // don't close the window
                break;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        int next = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.SelectedIndex = next;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Pick(CityMatch match)
    {
        _suppress = true;
        try
        {
            Box.Text = match.Name;
            Box.CaretIndex = Box.Text.Length;
        }
        finally
        {
            _suppress = false;
        }

        Picked = match;
        _caption.Text = $"📍 {match.Display}";
        _caption.Visibility = Visibility.Visible;
        _debounce.Stop();
        Close();
        CityPicked?.Invoke(match);
    }

    private void Close()
    {
        _popup.IsOpen = false;
    }

    private async Task SearchAsync(string query)
    {
        if (query.Length < 2)
        {
            return;
        }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        List<CityMatch> matches;
        try
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=en&format=json&name="
                         + Uri.EscapeDataString(query);
            string json = await Http.GetStringAsync(url, cts.Token);
            matches = Parse(json);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return; // a newer search replaced this one
        }
        catch (Exception)
        {
            if (cts.IsCancellationRequested || !Box.IsKeyboardFocusWithin)
            {
                return;
            }

            Show(Array.Empty<CityMatch>(), "Couldn't search right now — check your internet connection.");
            return;
        }

        if (cts.IsCancellationRequested || !Box.IsKeyboardFocusWithin)
        {
            return;
        }

        Show(matches, matches.Count == 0 ? $"No places found for \"{query}\"." : null);
    }

    private void Show(IReadOnlyList<CityMatch> matches, string? status)
    {
        _list.Items.Clear();
        foreach (CityMatch match in matches)
        {
            var line = new TextBlock { FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            line.Inlines.Add(new Run(match.Name) { FontWeight = FontWeights.SemiBold });
            if (match.Detail.Length > 0)
            {
                var rest = new Run(", " + match.Detail);
                rest.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
                line.Inlines.Add(rest);
            }

            var item = new ListBoxItem { Content = line, Tag = match, Focusable = false, ToolTip = match.Display };
            item.SetResourceReference(FrameworkElement.StyleProperty, "SuggestionItem");
            item.PreviewMouseLeftButtonUp += (_, args) =>
            {
                Pick(match);
                args.Handled = true;
            };
            _list.Items.Add(item);
        }

        _status.Text = status ?? string.Empty;
        _status.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        _list.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_popup.Child is FrameworkElement frame)
        {
            frame.MinWidth = Math.Max(Box.ActualWidth, 200);
            frame.MaxWidth = Math.Max(Box.ActualWidth, 360);
        }

        _popup.IsOpen = true;
    }

    private static List<CityMatch> Parse(string json)
    {
        var matches = new List<CityMatch>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return matches;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement r in results.EnumerateArray())
        {
            string name = Str(r, "name");
            if (name.Length == 0 ||
                !r.TryGetProperty("latitude", out JsonElement lat) ||
                !r.TryGetProperty("longitude", out JsonElement lon))
            {
                continue;
            }

            var parts = new List<string>();
            string admin1 = Str(r, "admin1");
            if (admin1.Length > 0 && !admin1.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(admin1);
            }

            string country = Str(r, "country");
            if (country.Length > 0)
            {
                parts.Add(country);
            }

            var match = new CityMatch(name, string.Join(", ", parts), lat.GetDouble(), lon.GetDouble());
            if (seen.Add(match.Display))
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}
