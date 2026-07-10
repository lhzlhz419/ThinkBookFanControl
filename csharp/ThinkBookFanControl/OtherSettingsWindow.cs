using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
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
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly ComboBox _brightnessCombo = new() { Width = 128 };
    private readonly CheckBox _autoOffToggle = new()
    {
        MinWidth = 128,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 0, 8)
    };
    private readonly Dictionary<InputSettingKind, CheckBox> _inputToggles = [];
    private Border _warrantyCard = null!;
    private Border _warrantyStatusBadge = null!;
    private TextBlock _warrantyStatusText = null!;
    private Border _warrantyProgressTrack = null!;
    private Border _warrantyProgressFill = null!;
    private TextBlock _warrantyStartDateText = null!;
    private TextBlock _warrantyEndDateText = null!;
    private int _warrantyProgressPercentage;
    private WarrantyState _warrantyState = WarrantyState.Unavailable;
    private bool _isDark;
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
        Height = 500;
        MinWidth = 520;
        MinHeight = 450;
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
            var warrantyTask = LoadWarrantyAsync();
            await LoadCurrentStateAsync(showReading: true);
            _refreshTimer.Start();
            await warrantyTask;
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        };
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
        for (var row = 0; row < 8; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddSettingRow(grid, 0, _t("KeyboardBacklightBrightness"), _brightnessCombo);
        AddSettingRow(grid, 1, _t("KeyboardBacklightAutoOff"), _autoOffToggle);
        AddInputSettingRow(grid, 2, _t("FunctionLock"), InputSettingKind.FunctionLock);
        AddInputSettingRow(grid, 3, _t("CapsLockOsd"), InputSettingKind.CapsLockOsd);
        AddInputSettingRow(grid, 4, _t("NumLockOsd"), InputSettingKind.NumLockOsd);
        AddInputSettingRow(grid, 5, _t("FnCtrlSwap"), InputSettingKind.FnCtrlSwap);
        AddInputSettingRow(grid, 6, _t("Touchpad"), InputSettingKind.Touchpad);

        var warrantyCard = BuildWarrantyCard();
        Grid.SetRow(warrantyCard, 7);
        Grid.SetColumnSpan(warrantyCard, 2);
        grid.Children.Add(warrantyCard);

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

    private UIElement BuildWarrantyCard()
    {
        _warrantyStatusText = new TextBlock
        {
            Text = _t("WarrantyLoading"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _warrantyStatusBadge = new Border
        {
            MinWidth = 42,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(12, 12, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = _warrantyStatusText
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        var title = new TextBlock
        {
            Text = _t("WarrantyInformation"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);
        Grid.SetColumn(_warrantyStatusBadge, 1);
        header.Children.Add(_warrantyStatusBadge);

        _warrantyProgressFill = new Border
        {
            Height = 6,
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush(
                ColorFrom("#5898fd"),
                ColorFrom("#45c4ee"),
                0)
        };
        _warrantyProgressTrack = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 12, 0, 0),
            Child = _warrantyProgressFill
        };
        _warrantyProgressTrack.SizeChanged += (_, _) =>
            UpdateWarrantyProgressWidth();

        _warrantyStartDateText = CreateWarrantyDateText(
            HorizontalAlignment.Left,
            TextAlignment.Left);
        _warrantyEndDateText = CreateWarrantyDateText(
            HorizontalAlignment.Right,
            TextAlignment.Right);
        var dates = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dates.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        dates.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        Grid.SetColumn(_warrantyStartDateText, 0);
        dates.Children.Add(_warrantyStartDateText);
        Grid.SetColumn(_warrantyEndDateText, 1);
        dates.Children.Add(_warrantyEndDateText);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(_warrantyProgressTrack);
        content.Children.Add(dates);

        _warrantyCard = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
        SetWarrantyLoading();
        return _warrantyCard;
    }

    private TextBlock CreateWarrantyDateText(
        HorizontalAlignment alignment,
        TextAlignment textAlignment) => new()
        {
            Text = _t("NoInformation"),
            FontSize = 12,
            HorizontalAlignment = alignment,
            TextAlignment = textAlignment,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

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

    private async Task LoadWarrantyAsync()
    {
        SetWarrantyLoading();
        try
        {
            var snapshot = await WarrantyService.GetWarrantyAsync(
                _lifetimeCts.Token);
            if (!_lifetimeCts.IsCancellationRequested)
                ApplyWarrantySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_lifetimeCts.IsCancellationRequested)
                ApplyWarrantySnapshot(WarrantySnapshot.Unavailable(ex.Message));
        }
    }

    private void SetWarrantyLoading()
    {
        _warrantyState = WarrantyState.Unavailable;
        _warrantyStatusText.Text = _t("WarrantyLoading");
        _warrantyStartDateText.Text = _t("NoInformation");
        _warrantyEndDateText.Text = _t("NoInformation");
        _warrantyProgressPercentage = 0;
        _warrantyCard.ToolTip = null;
        UpdateWarrantyProgressWidth();
        ApplyWarrantyColors();
    }

    private void ApplyWarrantySnapshot(WarrantySnapshot snapshot)
    {
        _warrantyState = snapshot.State;
        _warrantyStatusText.Text = _t(snapshot.State switch
        {
            WarrantyState.InWarranty => "WarrantyInCoverage",
            WarrantyState.Expired => "WarrantyExpired",
            WarrantyState.NotStarted => "WarrantyNotStarted",
            _ => "WarrantyUnavailable"
        });
        _warrantyStartDateText.Text = FormatWarrantyDate(snapshot.StartDate);
        _warrantyEndDateText.Text = FormatWarrantyDate(snapshot.EndDate);
        _warrantyProgressPercentage = snapshot.ProgressPercentage;

        var tooltipParts = new List<string>();
        if (snapshot.IsStale)
            tooltipParts.Add(_t("WarrantyCached"));
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            tooltipParts.Add(string.Format(
                _t("WarrantyQueryFailedFormat"),
                snapshot.Error));
        }

        _warrantyCard.ToolTip = tooltipParts.Count == 0
            ? null
            : string.Join(Environment.NewLine, tooltipParts);
        ApplyWarrantyColors();
        UpdateWarrantyProgressWidth();
    }

    private string FormatWarrantyDate(DateOnly? date) =>
        date.HasValue
            ? date.Value.ToString(
                _t("WarrantyDateFormat"),
                CultureInfo.InvariantCulture)
            : _t("NoInformation");

    private void UpdateWarrantyProgressWidth()
    {
        if (_warrantyProgressTrack is null || _warrantyProgressFill is null)
            return;

        _warrantyProgressFill.Width = Math.Max(
            0,
            _warrantyProgressTrack.ActualWidth *
            _warrantyProgressPercentage /
            100.0);
    }

    private void ApplyWarrantyColors()
    {
        if (_warrantyCard is null)
            return;

        var cardBackground = Brush(_isDark ? "#1f2937" : "#ffffff");
        var cardBorder = Brush(_isDark ? "#374151" : "#e5e7eb");
        var secondaryText = Brush(_isDark ? "#9ca3af" : "#8c8c8c");
        var progressTrack = Brush(_isDark ? "#374151" : "#f1f1f1");
        var statusBackground = _warrantyState == WarrantyState.InWarranty
            ? Brush(_isDark ? "#334c75" : "#e8f0fe")
            : Brush(_isDark ? "#374151" : "#f3f4f6");
        var statusText = _warrantyState == WarrantyState.InWarranty
            ? Brush(_isDark ? "#8db8ff" : "#3a78f2")
            : Brush(_isDark ? "#d1d5db" : "#6b7280");

        _warrantyCard.Background = cardBackground;
        _warrantyCard.BorderBrush = cardBorder;
        _warrantyStatusBadge.Background = statusBackground;
        _warrantyStatusText.Foreground = statusText;
        _warrantyStartDateText.Foreground = secondaryText;
        _warrantyEndDateText.Foreground = secondaryText;
        _warrantyProgressTrack.Background = progressTrack;
        _warrantyProgressFill.Background = _warrantyState == WarrantyState.Expired
            ? progressTrack
            : new LinearGradientBrush(
                ColorFrom("#5898fd"),
                ColorFrom("#45c4ee"),
                0);
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
        _isDark = isDark;
        var background = Brush(isDark ? "#111827" : "#ffffff");
        var text = Brush(isDark ? "#f9fafb" : "#111827");
        Background = background;
        Foreground = text;
        _brightnessCombo.Foreground = SystemColors.ControlTextBrush;
        _autoOffToggle.Foreground = text;
        foreach (var toggle in _inputToggles.Values)
            toggle.Foreground = text;
        ApplyWarrantyColors();
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Color ColorFrom(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);

}
