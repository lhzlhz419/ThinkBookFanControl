using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ThinkBookFanControl;

internal sealed class OtherSettingsWindow : Window
{
    private const int BrightnessOptionCount = 4;

    private readonly Func<string, string> _t;
    private readonly Func<TimeSpan> _refreshInterval;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ComboBox _brightnessCombo = new() { Width = 128 };
    private readonly CheckBox _autoOffToggle = new()
    {
        MinWidth = 128,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 8)
    };
    private readonly Dictionary<InputSettingKind, CheckBox> _inputToggles = [];
    private KeyboardBacklightState? _currentState;
    private InputSettingsState? _inputState;
    private bool _autoOffSupported;
    private bool _loading;
    private bool _refreshing;

    public OtherSettingsWindow(
        Func<string, string> translate,
        bool isDark,
        FontFamily fontFamily,
        double fontSize,
        Func<TimeSpan> refreshInterval)
    {
        _t = translate;
        _refreshInterval = refreshInterval;
        _refreshTimer = new DispatcherTimer
        {
            Interval = CurrentRefreshInterval()
        };
        Title = _t("OtherSettings");
        Width = 580;
        Height = 390;
        MinWidth = 520;
        MinHeight = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Content = BuildLayout();
        ApplyTheme(isDark);
        _refreshTimer.Tick += async (_, _) =>
        {
            SyncRefreshTimerInterval();
            await LoadCurrentStateAsync();
        };
        Loaded += async (_, _) =>
        {
            SyncRefreshTimerInterval();
            await LoadCurrentStateAsync(showReading: true);
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private UIElement BuildLayout()
    {
        _brightnessCombo.Items.Add(_t("Auto"));
        _brightnessCombo.Items.Add(_t("Low"));
        _brightnessCombo.Items.Add(_t("High"));
        _brightnessCombo.Items.Add(_t("KeyboardBacklightOff"));
        _brightnessCombo.SelectionChanged += async (_, _) => await ChangeBrightnessAsync();

        _autoOffToggle.Click += async (_, _) => await ChangeAutoOffAsync();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var row = 0; row < 7; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddSettingRow(grid, 0, _t("KeyboardBacklightBrightness"), _brightnessCombo);
        AddSettingRow(grid, 1, _t("KeyboardBacklightAutoOff"), _autoOffToggle);
        AddInputSettingRow(grid, 2, _t("FunctionLock"), InputSettingKind.FunctionLock);
        AddInputSettingRow(grid, 3, _t("CapsLockOsd"), InputSettingKind.CapsLockOsd);
        AddInputSettingRow(grid, 4, _t("NumLockOsd"), InputSettingKind.NumLockOsd);
        AddInputSettingRow(grid, 5, _t("FnCtrlSwap"), InputSettingKind.FnCtrlSwap);
        AddInputSettingRow(grid, 6, _t("Touchpad"), InputSettingKind.Touchpad);

        var closeButton = new Button
        {
            Content = _t("Close"),
            MinWidth = 76,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => Close();

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid
        };
        Grid.SetRow(scrollViewer, 0);
        root.Children.Add(scrollViewer);
        Grid.SetRow(closeButton, 1);
        root.Children.Add(closeButton);
        return root;
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

    private void AddInputSettingRow(
        Grid grid,
        int row,
        string label,
        InputSettingKind kind)
    {
        var toggle = new CheckBox
        {
            MinWidth = 128,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        toggle.Click += async (_, _) => await ChangeInputSettingAsync(kind, toggle);
        _inputToggles.Add(kind, toggle);
        AddSettingRow(grid, row, label, toggle);
    }

    private async Task LoadCurrentStateAsync(bool showReading = false)
    {
        if (_loading || _refreshing)
            return;

        _refreshing = true;
        if (showReading)
        {
            SetBusy(true);
            SetComboStatus(_brightnessCombo, _t("ReadingSettings"));
            _autoOffToggle.Content = _t("ReadingSettings");
            _autoOffToggle.ToolTip = null;
        }

        try
        {
            try
            {
                var state = await Task.Run(KeyboardBacklightController.ReadState);
                ApplyState(state);
            }
            catch (Exception ex)
            {
                var message = string.Format(
                    _t("SettingsReadFailedFormat"),
                    ex.Message);
                SetComboStatus(_brightnessCombo, message);
                _autoOffSupported = false;
                _autoOffToggle.IsChecked = false;
                _autoOffToggle.Content = _t("ReadFailed");
                _autoOffToggle.ToolTip = message;
            }

            if (showReading)
            {
                foreach (var toggle in _inputToggles.Values)
                {
                    toggle.Content = _t("ReadingSettings");
                    toggle.ToolTip = null;
                }
            }

            try
            {
                var inputState = await Task.Run(
                    () => InputSettingsController.ReadState(
                        refreshWmiState: showReading));
                ApplyInputState(inputState);
            }
            catch (Exception ex)
            {
                var failed = ToggleSettingState.Failed(ex);
                ApplyInputState(new(failed, failed, failed, failed, failed));
            }
        }
        finally
        {
            if (showReading)
                SetBusy(false);
            _refreshing = false;
            SyncRefreshTimerInterval();
        }
    }

    private async Task ChangeInputSettingAsync(
        InputSettingKind kind,
        CheckBox toggle)
    {
        if (_loading || _refreshing || _inputState is null)
            return;

        var desired = toggle.IsChecked == true;
        var previous = _inputState.Get(kind);
        SetBusy(true);
        try
        {
            var confirmed = await Task.Run(
                () => InputSettingsController.SetState(kind, desired));
            _inputState = _inputState.With(kind, confirmed);
            ApplyToggleState(toggle, confirmed);
        }
        catch (Exception ex)
        {
            ApplyToggleState(toggle, previous);
            var message = string.Format(_t("SettingWriteFailedFormat"), ex.Message);
            MessageBox.Show(
                this,
                message,
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ChangeBrightnessAsync()
    {
        if (_loading || _refreshing)
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
        if (_loading || _refreshing || !_autoOffSupported)
            return;

        var enabled = _autoOffToggle.IsChecked == true;

        SetBusy(true);
        try
        {
            var state = await Task.Run(() => KeyboardBacklightController.SetAutoOff(enabled));
            ApplyState(state);
        }
        catch (NotSupportedException)
        {
            MarkAutoOffUnsupported();
        }
        catch (Exception ex)
        {
            HandleAutoOffWriteFailure(ex);
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
            ApplyAutoOffState(state.AutoOffEnabled, state.AutoOffStatus);
        else
            MarkAutoOffUnsupported();
        _currentState = state;
        _loading = wasLoading;
    }

    private void ApplyInputState(InputSettingsState state)
    {
        var wasLoading = _loading;
        _loading = true;
        foreach (var (kind, toggle) in _inputToggles)
            ApplyToggleState(toggle, state.Get(kind));
        _inputState = state;
        _loading = wasLoading;
    }

    private void ApplyToggleState(CheckBox toggle, ToggleSettingState state)
    {
        toggle.IsChecked = state.Supported && state.Enabled;
        toggle.Content = state.Error is not null
            ? _t("ReadFailed")
            : state.Supported
                ? _t(state.Enabled ? "On" : "Off")
                : _t("NotSupported");
        toggle.ToolTip = state.Error;
    }

    private void MarkAutoOffUnsupported()
    {
        _autoOffSupported = false;
        _autoOffToggle.IsChecked = false;
        _autoOffToggle.Content = _t("NotSupported");
        _autoOffToggle.ToolTip = null;
    }

    private void ApplyAutoOffState(bool? enabled, byte status)
    {
        _autoOffToggle.IsChecked = enabled == true;
        if (enabled.HasValue)
        {
            _autoOffToggle.Content = _t(enabled.Value ? "On" : "Off");
            _autoOffToggle.ToolTip = null;
            return;
        }

        var unknown = string.Format(_t("UnknownEcValueFormat"), status);
        _autoOffToggle.Content = unknown;
        _autoOffToggle.ToolTip = unknown;
    }

    private void HandleAutoOffWriteFailure(Exception exception)
    {
        var message = string.Format(_t("SettingWriteFailedFormat"), exception.Message);
        if (_currentState is not null)
        {
            ApplyState(_currentState);
        }
        else
        {
            _autoOffToggle.IsChecked = false;
            _autoOffToggle.Content = _t("ReadFailed");
            _autoOffToggle.ToolTip = message;
        }

        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
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
        _autoOffToggle.IsEnabled = !busy && _autoOffSupported;
        foreach (var (kind, toggle) in _inputToggles)
        {
            var state = _inputState?.Get(kind);
            toggle.IsEnabled = !busy &&
                               state is { Supported: true, Error: null };
        }
    }

    private TimeSpan CurrentRefreshInterval()
    {
        var interval = _refreshInterval();
        return interval < TimeSpan.FromMilliseconds(500)
            ? TimeSpan.FromMilliseconds(500)
            : interval;
    }

    private void SyncRefreshTimerInterval()
    {
        var interval = CurrentRefreshInterval();
        if (_refreshTimer.Interval != interval)
            _refreshTimer.Interval = interval;
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

    private void SetComboStatus(ComboBox comboBox, string text)
    {
        RemoveStatusItem(comboBox);
        comboBox.Items.Add(text);
        comboBox.SelectedIndex = comboBox.Items.Count - 1;
        comboBox.ToolTip = text;
    }

    private void RemoveStatusItem(ComboBox comboBox)
    {
        while (comboBox.Items.Count > BrightnessOptionCount)
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
        _autoOffToggle.Foreground = text;
        foreach (var toggle in _inputToggles.Values)
            toggle.Foreground = text;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

}
