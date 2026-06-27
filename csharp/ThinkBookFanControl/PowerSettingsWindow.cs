using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThinkBookFanControl;

internal sealed class PowerSettingsWindow : Window
{
    private readonly Func<string, string> _t;
    private readonly ComboBox _cpuTurboTimeLimitCombo = new()
    {
        Width = 128,
        HorizontalAlignment = HorizontalAlignment.Right
    };
    private readonly Button _okButton = new() { MinWidth = 76, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _cancelButton = new() { MinWidth = 76, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
    private readonly Button _saveButton = new() { MinWidth = 76 };
    private readonly List<IntegerSliderEditor> _sliderEditors = [];

    private Grid _settingsGrid = null!;
    private IntegerSliderEditor _cpuPl1 = null!;
    private IntegerSliderEditor _cpuPl2 = null!;
    private IntegerSliderEditor _cpuTemperatureLimit = null!;
    private IntegerSliderEditor _gpuPowerBoost = null!;
    private IntegerSliderEditor _gpuConfigurableTgp = null!;
    private IntegerSliderEditor _gpuTemperatureLimit = null!;
    private IntegerSliderEditor _gpuToCpuDynamicBoost = null!;
    private bool _hasLoadedState;

    public PowerSettingsWindow(
        Func<string, string> translate,
        bool isDark,
        FontFamily fontFamily,
        double fontSize)
    {
        _t = translate;
        Title = _t("PowerSettings");
        Width = 720;
        Height = 430;
        MinWidth = 620;
        MinHeight = 400;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Content = BuildLayout();
        ApplyTheme(isDark);
        SetBusy(false);
        Loaded += async (_, _) => await LoadCurrentStateAsync();
    }

    private UIElement BuildLayout()
    {
        _settingsGrid = new Grid();
        _settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        _settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        _cpuPl1 = AddSliderRow(0, _t("CpuPl1"), 30, 150);
        _cpuPl2 = AddSliderRow(1, _t("CpuPl2"), 50, 200);
        _cpuTemperatureLimit = AddSliderRow(2, _t("CpuTemperatureLimit"), 75, 105);
        AddComboRow(3, _t("CpuTurboTimeLimit"), _cpuTurboTimeLimitCombo);
        _gpuPowerBoost = AddSliderRow(4, _t("GpuPowerBoost"), 0, 15);
        _gpuConfigurableTgp = AddSliderRow(5, _t("GpuConfigurableTgp"), 50, 100);
        _gpuTemperatureLimit = AddSliderRow(6, _t("GpuTemperatureLimit"), 75, 87);
        _gpuToCpuDynamicBoost = AddSliderRow(7, _t("GpuToCpuDynamicBoost"), 0, 50);

        foreach (var value in PowerSettingsController.TurboTimeLimits)
            _cpuTurboTimeLimitCombo.Items.Add(value.ToString(CultureInfo.InvariantCulture));

        _okButton.Content = _t("OK");
        _cancelButton.Content = _t("Cancel");
        _saveButton.Content = _t("Save");
        _okButton.Click += async (_, _) => await SaveAsync(closeAfterSave: true);
        _cancelButton.Click += (_, _) => Close();
        _saveButton.Click += async (_, _) => await SaveAsync(closeAfterSave: false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        buttons.Children.Add(_okButton);
        buttons.Children.Add(_cancelButton);
        buttons.Children.Add(_saveButton);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _settingsGrid
        };
        Grid.SetRow(scrollViewer, 0);
        root.Children.Add(scrollViewer);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        return root;
    }

    private IntegerSliderEditor AddSliderRow(int row, string label, int minimum, int maximum)
    {
        _settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabel(row, label);

        var editor = new IntegerSliderEditor(minimum, maximum);
        _sliderEditors.Add(editor);

        Grid.SetRow(editor.Slider, row);
        Grid.SetColumn(editor.Slider, 1);
        _settingsGrid.Children.Add(editor.Slider);

        Grid.SetRow(editor.TextBox, row);
        Grid.SetColumn(editor.TextBox, 2);
        _settingsGrid.Children.Add(editor.TextBox);
        return editor;
    }

    private void AddComboRow(int row, string label, ComboBox comboBox)
    {
        _settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabel(row, label);
        comboBox.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(comboBox, row);
        Grid.SetColumn(comboBox, 1);
        Grid.SetColumnSpan(comboBox, 2);
        _settingsGrid.Children.Add(comboBox);
    }

    private void AddLabel(int row, string label)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 14, 10)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        _settingsGrid.Children.Add(text);
    }

