using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SysDash.Monitoring;
using SysDash.SDK.Widgets;

namespace SysDash.App;

/// <summary>
/// Live window options for a floating widget, edited in the settings window's
/// "Window" section (opacity / layer apply immediately; Cancel reverts them).
/// </summary>
public sealed class WidgetAppearance
{
    public double Opacity { get; set; } = 1.0;

    /// <summary>"top", "normal" or "bottom".</summary>
    public string Layer { get; set; } = "normal";

    public bool ClickThrough { get; set; }

    /// <summary>"dark", "light", or "" = follow the app theme.</summary>
    public string Theme { get; set; } = string.Empty;

    public Action<string?>? ApplyTheme { get; set; }

    public Action<double>? ApplyOpacity { get; set; }

    public Action<string>? ApplyLayer { get; set; }

    public Action<bool>? ApplyClickThrough { get; set; }
}

/// <summary>
/// Modern per-widget settings window. Reflects over the widget's settings POCO
/// and builds grouped cards: on/off → toggle switch, ranged numbers → slider with
/// a live value, other numbers / text → input box, enums → dropdown. Labels,
/// help text, groups and slider ranges come from <see cref="SettingAttribute"/>;
/// a range can be capped by detected hardware (e.g. CPU thread count). Floating
/// widgets also get a "Window" card (opacity, layer, click-through).
/// </summary>
public partial class SettingsEditorWindow : Window
{
    private static readonly (string Key, string Label)[] Themes =
    {
        ("", "Same as app"),
        ("dark", "Dark"),
        ("light", "Light"),
    };

    private static readonly (string Key, string Label)[] Layers =
    {
        ("top", "Always on top"),
        ("normal", "Normal"),
        ("bottom", "On desktop (behind windows)"),
    };

    private readonly WidgetInstance _instance;
    private readonly WidgetAppearance? _appearance;
    // Last saved window options; closing the window reverts unsaved previews to these.
    private double _originalOpacity;
    private string _originalLayer;
    private string _originalTheme;
    private readonly List<EditorRow> _editors = new();
    private readonly List<CityLookup> _lookups = new();
    private readonly DispatcherTimer _savedFlash = new() { Interval = TimeSpan.FromSeconds(1.8) };

    private Slider? _opacitySlider;
    private ComboBox? _layerBox;
    private ComboBox? _themeBox;
    private CheckBox? _clickThroughSwitch;

    /// <summary>
    /// An editor and the value it showed when the form was built / last saved.
    /// Only editors the user changed are written back, so values the widget
    /// updated itself meanwhile (e.g. coordinates looked up for a city) are kept.
    /// Null baseline = always write (after "Reset to defaults").
    /// </summary>
    private sealed class EditorRow
    {
        public EditorRow(PropertyInfo property, Control editor, string? baseline)
        {
            Property = property;
            Editor = editor;
            Baseline = baseline;
        }

        public PropertyInfo Property { get; }

        public Control Editor { get; }

        public string? Baseline { get; set; }
    }

    /// <summary>Raised after every Save (the window stays open).</summary>
    public event Action? Saved;

    public SettingsEditorWindow(WidgetInstance instance, WidgetAppearance? appearance = null)
    {
        InitializeComponent();

        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _appearance = appearance;
        _originalOpacity = appearance?.Opacity ?? 1.0;
        _originalLayer = appearance?.Layer ?? "normal";
        _originalTheme = appearance?.Theme ?? string.Empty;

        Title = $"{_instance.EffectiveTitle} — Settings";
        TitleText.Text = _instance.EffectiveTitle;
        DescriptionText.Text = _instance.Widget.Description;

        BuildForm(_instance.Settings, markDirty: false);

        _savedFlash.Tick += (_, _) =>
        {
            _savedFlash.Stop();
            SaveButton.Content = "Save";
        };
    }

    // ---- form -------------------------------------------------------------

