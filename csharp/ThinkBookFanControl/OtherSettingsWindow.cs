using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThinkBookFanControl;

internal sealed class OtherSettingsWindow : Window
{
    private const int BrightnessOptionCount = 4;
    private const int AutoOffOptionCount = 2;

    private readonly Func<string, string> _t;
    private readonly ComboBox _brightnessCombo = new() { Width = 128 };
    private readonly ComboBox _autoOffCombo = new() { Width = 128 };
    private KeyboardBacklightState? _currentState;
    private bool _autoOffSupported;
    private bool _loading;

    public OtherSettingsWindow(
        Func<string, string> translate,
        bool isDark,
        FontFamily fontFamily,
        double fontSize)
    {
        _t = translate;
        Title = _t("OtherSettings");
        Width = 460;
        Height = 200;
        MinWidth = 420;
        MinHeight = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Content = BuildLayout();
        ApplyTheme(isDark);
        Loaded += async (_, _) => await LoadCurrentStateAsync();
    }

    private UIElement BuildLayout()
    {
        _brightnessCombo.Items.Add(_t("Auto"));
        _brightnessCombo.Items.Add(_t("Low"));
        _brightnessCombo.Items.Add(_t("High"));
        _brightnessCombo.Items.Add(_t("KeyboardBacklightOff"));
        _brightnessCombo.SelectionChanged += async (_, _) => await ChangeBrightnessAsync();

        _autoOffCombo.Items.Add(_t("On"));
        _autoOffCombo.Items.Add(_t("Off"));
        _autoOffCombo.SelectionChanged += async (_, _) => await ChangeAutoOffAsync();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddSettingRow(grid, 0, _t("KeyboardBacklightBrightness"), _brightnessCombo);
        AddSettingRow(grid, 1, _t("KeyboardBacklightAutoOff"), _autoOffCombo);

        var closeButton = new Button
        {
            Content = _t("Close"),
            MinWidth = 76,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => Close();

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(grid);
        panel.Children.Add(closeButton);
        return panel;
    }

    private static void AddSettingRow(Grid grid, int row, string label, UIElement control)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 8)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(control);
        Grid.SetRow(container, row);
        Grid.SetColumn(container, 1);
        grid.Children.Add(container);
    }

    private async Task LoadCurrentStateAsync()
    {
        if (_loading)
            return;

        SetBusy(true);
        SetComboStatus(_brightnessCombo, _t("ReadingSettings"));
        SetComboStatus(_autoOffCombo, _t("ReadingSettings"));
        try
        {
            var state = await Task.Run(KeyboardBacklightController.ReadState);
            ApplyState(state);
        }
        catch (Exception ex)
        {
            var message = string.Format(_t("SettingsReadFailedFormat"), ex.Message);
            SetComboStatus(_brightnessCombo, message);
            SetComboStatus(_autoOffCombo, message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ChangeBrightnessAsync()
    {
        if (_loading)
            return;

        var level = _brightnessCombo.SelectedIndex switch
        {
            0 => KeyboardBacklightLevel.Auto,
            1 => KeyboardBacklightLevel.Low,
            2 => KeyboardBacklightLevel.High,
            3 => KeyboardBacklightLevel.Off,
            _ => (KeyboardBacklightLevel?)null
        };
        if (level is null)
            return;

        SetBusy(true);
        try
        {
            var state = await Task.Run(() => KeyboardBacklightController.SetBrightness(level.Value));
            ApplyState(state);
        }
        catch (Exception ex)
        {
            HandleWriteFailure(_brightnessCombo, ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ChangeAutoOffAsync()
    {
        if (_loading || !_autoOffSupported)
            return;

        var enabled = _autoOffCombo.SelectedIndex switch
        {
            0 => true,
            1 => false,
            _ => (bool?)null
        };
        if (enabled is null)
            return;

        SetBusy(true);
        try
        {
            var state = await Task.Run(() => KeyboardBacklightController.SetAutoOff(enabled.Value));
            ApplyState(state);
        }
        catch (NotSupportedException)
        {
            MarkAutoOffUnsupported();
        }
        catch (Exception ex)
        {
            HandleWriteFailure(_autoOffCombo, ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyState(KeyboardBacklightState state)
    {
        var wasLoading = _loading;
        _loading = true;
        SelectBrightness(state.Level, state.BrightnessStatus);
        _autoOffSupported = state.AutoOffSupported;
        if (_autoOffSupported)
            SelectAutoOff(state.AutoOffEnabled, state.AutoOffStatus);
        else
            SetComboStatus(_autoOffCombo, _t("NotSupported"));
        _currentState = state;
        _loading = wasLoading;
    }

    private void MarkAutoOffUnsupported()
    {
        _autoOffSupported = false;
        SetComboStatus(_autoOffCombo, _t("NotSupported"));
    }

    private void HandleWriteFailure(ComboBox comboBox, Exception exception)
    {
        var message = string.Format(_t("SettingWriteFailedFormat"), exception.Message);
        if (_currentState is not null)
            ApplyState(_currentState);
        else
            SetComboStatus(comboBox, message);

        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void SetBusy(bool busy)
    {
        _loading = busy;
        _brightnessCombo.IsEnabled = !busy;
        _autoOffCombo.IsEnabled = !busy && _autoOffSupported;
    }

    private void SelectBrightness(KeyboardBacklightLevel? level, byte status)
    {
        RemoveStatusItem(_brightnessCombo);
        var selectedIndex = level switch
        {
            KeyboardBacklightLevel.Auto => 0,
            KeyboardBacklightLevel.Low => 1,
            KeyboardBacklightLevel.High => 2,
            KeyboardBacklightLevel.Off => 3,
            _ => -1
        };

        if (selectedIndex >= 0)
        {
            _brightnessCombo.SelectedIndex = selectedIndex;
            return;
        }

        SetComboStatus(_brightnessCombo, string.Format(_t("UnknownEcValueFormat"), status));
    }

    private void SelectAutoOff(bool? enabled, byte status)
    {
        RemoveStatusItem(_autoOffCombo);
        var selectedIndex = enabled switch
        {
            true => 0,
            false => 1,
            null => -1
        };

        if (selectedIndex >= 0)
        {
            _autoOffCombo.SelectedIndex = selectedIndex;
            return;
        }

        SetComboStatus(_autoOffCombo, string.Format(_t("UnknownEcValueFormat"), status));
    }

    private void SetComboStatus(ComboBox comboBox, string text)
    {
        RemoveStatusItem(comboBox);
        comboBox.Items.Add(text);
        comboBox.SelectedIndex = comboBox.Items.Count - 1;
        comboBox.ToolTip = text;
    }

    private void RemoveStatusItem(ComboBox comboBox)
    {
        var optionCount = ReferenceEquals(comboBox, _brightnessCombo)
            ? BrightnessOptionCount
            : AutoOffOptionCount;
        while (comboBox.Items.Count > optionCount)
            comboBox.Items.RemoveAt(comboBox.Items.Count - 1);
        comboBox.ToolTip = null;
    }

    private void ApplyTheme(bool isDark)
    {
        var background = Brush(isDark ? "#111827" : "#ffffff");
        var text = Brush(isDark ? "#f9fafb" : "#111827");
        Background = background;
        Foreground = text;
        _brightnessCombo.Foreground = SystemColors.ControlTextBrush;
        _autoOffCombo.Foreground = SystemColors.ControlTextBrush;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

}