    private async Task LoadCurrentStateAsync()
    {
        SetBusy(true);
        try
        {
            var state = await Task.Run(PowerSettingsController.ReadState);
            ApplyState(state);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                string.Format(_t("PowerSettingsReadFailedFormat"), ex.Message),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync(bool closeAfterSave)
    {
        if (!TryCollectState(out var state))
            return;

        SetBusy(true);
        try
        {
            if (closeAfterSave)
            {
                await Task.Run(() => PowerSettingsController.WriteState(state));
                DialogResult = true;
                return;
            }

            var confirmedState = await Task.Run(() => PowerSettingsController.WriteAndReadState(state));
            ApplyState(confirmedState);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                string.Format(_t("PowerSettingsWriteFailedFormat"), ex.Message),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsLoaded)
                SetBusy(false);
        }
    }

    private void ApplyState(PowerSettingsState state)
    {
        _cpuPl1.SetValue(state.CpuPl1);
        _cpuPl2.SetValue(state.CpuPl2);
        _cpuTemperatureLimit.SetValue(state.CpuTemperatureLimit);
        _cpuTurboTimeLimitCombo.SelectedItem = state.CpuTurboTimeLimit.ToString(CultureInfo.InvariantCulture);
        _gpuPowerBoost.SetValue(state.GpuPowerBoost);
        _gpuConfigurableTgp.SetValue(state.GpuConfigurableTgp);
        _gpuTemperatureLimit.SetValue(state.GpuTemperatureLimit);
        _gpuToCpuDynamicBoost.SetValue(state.GpuToCpuDynamicBoost);
        _hasLoadedState = true;
    }

    private bool TryCollectState(out PowerSettingsState state)
    {
        state = null!;
        if (!TryGetValue(_cpuPl1, "CpuPl1", out var cpuPl1) ||
            !TryGetValue(_cpuPl2, "CpuPl2", out var cpuPl2) ||
            !TryGetValue(_cpuTemperatureLimit, "CpuTemperatureLimit", out var cpuTemperatureLimit) ||
            !TryGetValue(_gpuPowerBoost, "GpuPowerBoost", out var gpuPowerBoost) ||
            !TryGetValue(_gpuConfigurableTgp, "GpuConfigurableTgp", out var gpuConfigurableTgp) ||
            !TryGetValue(_gpuTemperatureLimit, "GpuTemperatureLimit", out var gpuTemperatureLimit) ||
            !TryGetValue(_gpuToCpuDynamicBoost, "GpuToCpuDynamicBoost", out var gpuToCpuDynamicBoost))
        {
            return false;
        }

        if (_cpuTurboTimeLimitCombo.SelectedItem is not string turboText ||
            !int.TryParse(turboText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cpuTurboTimeLimit))
        {
            MessageBox.Show(this, _t("PowerSettingsTurboRequired"), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            _cpuTurboTimeLimitCombo.Focus();
            return false;
        }

        state = new PowerSettingsState(
            cpuPl1,
            cpuPl2,
            cpuTemperatureLimit,
            cpuTurboTimeLimit,
            gpuPowerBoost,
            gpuConfigurableTgp,
            gpuTemperatureLimit,
            gpuToCpuDynamicBoost);
        return true;
    }

    private bool TryGetValue(IntegerSliderEditor editor, string labelKey, out int value)
    {
        if (editor.TryGetValue(out value))
            return true;

        MessageBox.Show(
            this,
            string.Format(_t("PowerSettingRangeFormat"), _t(labelKey), editor.Minimum, editor.Maximum),
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        editor.TextBox.Focus();
        editor.TextBox.SelectAll();
        return false;
    }

    private void SetBusy(bool busy)
    {
        _settingsGrid.IsEnabled = !busy && _hasLoadedState;
        _okButton.IsEnabled = !busy && _hasLoadedState;
        _saveButton.IsEnabled = !busy && _hasLoadedState;
        _cancelButton.IsEnabled = !busy;
    }

    private void ApplyTheme(bool isDark)
    {
        var background = Brush(isDark ? "#111827" : "#ffffff");
        var text = Brush(isDark ? "#f9fafb" : "#111827");
        Background = background;
        Foreground = text;
        _cpuTurboTimeLimitCombo.Foreground = SystemColors.ControlTextBrush;
        foreach (var editor in _sliderEditors)
            editor.TextBox.Foreground = SystemColors.ControlTextBrush;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private sealed class IntegerSliderEditor
    {
        private bool _syncing;

        public IntegerSliderEditor(int minimum, int maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
            Slider = new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                SmallChange = 1,
                LargeChange = Math.Max(1, (maximum - minimum) / 10),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 10)
            };
            TextBox = new TextBox
            {
                Width = 56,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            Slider.ValueChanged += (_, args) =>
            {
                if (_syncing)
                    return;
                _syncing = true;
                TextBox.Text = ((int)Math.Round(args.NewValue)).ToString(CultureInfo.InvariantCulture);
                _syncing = false;
            };
            TextBox.TextChanged += (_, _) =>
            {
                if (_syncing ||
                    !int.TryParse(TextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
                    value < Minimum ||
                    value > Maximum)
                {
                    return;
                }

                _syncing = true;
                Slider.Value = value;
                _syncing = false;
            };
        }

        public int Minimum { get; }

        public int Maximum { get; }

        public Slider Slider { get; }

        public TextBox TextBox { get; }

        public void SetValue(int value)
        {
            if (value < Minimum || value > Maximum)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"Value must be between {Minimum} and {Maximum}.");

            _syncing = true;
            Slider.Value = value;
            TextBox.Text = value.ToString(CultureInfo.InvariantCulture);
            _syncing = false;
        }

        public bool TryGetValue(out int value)
        {
            return int.TryParse(TextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                   value >= Minimum &&
                   value <= Maximum;
        }
    }
}