    private void BuildForm(object valueSource, bool markDirty)
    {
        EditorPanel.Children.Clear();
        _editors.Clear();
        _lookups.Clear();

        Type settingsType = _instance.Settings.GetType();
        var entries = settingsType.GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0 &&
                        p.GetSetMethod() is not null &&
                        p.GetGetMethod() is not null)
            .Select(p => (Property: p, Meta: p.GetCustomAttribute<SettingAttribute>()))
            .Where(e => e.Meta is not { Hidden: true })
            .ToList();

        var groups = entries
            .GroupBy(e => e.Meta?.Group ?? "General")
            .OrderBy(g => g.Min(e => e.Meta?.Order ?? 1000));

        foreach (var group in groups)
        {
            StackPanel body = AddCard(group.Key);
            foreach (var entry in group.OrderBy(e => e.Meta?.Order ?? 1000).ThenBy(e => e.Property.Name))
            {
                object? value = entry.Property.GetValue(valueSource);
                FrameworkElement? row = BuildRow(entry.Property, entry.Meta, value, out Control? editor);
                if (row is null || editor is null)
                {
                    continue;
                }

                if (body.Children.Count > 0)
                {
                    row.Margin = new Thickness(0, 12, 0, 0);
                }

                body.Children.Add(row);
                _editors.Add(new EditorRow(entry.Property, editor, markDirty ? null : Snapshot(editor)));
            }
        }

        if (_appearance is not null)
        {
            BuildWindowCard(_appearance);
        }

        if (EditorPanel.Children.Count == 0)
        {
            EditorPanel.Children.Add(Text("This widget has no settings.", 12.5, "TextSecondaryBrush"));
        }
    }

    private StackPanel AddCard(string title)
    {
        var body = new StackPanel();
        var stack = new StackPanel();
        var header = new TextBlock { Text = title.ToUpperInvariant() };
        header.SetResourceReference(StyleProperty, "SectionHeader");
        header.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(header);
        stack.Children.Add(body);

        var card = new Border { Child = stack, Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(StyleProperty, "WidgetSection");
        EditorPanel.Children.Add(card);
        return body;
    }

    private FrameworkElement? BuildRow(PropertyInfo property, SettingAttribute? meta, object? value, out Control? editor)
    {
        Type actual = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        string label = meta?.Label ?? Humanize(property.Name);
        string? help = meta?.Description;

        if (meta?.OptionsSource is { Length: > 0 } source)
        {
            ComboBox? picker = BuildOptionsPicker(source, actual, value);
            if (picker is null)
            {
                editor = null;
                return null; // nothing to choose from (e.g. a single GPU)
            }

            editor = picker;
            return LabelledRow(label, help, picker);
        }

        if (actual == typeof(bool))
        {
            var toggle = new CheckBox { IsChecked = value is true, VerticalAlignment = VerticalAlignment.Center };
            toggle.SetResourceReference(StyleProperty, "ToggleSwitch");
            editor = toggle;
            return LabelledRow(label, help, toggle);
        }

        if (IsNumeric(actual) && meta is not null && meta.Max > meta.Min)
        {
            double max = ResolveMax(meta);
            double step = meta.Step > 0 ? meta.Step : 1;
            double current = Math.Clamp(Convert.ToDouble(value ?? meta.Min, CultureInfo.InvariantCulture), meta.Min, max);

            var slider = new Slider
            {
                Minimum = meta.Min,
                Maximum = max,
                TickFrequency = step,
                IsSnapToTickEnabled = true,
                SmallChange = step,
                LargeChange = step,
                Value = current,
                Margin = new Thickness(0, 6, 0, 0),
            };
            slider.SetResourceReference(StyleProperty, "ModernSlider");

            TextBlock valueText = Text(FormatSlider(current, meta.Unit), 12.5, "AccentBrush");
            valueText.FontWeight = FontWeights.SemiBold;
            valueText.HorizontalAlignment = HorizontalAlignment.Right;
            slider.ValueChanged += (_, args) => valueText.Text = FormatSlider(args.NewValue, meta.Unit);

            var top = new Grid();
            top.Children.Add(Text(label, 13, "TextPrimaryBrush"));
            top.Children.Add(valueText);

            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(slider);
            if (!string.IsNullOrWhiteSpace(help))
            {
                stack.Children.Add(Help(help));
            }

            editor = slider;
            return stack;
        }

        if (IsNumeric(actual))
        {
            var box = new TextBox
            {
                Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                Width = 130,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            editor = box;
            return LabelledRow(label, help, box);
        }

        if (actual.IsEnum)
        {
            var combo = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
            foreach (string name in Enum.GetNames(actual))
            {
                combo.Items.Add(name);
            }

            combo.SelectedItem = value?.ToString() ?? string.Empty;
            editor = combo;
            return LabelledRow(label, help, combo);
        }

        if (actual == typeof(string) || actual == typeof(List<string>) || actual == typeof(string[]))
        {
            string text = value is string s
                ? s
                : value is IEnumerable e
                    ? string.Join(", ", e.Cast<object?>().Select(o => o?.ToString()))
                    : string.Empty;

            var stack = new StackPanel();
            stack.Children.Add(Text(label, 13, "TextPrimaryBrush"));

            TextBox box;
            if (actual == typeof(string) &&
                string.Equals(meta?.Lookup, "city", StringComparison.OrdinalIgnoreCase))
            {
                var lookup = new CityLookup(text);
                lookup.CityPicked += FillCoordinates;
                _lookups.Add(lookup);
                box = lookup.Box;
                stack.Children.Add(lookup.View);
            }
            else
            {
                box = new TextBox { Text = text, Margin = new Thickness(0, 6, 0, 0) };
                stack.Children.Add(box);
            }

            if (!string.IsNullOrWhiteSpace(help))
            {
                stack.Children.Add(Help(help));
            }

            editor = box;
            return stack;
        }

        editor = null;
        return null;
    }

    /// <summary>
    /// Dropdown filled with hardware detected right now. Each item's Tag is the
    /// value written back (GPU index, or adapter / drive name; "" = automatic / all).
    /// </summary>
    private ComboBox? BuildOptionsPicker(string source, Type actual, object? value)
    {
        Core.Models.MetricsSnapshot? latest =
            (_instance.Services.GetService(typeof(IMetricsService)) as IMetricsService)?.Latest;

        var items = new List<(string Text, object Tag)>();
        switch (source.ToLowerInvariant())
        {
            case "gpus":
                IReadOnlyList<Core.Models.GpuReading> gpus = latest?.Gpus ?? Array.Empty<Core.Models.GpuReading>();
                if (gpus.Count <= 1)
                {
                    return null;
                }

                for (int i = 0; i < gpus.Count; i++)
                {
                    items.Add((gpus[i].Name, i));
                }

                break;

            case "adapters":
                items.Add(("Automatic (busiest adapter)", string.Empty));
                foreach (var adapter in (latest?.Adapters ?? Array.Empty<Core.Models.NetworkAdapterReading>())
                             .OrderByDescending(a => a.IsUp).ThenBy(a => a.Name))
                {
                    items.Add((adapter.IsUp ? adapter.Name : $"{adapter.Name} (disconnected)", adapter.Name));
                }

                break;

            case "drives":
                items.Add(("All drives", string.Empty));
                foreach (var drive in latest?.Drives ?? Array.Empty<Core.Models.DriveReading>())
                {
                    items.Add((drive.Name.TrimEnd('\\'), drive.Name));
                }

                break;

            default:
                return null;
        }

        // Keep a saved value that is not connected right now selectable.
        if (actual == typeof(string) && value is string saved && saved.Length > 0 &&
            !items.Any(i => saved.Equals(i.Tag as string, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(($"{saved} (not found)", saved));
        }

        var combo = new ComboBox { MinWidth = 190, MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center };
        int selected = 0;
        for (int i = 0; i < items.Count; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = items[i].Text, Tag = items[i].Tag });
            if (Equals(items[i].Tag, value) ||
                (items[i].Tag is string tag && value is string v && tag.Equals(v, StringComparison.OrdinalIgnoreCase)) ||
                (items[i].Tag is string empty && empty.Length == 0 && value is null))
            {
                selected = i;
            }
        }

        combo.SelectedIndex = selected;
        return combo;
    }

    private void BuildWindowCard(WidgetAppearance appearance)
    {
        StackPanel body = AddCard("Window");

        // Opacity
        _opacitySlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            SmallChange = 5,
            LargeChange = 10,
            Value = Math.Clamp(Math.Round(appearance.Opacity * 100), 0, 100),
            Margin = new Thickness(0, 6, 0, 0),
        };
        _opacitySlider.SetResourceReference(StyleProperty, "ModernSlider");

        TextBlock opacityValue = Text($"{_opacitySlider.Value:0}%", 12.5, "AccentBrush");
        opacityValue.FontWeight = FontWeights.SemiBold;
        opacityValue.HorizontalAlignment = HorizontalAlignment.Right;
        _opacitySlider.ValueChanged += (_, args) =>
        {
            opacityValue.Text = $"{args.NewValue:0}%";
            appearance.ApplyOpacity?.Invoke(args.NewValue / 100.0); // live preview
        };

        var opacityTop = new Grid();
        opacityTop.Children.Add(Text("Background opacity", 13, "TextPrimaryBrush"));
        opacityTop.Children.Add(opacityValue);
        var opacityStack = new StackPanel();
        opacityStack.Children.Add(opacityTop);
        opacityStack.Children.Add(_opacitySlider);
        body.Children.Add(opacityStack);

        // Theme (this widget only)
        _themeBox = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (_, text) in Themes)
        {
            _themeBox.Items.Add(text);
        }

        int themeIndex = Array.FindIndex(Themes, t => t.Key.Equals(appearance.Theme ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        _themeBox.SelectedIndex = themeIndex >= 0 ? themeIndex : 0;
        _themeBox.SelectionChanged += (_, _) =>
        {
            if (_themeBox.SelectedIndex >= 0)
            {
                appearance.ApplyTheme?.Invoke(Themes[_themeBox.SelectedIndex].Key); // live preview
            }
        };

        FrameworkElement themeRow = LabelledRow("Theme", "Dark or light for this widget only, or the same as the app.", _themeBox);
        themeRow.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(themeRow);

        // Layer
        _layerBox = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (_, text) in Layers)
        {
            _layerBox.Items.Add(text);
        }

        int layerIndex = Array.FindIndex(Layers, l => l.Key.Equals(appearance.Layer, StringComparison.OrdinalIgnoreCase));
        _layerBox.SelectedIndex = layerIndex >= 0 ? layerIndex : 1;
        _layerBox.SelectionChanged += (_, _) =>
        {
            if (_layerBox.SelectedIndex >= 0)
            {
                appearance.ApplyLayer?.Invoke(Layers[_layerBox.SelectedIndex].Key); // live preview
            }
        };

        FrameworkElement layerRow = LabelledRow("Layer", "Always on top, a normal window, or pinned to the desktop.", _layerBox);
        layerRow.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(layerRow);

        // Click-through (applied on Save only — while active the widget ignores the mouse)
        _clickThroughSwitch = new CheckBox { IsChecked = appearance.ClickThrough, VerticalAlignment = VerticalAlignment.Center };
        _clickThroughSwitch.SetResourceReference(StyleProperty, "ToggleSwitch");
        FrameworkElement clickRow = LabelledRow(
            "Click-through",
            "Mouse clicks pass through the widget. Turn it off again from the tray → Widgets menu.",
            _clickThroughSwitch);
        clickRow.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(clickRow);
    }

    private FrameworkElement LabelledRow(string label, string? help, FrameworkElement control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(Text(label, 13, "TextPrimaryBrush"));
        if (!string.IsNullOrWhiteSpace(help))
        {
            left.Children.Add(Help(help));
        }

        grid.Children.Add(left);
        grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        return grid;
    }

    private static TextBlock Text(string text, double size, string brushKey)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private static TextBlock Help(string text)
    {
        TextBlock block = Text(text, 11.5, "TextTertiaryBrush");
        block.Margin = new Thickness(0, 2, 0, 0);
        return block;
    }

    private static string FormatSlider(double value, string? unit)
    {
        string number = Math.Abs(value % 1) < 0.0001
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    /// <summary>Caps a slider maximum by something detected on this PC.</summary>
    private double ResolveMax(SettingAttribute meta)
    {
        double max = meta.Max;
        if (string.Equals(meta.MaxSource, "cpuThreads", StringComparison.OrdinalIgnoreCase))
        {
            int? threads = (_instance.Services.GetService(typeof(IMetricsService)) as IMetricsService)
                ?.Latest.Cpu?.PerCoreLoad?.Count;
            if (threads is > 0)
            {
                max = Math.Min(max, threads.Value);
            }
        }

        return Math.Max(meta.Min + (meta.Step > 0 ? meta.Step : 1), max);
    }

    /// <summary>A picked city also fills the Latitude / Longitude boxes (Advanced).</summary>
    private void FillCoordinates(CityMatch match)
    {
        foreach (EditorRow row in _editors)
        {
            if (row.Editor is not TextBox box)
            {
                continue;
            }

            if (row.Property.Name == "Latitude")
            {
                box.Text = match.Latitude.ToString(CultureInfo.InvariantCulture);
            }
            else if (row.Property.Name == "Longitude")
            {
                box.Text = match.Longitude.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Writes a picked city's coordinates straight into the settings and marks
    /// it as resolved, so the widget does not have to look the name up again.
    /// </summary>
    private void ApplyLookups()
    {
        object settings = _instance.Settings;
        Type type = settings.GetType();
        foreach (CityLookup lookup in _lookups)
        {
            if (lookup.Picked is not { } match ||
                !string.Equals(lookup.Box.Text.Trim(), match.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SetIfExists(type, settings, "Latitude", match.Latitude);
            SetIfExists(type, settings, "Longitude", match.Longitude);
            SetIfExists(type, settings, "ResolvedLocation", lookup.Box.Text.Trim());
        }
    }

    private static void SetIfExists(Type type, object target, string name, object value)
    {
        PropertyInfo? property = type.GetProperty(name);
        if (property?.GetSetMethod() is null)
        {
            return;
        }

        Type actual = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(target, Convert.ChangeType(value, actual, CultureInfo.InvariantCulture));
    }

    /// <summary>Text form of what an editor currently shows (for change tracking).</summary>
    private static string Snapshot(Control editor) => editor switch
    {
        CheckBox checkbox => checkbox.IsChecked == true ? "1" : "0",
        Slider slider => slider.Value.ToString("R", CultureInfo.InvariantCulture),
        ComboBox combo => combo.SelectedIndex.ToString(CultureInfo.InvariantCulture),
        TextBox text => text.Text,
        _ => string.Empty,
    };

    // ---- buttons ----------------------------------------------------------

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        BuildForm(_instance.Widget.CreateDefaultSettings(), markDirty: true);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate every changed editor first, so a bad number saves nothing.
            var changed = _editors.Where(r => r.Baseline is null || r.Baseline != Snapshot(r.Editor)).ToList();
            foreach (EditorRow row in changed)
            {
                if (row.Editor is TextBox numberBox &&
                    IsNumeric(Nullable.GetUnderlyingType(row.Property.PropertyType) ?? row.Property.PropertyType) &&
                    !double.TryParse(numberBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw new FormatException();
                }
            }

            // Picked city first, so a Latitude / Longitude the user edited by hand afterwards wins.
            ApplyLookups();

            foreach (EditorRow row in changed)
            {
                ApplyEditor(row);
                row.Baseline = Snapshot(row.Editor);
            }

            if (_appearance is not null)
            {
                if (_opacitySlider is not null)
                {
                    _appearance.ApplyOpacity?.Invoke(_opacitySlider.Value / 100.0);
                }

                if (_layerBox is not null && _layerBox.SelectedIndex >= 0)
                {
                    _appearance.ApplyLayer?.Invoke(Layers[_layerBox.SelectedIndex].Key);
                }

                if (_themeBox is not null && _themeBox.SelectedIndex >= 0)
                {
                    _appearance.ApplyTheme?.Invoke(Themes[_themeBox.SelectedIndex].Key);
                }

                bool clickThrough = _clickThroughSwitch?.IsChecked == true;
                if (clickThrough != _appearance.ClickThrough)
                {
                    _appearance.ApplyClickThrough?.Invoke(clickThrough);
                    _appearance.ClickThrough = clickThrough;
                }

                // These are now the saved values: closing keeps them.
                if (_opacitySlider is not null)
                {
                    _originalOpacity = _opacitySlider.Value / 100.0;
                    _appearance.Opacity = _originalOpacity;
                }

                if (_layerBox is not null && _layerBox.SelectedIndex >= 0)
                {
                    _originalLayer = Layers[_layerBox.SelectedIndex].Key;
                    _appearance.Layer = _originalLayer;
                }

                if (_themeBox is not null && _themeBox.SelectedIndex >= 0)
                {
                    _originalTheme = Themes[_themeBox.SelectedIndex].Key;
                    _appearance.Theme = _originalTheme;
                }
            }

            // Stay open so several changes can be tried one after another.
            Saved?.Invoke();
            SaveButton.Content = "Saved ✓";
            _savedFlash.Stop();
            _savedFlash.Start();
        }
        catch (FormatException)
        {
            MessageBox.Show(
                "One of the values is not a valid number. Please fix it before saving.",
                "DeskStats",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}", "DeskStats");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _savedFlash.Stop();
        RevertPreview(); // undo unsaved live previews (back to the last saved values)
        base.OnClosed(e);
    }

    private void RevertPreview()
    {
        if (_appearance is null)
        {
            return;
        }

        _appearance.ApplyOpacity?.Invoke(_originalOpacity);
        _appearance.ApplyLayer?.Invoke(_originalLayer);
        _appearance.ApplyTheme?.Invoke(_originalTheme);
    }

    // ---- value conversion ---------------------------------------------------

    private void ApplyEditor(EditorRow row)
    {
        Type actual = Nullable.GetUnderlyingType(row.Property.PropertyType) ?? row.Property.PropertyType;
        object settings = _instance.Settings;

        switch (row.Editor)
        {
            case CheckBox checkbox:
                row.Property.SetValue(settings, checkbox.IsChecked == true);
                break;

            case Slider slider:
                row.Property.SetValue(settings, Convert.ChangeType(slider.Value, actual, CultureInfo.InvariantCulture));
                break;

            case ComboBox picker when picker.SelectedItem is ComboBoxItem { Tag: not null } choice:
                row.Property.SetValue(settings, actual == typeof(int)
                    ? Convert.ToInt32(choice.Tag, CultureInfo.InvariantCulture)
                    : choice.Tag);
                break;

            case ComboBox combo when actual.IsEnum:
                if (combo.SelectedItem is string text)
                {
                    row.Property.SetValue(settings, Enum.Parse(actual, text));
                }

                break;

            case TextBox input:
                ApplyText(row.Property, settings, actual, input.Text);
                break;
        }
    }

    private static void ApplyText(PropertyInfo property, object settings, Type actual, string input)
    {
        if (actual == typeof(string))
        {
            property.SetValue(settings, input);
        }
        else if (IsNumeric(actual))
        {
            object number = Convert.ChangeType(
                double.Parse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                actual,
                CultureInfo.InvariantCulture);
            property.SetValue(settings, number);
        }
        else if (actual == typeof(List<string>))
        {
            property.SetValue(settings, SplitList(input));
        }
        else if (actual == typeof(string[]))
        {
            property.SetValue(settings, SplitList(input).ToArray());
        }
    }

    private static List<string> SplitList(string input)
        => input.Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static bool IsNumeric(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short) ||
           type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
           type == typeof(ushort) || type == typeof(double) || type == typeof(float) ||
           type == typeof(decimal);

    private static string Humanize(string pascalCase)
    {
        var chars = new List<char>();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(pascalCase[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(c);
        }

        return new string(chars.ToArray()).Trim();
    }
}
