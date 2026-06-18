using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.ComponentModel;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ThinkBookFanControl;

public sealed class MainWindow : Window
{
    private const string StartupTaskName = "ThinkBookFanControl";
    private const double HeatSoakEnterTempC = 75;
    private const double HeatSoakExitTempC = 65;
    private static readonly TimeSpan HeatSoakDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HeatSoakExitAverageDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunningFanSnapshotMinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StoppedFanSnapshotMinInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FanWriteMinInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FanWriteUrgentMinInterval = TimeSpan.FromMilliseconds(1500);
    private const int FanWriteMinDeltaRpm = 300;
    private const int FanWriteUrgentDeltaRpm = 800;

    private readonly FanController _fanController = new();
    private TemperatureReader? _temperatureReader;
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _trayMenuTimer = new();
    private readonly DispatcherTimer _fixedControlTimer = new();
    private readonly List<FanProfile> _profiles;
    private readonly AppSettings _settings;
    private readonly ItsModeDetector _itsModeDetector = new();
    private readonly GameProcessDetector _gameProcessDetector = new();

    private readonly TextBlock _cpuMetricTitle = new();
    private readonly TextBlock _gpuMetricTitle = new();
    private readonly TextBlock _vramMetricTitle = new();
    private readonly TextBlock _fan1MetricTitle = new();
    private readonly TextBlock _fan2MetricTitle = new();
    private readonly TextBlock _targetMetricTitle = new();
    private readonly TextBlock _cpuTempText = MetricValue();
    private readonly TextBlock _gpuTempText = MetricValue();
    private readonly TextBlock _vramTempText = MetricValue();
    private readonly TextBlock _fan1Text = MetricValue();
    private readonly TextBlock _fan2Text = MetricValue();
    private readonly TextBlock _targetText = MetricValue();
    private readonly TextBlock _statusText = new() { Text = "Idle", VerticalAlignment = VerticalAlignment.Center };

    private readonly ComboBox _profileCombo = new() { Width = 150 };
    private readonly TextBox _nameBox = new() { Width = 130 };
    private readonly ComboBox _intervalCombo = OptionCombo("1", "2", "5");
    private readonly ComboBox _smoothingCombo = OptionCombo("1", "2", "3", "5", "10");
    private readonly ComboBox _rampDownCombo = OptionCombo("10", "20", "50", "100", "inf");
    private readonly ComboBox _gameExitHoldCombo = OptionCombo("0", "10", "20", "30", "60");
    private readonly ComboBox _fixedModeCombo = OptionCombo("Normal", "Game");
    private readonly ComboBox _editFanCombo = OptionCombo("Fan 1", "Fan 2");
    private readonly ComboBox _languageCombo = OptionCombo("\u4e2d\u6587", "English");
    private readonly ComboBox _themeCombo = OptionCombo("Light", "Dark");
    private readonly Button _startButton = new() { Content = "Start", MinWidth = 76 };
    private readonly Button _saveButton = new() { MinWidth = 76, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _refreshButton = new() { MinWidth = 76, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _fixedModeHotkeyButton = new() { MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
    private readonly CheckBox _syncFanSpeedsCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _fixedSyncFanSpeedsCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _autoDetectGamesCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _startupCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
    private readonly CheckBox _startToTrayCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _minimizeToTrayCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox _closeToTrayCheck = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

    private readonly CurveEditor _cpuChart;
    private readonly CurveEditor _gpuChart;
    private readonly TextBlock _profileLabel = Label("");
    private readonly TextBlock _nameLabel = Label("");
    private readonly TextBlock _intervalLabel = Label("");
    private readonly TextBlock _smoothingLabel = Label("");
    private readonly TextBlock _rampDownLabel = Label("");
    private readonly TextBlock _gameExitHoldLabel = Label("");
    private readonly TextBlock _fixedModeLabel = Label("");
    private readonly TextBlock _fixedModeHotkeyLabel = Label("");
    private readonly TextBlock _editFanLabel = Label("");
    private readonly TextBlock _languageLabel = Label("");
    private readonly TextBlock _themeLabel = Label("");
    private TabControl? _strategyTabs;
    private TabItem? _fixedStrategyTab;
    private TabItem? _fanCurveStrategyTab;
    private TabItem? _cpuTab;
    private TabItem? _gpuTab;
    private Grid? _root;
    private TabControl? _curveTabs;
    private Border? _fixedRpmPanel;
    private TextBlock? _fixedRpmHeader;
    private TextBlock? _fixedRpmNote;
    private readonly Dictionary<(bool Game, int Fan), TextBlock> _fixedColumnLabels = [];
    private Border? _bottomBorder;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _trayCpuGpuItem;
    private Forms.ToolStripMenuItem? _trayVramItem;
    private Forms.ToolStripMenuItem? _trayFanItem;
    private bool _exitRequested;
    private string _lastCpuText = "--";
    private string _lastGpuText = "--";
    private string _lastVramText = "--";
    private string _lastFan1Text = "--";
    private string _lastFan2Text = "--";
    private readonly List<Border> _metricBorders = [];
    private readonly List<TextBlock> _labels = [];

    private int _profileIndex;
    private bool _loadingProfile;
    private bool _loadingSettings;
    private bool _running;
    private bool _closingAfterRestore;
    private bool _exitRestoreInProgress;
    private bool _temperatureSampling;
    private bool _fanSnapshotSampling;
    private bool _fixedControlSampling;
    private bool _fanWriteInProgress;
    private readonly SemaphoreSlim _fanIoLock = new(1, 1);
    private FanTargets? _lastTarget;
    private FanTargets? _lastAppliedTarget;
    private FanTargets? _queuedTarget;
    private ItsMode _currentItsMode = ItsMode.Unknown;
    private bool _gamesRunning;
    private bool _effectiveGameMode;
    private bool _overrideGameSeenSinceArmed;
    private bool _overrideNormalSawNoGamesSinceArmed;
    private bool _updatingFixedModeCombo;
    private bool _capturingFixedModeHotkey;
    private bool _fanCurveWarningShownThisRun;
    private bool _hasConfirmedFixedState;
    private ItsMode _confirmedItsMode = ItsMode.Unknown;
    private bool _confirmedGamesRunning;
    private ItsMode _pendingItsMode = ItsMode.Unknown;
    private bool _pendingGamesRunning;
    private bool _hasPendingFixedState;
    private bool _updatingFixedRpmBoxes;
    private DateTimeOffset? _lastGameStopTime;
    private int _fanMinRpm = 1500;
    private int _fanMaxRpm = 5500;
    private bool _fanRangeDetected;
    private double? _smoothedCpuTempC;
    private double? _smoothedGpuTempC;
    private DateTimeOffset? _lastFan1TargetTime;
    private DateTimeOffset? _lastFan2TargetTime;
    private DateTimeOffset? _lastFanSnapshotTime;
    private DateTimeOffset? _lastFanWriteTime;
    private DateTimeOffset? _highTempSince;
    private bool _heatSoaked;
    private readonly Queue<(DateTimeOffset Timestamp, double TempC)> _heatSoakExitSamples = [];
    private readonly Dictionary<ItsMode, TextBlock> _fixedModeLabels = [];
    private readonly Dictionary<(ItsMode Mode, bool Game, int Fan), TextBox> _fixedRpmBoxes = [];
    private List<int> _cpuFan1Curve;
    private List<int> _cpuFan2Curve;
    private List<int> _gpuFan1Curve;
    private List<int> _gpuFan2Curve;

    public MainWindow(bool startToTrayRequested = false)
    {
        Title = "ThinkBook Fan Control";
        Width = 1220;
        Height = 840;
        MinWidth = 820;
        MinHeight = 620;
        FontFamily = new FontFamily("Segoe UI");
        _gameExitHoldCombo.Width = 58;
        _languageCombo.Width = 72;
        _themeCombo.Width = 64;
        _settings = CurveProfileStore.LoadSettings();
        _settings.ControlStrategy = ControlStrategy.FixedRpm;
        _profiles = CurveProfileStore.Load();
        _cpuFan1Curve = [.. _profiles[0].CpuFan1Curve];
        _cpuFan2Curve = [.. _profiles[0].CpuFan2Curve];
        _gpuFan1Curve = [.. _profiles[0].GpuFan1Curve];
        _gpuFan2Curve = [.. _profiles[0].GpuFan2Curve];
        _cpuChart = new CurveEditor("CPU fan curve", CurveProfileStore.CpuTemps, _cpuFan1Curve, _cpuFan2Curve);
        _gpuChart = new CurveEditor("GPU fan curve", CurveProfileStore.GpuTemps, _gpuFan1Curve, _gpuFan2Curve);
        _cpuChart.ValuesChanged += (fan1Values, fan2Values) =>
        {
            _cpuFan1Curve = fan1Values;
            _cpuFan2Curve = fan2Values;
        };
        _gpuChart.ValuesChanged += (fan1Values, fan2Values) =>
        {
            _gpuFan1Curve = fan1Values;
            _gpuFan2Curve = fan2Values;
        };

        Content = BuildLayout();
        HookSettingsControls();
        LoadSettingsControls();
        LoadProfile(Math.Max(0, Math.Min(_profiles.Count - 1, _settings.LastProfileIndex)));
        ApplyCurveEditSettings();
        ApplyLanguage();
        ApplyTheme();
        InitializeTrayIcon();
        ApplyStartupSetting();

        SyncTimerIntervals();
        _timer.Tick += async (_, _) => await SampleAsync();
        _timer.Start();

        _trayMenuTimer.Tick += async (_, _) => await RefreshTrayMenuAsync();
        _trayMenuTimer.Start();

        _fixedControlTimer.Interval = TimeSpan.FromSeconds(2);
        _fixedControlTimer.Tick += async (_, _) => await EvaluateFixedRpmControlAsync();
        _fixedControlTimer.Start();

        StateChanged += (_, _) => OnStateChanged();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => OnClosed();

        if (startToTrayRequested && _settings.StartWithWindows && _settings.StartToTray)
            Loaded += (_, _) => HideWindowToTray();

        if (_settings.ResumeFanControlOnNextStart)
            Dispatcher.BeginInvoke(new Action(async () => await ResumeFanControlAsync()));
    }

    private UIElement BuildLayout()
    {
        var root = new Grid();
        _root = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var metrics = new UniformGrid { Columns = 6, Margin = new Thickness(12, 10, 12, 8) };
        metrics.Children.Add(Metric(_cpuMetricTitle, _cpuTempText));
        metrics.Children.Add(Metric(_gpuMetricTitle, _gpuTempText));
        metrics.Children.Add(Metric(_vramMetricTitle, _vramTempText));
        metrics.Children.Add(Metric(_fan1MetricTitle, _fan1Text));
        metrics.Children.Add(Metric(_fan2MetricTitle, _fan2Text));
        metrics.Children.Add(Metric(_targetMetricTitle, _targetText));
        Grid.SetRow(metrics, 0);
        root.Children.Add(metrics);

        var controls = BuildControls();
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);

        var strategyTabs = new TabControl { Margin = new Thickness(12, 0, 12, 8) };
        _strategyTabs = strategyTabs;
        _fixedRpmPanel = BuildFixedRpmPanel();
        _fixedStrategyTab = new TabItem { Content = _fixedRpmPanel };
        _fanCurveStrategyTab = new TabItem { Content = BuildFanCurvePanel() };
        strategyTabs.Items.Add(_fixedStrategyTab);
        strategyTabs.Items.Add(_fanCurveStrategyTab);
        strategyTabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == strategyTabs)
                OnStrategyTabChanged();
        };
        Grid.SetRow(strategyTabs, 2);
        root.Children.Add(strategyTabs);

        var bottom = new Border { Padding = new Thickness(12, 0, 12, 12), Child = _statusText };
        _bottomBorder = bottom;
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        return root;
    }

    private UIElement BuildControls()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 0, 12, 8) };

        var row1 = new WrapPanel { Orientation = Orientation.Horizontal };
        AddLabeledControl(row1, _intervalLabel, _intervalCombo);
        AddLabeledControl(row1, _languageLabel, _languageCombo);
        AddLabeledControl(row1, _themeLabel, _themeCombo);
        row1.Children.Add(_startupCheck);
        row1.Children.Add(_startToTrayCheck);
        row1.Children.Add(_minimizeToTrayCheck);
        row1.Children.Add(_closeToTrayCheck);
        panel.Children.Add(row1);

        var row3 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _saveButton.Click += (_, _) => SaveCurrentProfile(requireStopped: true);
        row3.Children.Add(_saveButton);

        _refreshButton.Click += async (_, _) => await SampleAsync(force: true);
        row3.Children.Add(_refreshButton);

        _startButton.Click += async (_, _) => await ToggleRunningAsync();
        _startButton.Margin = new Thickness(0, 0, 6, 0);
        row3.Children.Add(_startButton);
        panel.Children.Add(row3);

        return panel;
    }

    private UIElement BuildFanCurvePanel()
    {
        var panel = new DockPanel();

        var controls = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 6) };
        _profileCombo.ItemsSource = ProfileLabels();
        _profileCombo.SelectionChanged += (_, _) =>
        {
            if (!_loadingProfile && _profileCombo.SelectedIndex >= 0)
                ChangeProfile(_profileCombo.SelectedIndex);
        };
        AddLabeledControl(controls, _profileLabel, _profileCombo);
        AddLabeledControl(controls, _nameLabel, _nameBox);
        AddLabeledControl(controls, _editFanLabel, _editFanCombo);
        AddStandaloneControl(controls, _syncFanSpeedsCheck);
        AddLabeledControl(controls, _smoothingLabel, _smoothingCombo);
        AddLabeledControl(controls, _rampDownLabel, _rampDownCombo);
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);

        var tabs = new TabControl { Margin = new Thickness(8, 0, 8, 8) };
        _curveTabs = tabs;
        _cpuTab = new TabItem { Content = _cpuChart };
        _gpuTab = new TabItem { Content = _gpuChart };
        tabs.Items.Add(_cpuTab);
        tabs.Items.Add(_gpuTab);
        panel.Children.Add(tabs);
        return panel;
    }

    private Border BuildFixedRpmPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 12, 12, 8), HorizontalAlignment = HorizontalAlignment.Center };
        _fixedRpmHeader = new TextBlock
        {
            Text = "Fixed RPM",
            FontWeight = FontWeights.SemiBold,
            FontSize = 18,
            Margin = new Thickness(0, 0, 0, 10),
            TextAlignment = TextAlignment.Center
        };
        _labels.Add(_fixedRpmHeader);
        panel.Children.Add(_fixedRpmHeader);

        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AddLabeledControl(options, _gameExitHoldLabel, _gameExitHoldCombo);
        AddStandaloneControl(options, _fixedSyncFanSpeedsCheck);
        AddStandaloneControl(options, _autoDetectGamesCheck);
        AddLabeledControl(options, _fixedModeLabel, _fixedModeCombo);
        AddLabeledControl(options, _fixedModeHotkeyLabel, _fixedModeHotkeyButton);
        panel.Children.Add(options);

        var grid = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });

        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddFixedText(grid, "", 0, 0, true);
        _fixedColumnLabels[(false, 1)] = AddFixedText(grid, "Normal F1", 0, 1, true);
        _fixedColumnLabels[(false, 2)] = AddFixedText(grid, "Normal F2", 0, 2, true);
        _fixedColumnLabels[(true, 1)] = AddFixedText(grid, "Game F1", 0, 3, true);
        _fixedColumnLabels[(true, 2)] = AddFixedText(grid, "Game F2", 0, 4, true);

        AddFixedRpmRow(grid, 1, ItsMode.PowerSaving, "Power saving");
        AddFixedRpmRow(grid, 2, ItsMode.Intelligent, "Intelligent");
        AddFixedRpmRow(grid, 3, ItsMode.Performance, "Performance");
        AddFixedRpmRow(grid, 4, ItsMode.Geek, "Geek");

        panel.Children.Add(grid);
        _fixedRpmNote = new TextBlock
        {
            Text = "0 = firmware auto. Non-zero RPM values are clamped to the detected fan range.",
            FontSize = 15,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _labels.Add(_fixedRpmNote);
        panel.Children.Add(_fixedRpmNote);

        return new Border
        {
            Margin = new Thickness(12, 0, 12, 8),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = panel
        };
    }

    private void AddFixedRpmRow(Grid grid, int row, ItsMode mode, string label)
    {
        _fixedModeLabels[mode] = AddFixedText(grid, label, row, 0, false);
        AddFixedRpmBox(grid, row, 1, mode, false, 1);
        AddFixedRpmBox(grid, row, 2, mode, false, 2);
        AddFixedRpmBox(grid, row, 3, mode, true, 1);
        AddFixedRpmBox(grid, row, 4, mode, true, 2);
    }

    private TextBlock AddFixedText(Grid grid, string text, int row, int column, bool bold)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        _labels.Add(block);
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }

    private void AddFixedRpmBox(Grid grid, int row, int column, ItsMode mode, bool game, int fan)
    {
        var box = new TextBox
        {
            Width = 108,
            FontSize = 17,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 8),
            Text = GetFixedRpmValue(mode, game, fan).ToString(CultureInfo.InvariantCulture)
        };
        box.TextChanged += (_, _) => SyncFixedRpmTextBox(mode, game, fan, box.Text);
        box.LostFocus += (_, _) => SaveFixedRpmSettingsFromUi();
        box.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter)
            {
                SaveFixedRpmSettingsFromUi();
                args.Handled = true;
            }
        };
        _fixedRpmBoxes[(mode, game, fan)] = box;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, column);
        grid.Children.Add(box);
    }

    private async Task SampleAsync(bool force = false)
    {
        if (_temperatureSampling)
            return;

        var profile = UiToProfile();
        SyncTimerIntervals();

        _temperatureSampling = true;
        try
        {
            var temps = await Task.Run(ReadTemperatures);
            _ = RefreshFanSnapshotAsync(force);
            UpdateHeatSoak(temps);

            _smoothedCpuTempC = SmoothTemperature(_smoothedCpuTempC, temps.CpuTempC, profile.TemperatureSmoothing);
            _smoothedGpuTempC = SmoothTemperature(_smoothedGpuTempC, temps.GpuTempC, profile.TemperatureSmoothing);

            if (_settings.ControlStrategy == ControlStrategy.FixedRpm)
            {
                if (!_running)
                    ResetFanTargetState();

                UpdateTemperatureUi(temps);
                _targetText.Text = FormatTarget(_lastTarget);
                _cpuChart.SetCurrentTemp(temps.CpuTempC);
                _gpuChart.SetCurrentTemp(temps.GpuTempC);
                _statusText.Text = $"{(_running ? T("Running") : T("Monitoring"))} | {T("Strategy")}: {T("FixedRpm")} | {T("CurrentMode")}: {T(ModeKey(_currentItsMode))} | {T("Game")}: {FormatGameState()}";
                UpdateTrayText();
                return;
            }

            var cpuFan1Target = CurveProfileStore.Interpolate(CurveProfileStore.CpuTemps, _cpuFan1Curve, _smoothedCpuTempC);
            var gpuFan1Target = CurveProfileStore.Interpolate(CurveProfileStore.GpuTemps, _gpuFan1Curve, _smoothedGpuTempC);
            var cpuFan2Target = CurveProfileStore.Interpolate(CurveProfileStore.CpuTemps, _cpuFan2Curve, _smoothedCpuTempC);
            var gpuFan2Target = CurveProfileStore.Interpolate(CurveProfileStore.GpuTemps, _gpuFan2Curve, _smoothedGpuTempC);
            var rawTarget = new FanTargets(
                ClampForCurrentRange(Math.Max(cpuFan1Target, gpuFan1Target)),
                ClampForCurrentRange(Math.Max(cpuFan2Target, gpuFan2Target)));
            var target = ApplyRampDown(rawTarget, profile.RampDownRpmPerSecond);

            if (_running)
            {
                var now = DateTimeOffset.Now;
                if (target != _lastTarget)
                {
                    var previousTarget = _lastTarget;
                    _lastTarget = target;
                    if (previousTarget is null || target.Fan1Rpm != previousTarget.Fan1Rpm)
                        _lastFan1TargetTime = now;
                    if (previousTarget is null || target.Fan2Rpm != previousTarget.Fan2Rpm)
                        _lastFan2TargetTime = now;
                }

                if (target != _lastAppliedTarget && ShouldQueueFanTarget(target, now))
                    QueueTargetApply(target);
            }
            if (!_running)
            {
                ResetFanTargetState();
            }

            UpdateTemperatureUi(temps);
            _targetText.Text = FormatTarget(target);
            _cpuChart.SetCurrentTemp(temps.CpuTempC);
            _gpuChart.SetCurrentTemp(temps.GpuTempC);
            _statusText.Text = $"{(_running ? T("Running") : T("Monitoring"))} | {T("HeatSoak")}: {(_heatSoaked ? T("On") : T("Off"))} | CPU: {temps.CpuSensor} | GPU: {temps.GpuSensor} | VRAM: {temps.VramSensor}";
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            if (_running)
            {
                _running = false;
                _startButton.Content = T("Start");
                _queuedTarget = null;
                try
                {
                    await RestoreAutoWithLockAsync();
                    SetResumeFanControlOnNextStart(false);
                }
                catch { }
            }
            _statusText.Text = T("MonitorError") + ": " + ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            _temperatureSampling = false;
        }
    }

    private async Task RefreshFanSnapshotAsync(bool force = false)
    {
        if (_fanSnapshotSampling)
            return;

        var now = DateTimeOffset.Now;
        var minInterval = _running ? RunningFanSnapshotMinInterval : StoppedFanSnapshotMinInterval;
        if (!force && _lastFanSnapshotTime is DateTimeOffset lastSnapshotTime && now - lastSnapshotTime < minInterval)
            return;

        _lastFanSnapshotTime = now;
        _fanSnapshotSampling = true;
        try
        {
            await _fanIoLock.WaitAsync();
            FanSnapshot fans;
            try
            {
                fans = await Task.Run(() => _fanController.ReadSnapshot());
            }
            finally
            {
                _fanIoLock.Release();
            }

            UpdateFanRange(fans.Limits);
            _fan1Text.Text = $"{fans.Fan1Rpm} RPM";
            _fan2Text.Text = $"{fans.Fan2Rpm} RPM";
            _lastFan1Text = _fan1Text.Text;
            _lastFan2Text = _fan2Text.Text;
            UpdateTrayMenuMetrics();
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            _statusText.Text = T("FanReadError") + ": " + ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            _fanSnapshotSampling = false;
        }
    }

    private int ClampForCurrentRange(int rpm)
    {
        return Math.Max(_fanMinRpm, Math.Min(_fanMaxRpm, rpm));
    }

    private void QueueTargetApply(FanTargets target)
    {
        _queuedTarget = target;
        if (!_fanWriteInProgress)
            _ = ApplyQueuedTargetsAsync();
    }

    private bool ShouldQueueFanTarget(FanTargets target, DateTimeOffset now)
    {
        if (_lastAppliedTarget is not FanTargets appliedTarget)
            return true;

        var fan1Delta = target.Fan1Rpm - appliedTarget.Fan1Rpm;
        var fan2Delta = target.Fan2Rpm - appliedTarget.Fan2Rpm;
        var maxIncrease = Math.Max(fan1Delta, fan2Delta);
        var maxDelta = Math.Max(Math.Abs(fan1Delta), Math.Abs(fan2Delta));
        var minInterval = maxIncrease >= FanWriteUrgentDeltaRpm ? FanWriteUrgentMinInterval : FanWriteMinInterval;

        if (maxDelta < FanWriteMinDeltaRpm && _lastFanWriteTime is DateTimeOffset lastSmallWriteTime && now - lastSmallWriteTime < FanWriteMinInterval)
            return false;

        return _lastFanWriteTime is not DateTimeOffset lastFanWriteTime || now - lastFanWriteTime >= minInterval;
    }

    private async Task ApplyQueuedTargetsAsync()
    {
        _fanWriteInProgress = true;
        try
        {
            while (_running && _queuedTarget is FanTargets target)
            {
                _queuedTarget = null;
                if (_lastFanWriteTime is DateTimeOffset lastFanWriteTime)
                {
                    var delay = FanWriteMinInterval - (DateTimeOffset.Now - lastFanWriteTime);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay);
                    if (!_running)
                        break;
                }

                if (_lastTarget is FanTargets latestTarget)
                    target = latestTarget;

                await _fanIoLock.WaitAsync();
                try
                {
                    await Task.Run(() => _fanController.Apply(target.Fan1Rpm, target.Fan2Rpm));
                    _lastAppliedTarget = target;
                    _lastFanWriteTime = DateTimeOffset.Now;
                }
                finally
                {
                    _fanIoLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            _running = false;
            _queuedTarget = null;
            _lastTarget = null;
            _lastAppliedTarget = null;
            _lastFan1TargetTime = null;
            _lastFan2TargetTime = null;
            _startButton.Content = T("Start");
            _statusText.Text = T("FanWriteError") + ": " + ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            _fanWriteInProgress = false;
            if (_running && _queuedTarget is not null)
                _ = ApplyQueuedTargetsAsync();
        }
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindowFromTray();
        _trayMenu.Opening += (_, _) => UpdateTrayMenu();
        UpdateTrayMenu();
        UpdateTrayText();
    }

    private async Task RefreshTrayMenuAsync()
    {
        if (_trayMenu?.Visible == true)
            await SampleAsync();

        UpdateTrayMenuMetrics();
        UpdateTrayText();
    }

    private void UpdateTrayMenu()
    {
        if (_trayMenu is null)
            return;

        _trayMenu.Items.Clear();
        _trayCpuGpuItem = new Forms.ToolStripMenuItem { Enabled = false };
        _trayVramItem = new Forms.ToolStripMenuItem { Enabled = false };
        _trayFanItem = new Forms.ToolStripMenuItem { Enabled = false };
        _trayMenu.Items.Add(_trayCpuGpuItem);
        _trayMenu.Items.Add(_trayVramItem);
        _trayMenu.Items.Add(_trayFanItem);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var toggleItem = new Forms.ToolStripMenuItem(_running ? T("Stop") : T("Start"));
        toggleItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ToggleRunningAsync()));
        _trayMenu.Items.Add(toggleItem);

        var profilesMenu = new Forms.ToolStripMenuItem(T("Profile"));
        for (var i = 0; i < _profiles.Count; i++)
        {
            var index = i;
            var item = new Forms.ToolStripMenuItem($"{i + 1}: {_profiles[i].Name}") { Checked = i == _profileIndex };
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => ChangeProfile(index)));
            profilesMenu.DropDownItems.Add(item);
        }
        _trayMenu.Items.Add(profilesMenu);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var showItem = new Forms.ToolStripMenuItem(T("ShowWindow"));
        showItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowWindowFromTray));
        _trayMenu.Items.Add(showItem);

        var exitItem = new Forms.ToolStripMenuItem(T("Exit"));
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            _exitRequested = true;
            Close();
        }));
        _trayMenu.Items.Add(exitItem);
        UpdateTrayMenuMetrics();
    }

    private void UpdateTrayText()
    {
        if (_trayIcon is null)
            return;

        var text = $"CPU {_lastCpuText} GPU {_lastGpuText} F1 {_lastFan1Text} F2 {_lastFan2Text}";
        _trayIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void UpdateTrayMenuMetrics()
    {
        if (_trayCpuGpuItem is not null)
            _trayCpuGpuItem.Text = $"CPU: {_lastCpuText}   GPU: {_lastGpuText}";
        if (_trayVramItem is not null)
            _trayVramItem.Text = $"VRAM: {_lastVramText}";
        if (_trayFanItem is not null)
            _trayFanItem.Text = $"{T("Fan1")}: {_lastFan1Text}   {T("Fan2")}: {_lastFan2Text}";
    }

    private void ShowWindowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideWindowToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void OnStateChanged()
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            HideWindowToTray();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_settings.CloseToTray && !_exitRequested)
        {
            e.Cancel = true;
            HideWindowToTray();
            return;
        }

        if (_closingAfterRestore || !_running)
            return;

        e.Cancel = true;
        if (_exitRestoreInProgress)
            return;

        _exitRestoreInProgress = true;
        _exitRequested = true;
        _startButton.IsEnabled = false;
        _startButton.Content = T("Stopping");
        _statusText.Text = T("RestoringAuto");
        try
        {
            await RestoreAutoForExitAsync();
            _closingAfterRestore = true;
            Close();
        }
        catch (Exception ex)
        {
            _running = true;
            _exitRequested = false;
            _exitRestoreInProgress = false;
            _startButton.IsEnabled = true;
            _startButton.Content = T("Stop");
            MessageBox.Show(this, ex.Message, T("RestoreAutoFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyStartupSetting()
    {
        try
        {
            DeleteLegacyStartupRunEntry();

            if (_settings.StartWithWindows)
                CreateStartupTask(_settings.StartToTray);
            else
                DeleteStartupTask();
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private static void DeleteLegacyStartupRunEntry()
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        key?.DeleteValue(StartupTaskName, throwOnMissingValue: false);
    }

    private static void CreateStartupTask(bool startToTray)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Cannot determine executable path for startup task.");

        var xmlPath = Path.Combine(Path.GetTempPath(), StartupTaskName + ".xml");
        try
        {
            File.WriteAllText(xmlPath, BuildStartupTaskXml(executablePath, startToTray), Encoding.Unicode);
            RunSchtasks(false, "/Create", "/TN", StartupTaskName, "/XML", xmlPath, "/F");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static void DeleteStartupTask()
    {
        RunSchtasks(true, "/Delete", "/TN", StartupTaskName, "/F");
    }

    private static string BuildStartupTaskXml(string executablePath, bool startToTray)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new InvalidOperationException("Cannot determine current user SID for startup task.");

        var escapedSid = SecurityElement.Escape(sid);
        var escapedPath = SecurityElement.Escape(executablePath);
        var arguments = startToTray ? "      <Arguments>--startup-tray</Arguments>\r\n" : "";
        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>{escapedSid}</Author>
    <Description>Start ThinkBook Fan Control at user logon.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escapedSid}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{escapedSid}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{escapedPath}</Command>
{arguments}    </Exec>
  </Actions>
</Task>
""";
    }

    private static void RunSchtasks(bool ignoreFailure, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start schtasks.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"schtasks.exe failed with exit code {process.ExitCode}. {output} {error}".Trim());
    }

    private void UpdateFanRange(IReadOnlyDictionary<string, FanLimit> limits)
    {
        var (minimum, maximum) = FanController.SharedRange(limits);
        if (_fanRangeDetected && minimum == _fanMinRpm && maximum == _fanMaxRpm)
            return;

        _fanRangeDetected = true;
        _fanMinRpm = minimum;
        _fanMaxRpm = maximum;
        _cpuFan1Curve = CurveProfileStore.ClampCurve(_cpuFan1Curve, minimum, maximum);
        _cpuFan2Curve = CurveProfileStore.ClampCurve(_cpuFan2Curve, minimum, maximum);
        _gpuFan1Curve = CurveProfileStore.ClampCurve(_gpuFan1Curve, minimum, maximum);
        _gpuFan2Curve = CurveProfileStore.ClampCurve(_gpuFan2Curve, minimum, maximum);
        _cpuChart.SetRpmRange(minimum, maximum);
        _gpuChart.SetRpmRange(minimum, maximum);
        _cpuChart.SetValues(_cpuFan1Curve, _cpuFan2Curve);
        _gpuChart.SetValues(_gpuFan1Curve, _gpuFan2Curve);
        _settings.FixedRpm = CurveProfileStore.NormalizeFixedRpmSettings(_settings.FixedRpm, minimum, maximum);
        RefreshFixedRpmBoxes();
        CurveProfileStore.SaveSettings(_settings);
    }

    private void ChangeProfile(int index)
    {
        if (_running)
        {
            MessageBox.Show(this, T("StopBeforeSwitch"), T("FanCurve"), MessageBoxButton.OK, MessageBoxImage.Information);
            _profileCombo.SelectedIndex = _profileIndex;
            return;
        }

        LoadProfile(index);
        _settings.LastProfileIndex = index;
        CurveProfileStore.SaveSettings(_settings);
    }

    private void LoadProfile(int index)
    {
        _loadingProfile = true;
        _profileIndex = index;
        var profile = _profiles[index];
        _profileCombo.SelectedIndex = index;
        _nameBox.Text = profile.Name;
        SelectComboValue(_smoothingCombo, profile.TemperatureSmoothing);
        SelectComboValue(_rampDownCombo, profile.RampDownRpmPerSecond);
        _cpuFan1Curve = _fanRangeDetected ? CurveProfileStore.ClampCurve(profile.CpuFan1Curve, _fanMinRpm, _fanMaxRpm) : [.. profile.CpuFan1Curve];
        _cpuFan2Curve = _fanRangeDetected ? CurveProfileStore.ClampCurve(profile.CpuFan2Curve, _fanMinRpm, _fanMaxRpm) : [.. profile.CpuFan2Curve];
        _gpuFan1Curve = _fanRangeDetected ? CurveProfileStore.ClampCurve(profile.GpuFan1Curve, _fanMinRpm, _fanMaxRpm) : [.. profile.GpuFan1Curve];
        _gpuFan2Curve = _fanRangeDetected ? CurveProfileStore.ClampCurve(profile.GpuFan2Curve, _fanMinRpm, _fanMaxRpm) : [.. profile.GpuFan2Curve];
        _cpuChart.SetValues(_cpuFan1Curve, _cpuFan2Curve);
        _gpuChart.SetValues(_gpuFan1Curve, _gpuFan2Curve);
        _loadingProfile = false;
        UpdateTrayMenu();
    }

    private FanProfile UiToProfile()
    {
        return new FanProfile
        {
            Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? $"Profile {_profileIndex + 1}" : _nameBox.Text.Trim(),
            TemperatureSmoothing = SelectedNumber(_smoothingCombo, 3),
            RampDownRpmPerSecond = SelectedRampDown(),
            CpuFan1Curve = [.. _cpuFan1Curve],
            CpuFan2Curve = [.. _cpuFan2Curve],
            GpuFan1Curve = [.. _gpuFan1Curve],
            GpuFan2Curve = [.. _gpuFan2Curve],
            CpuCurve = [.. _cpuFan1Curve],
            GpuCurve = [.. _gpuFan1Curve],
        };
    }

    private bool SaveCurrentProfile(bool requireStopped = true)
    {
        if (requireStopped && _running)
        {
            ShowStopFirstWarning();
            return false;
        }

        SaveFixedRpmSettingsFromUi();
        _profiles[_profileIndex] = UiToProfile();
        CurveProfileStore.Save(_profiles);
        CurveProfileStore.SaveSettings(_settings);
        _profileCombo.ItemsSource = ProfileLabels();
        _profileCombo.SelectedIndex = _profileIndex;
        _statusText.Text = T("Saved") + " " + CurveProfileStore.ProfilePath;
        UpdateTrayMenu();
        return true;
    }

    private async Task ToggleRunningAsync()
    {
        if (_running)
        {
            await RestoreAutoAsync();
            return;
        }

        SaveCurrentProfile(requireStopped: false);
        StartFanControl();
    }

    private async Task ResumeFanControlAsync()
    {
        if (_running)
            return;

        StartFanControl();
        _statusText.Text = T("ControllerResumed");
        await SampleAsync(force: true);
    }

    private void StartFanControl()
    {
        _running = true;
        SetResumeFanControlOnNextStart(true);
        _lastTarget = null;
        _lastAppliedTarget = null;
        _lastFan1TargetTime = null;
        _lastFan2TargetTime = null;
        _lastFanWriteTime = null;
        _smoothedCpuTempC = null;
        _smoothedGpuTempC = null;
        _highTempSince = null;
        _heatSoaked = false;
        _heatSoakExitSamples.Clear();
        _currentItsMode = ItsMode.Unknown;
        _gamesRunning = false;
        _effectiveGameMode = _settings.FixedGameModeOverride == FixedGameModeOverride.GameUntilGamesEnd;
        _overrideGameSeenSinceArmed = false;
        _overrideNormalSawNoGamesSinceArmed = false;
        _hasConfirmedFixedState = false;
        _hasPendingFixedState = false;
        _lastGameStopTime = null;
        UpdateFixedModeCombo();
        _startButton.Content = T("Stop");
        _statusText.Text = T("ControllerEnabled");
        UpdateTrayMenu();
    }

    private async Task RestoreAutoAsync()
    {
        _running = false;
        _lastTarget = null;
        _lastAppliedTarget = null;
        _lastFan1TargetTime = null;
        _lastFan2TargetTime = null;
        _queuedTarget = null;
        _gamesRunning = false;
        _effectiveGameMode = false;
        _settings.FixedGameModeOverride = FixedGameModeOverride.None;
        _overrideGameSeenSinceArmed = false;
        _overrideNormalSawNoGamesSinceArmed = false;
        UpdateFixedModeCombo();
        _lastGameStopTime = null;
        _startButton.IsEnabled = false;
        _startButton.Content = T("Stopping");
        _statusText.Text = T("RestoringAuto");
        try
        {
            await RestoreAutoWithLockAsync();
            SetResumeFanControlOnNextStart(false);
            _startButton.Content = T("Start");
            _statusText.Text = T("AutoRestored");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("RestoreAutoFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            _startButton.Content = T("Start");
        }
        finally
        {
            _startButton.IsEnabled = true;
            UpdateTrayMenu();
        }
    }

    private void OnClosed()
    {
        _timer.Stop();
        _trayMenuTimer.Stop();
        _fixedControlTimer.Stop();
        if (_running)
        {
            try
            {
                _fanIoLock.Wait();
                try
                {
                    _fanController.RestoreAuto();
                }
                finally { _fanIoLock.Release(); }
            }
            catch { }
        }
        _temperatureReader?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
    }

    private async Task RestoreAutoWithLockAsync()
    {
        await _fanIoLock.WaitAsync();
        try
        {
            await Task.Run(() => _fanController.RestoreAuto());
        }
        finally
        {
            _fanIoLock.Release();
        }
    }

    private async Task RestoreAutoForExitAsync()
    {
        _running = false;
        _queuedTarget = null;
        _lastTarget = null;
        _lastAppliedTarget = null;
        _lastFan1TargetTime = null;
        _lastFan2TargetTime = null;
        _gamesRunning = false;
        _effectiveGameMode = false;
        _lastGameStopTime = null;
        await RestoreAutoWithLockAsync();
    }

    private async Task EvaluateFixedRpmControlAsync()
    {
        if (_fixedControlSampling || !_running || _settings.ControlStrategy != ControlStrategy.FixedRpm)
            return;

        _fixedControlSampling = true;
        try
        {
            var modeTask = Task.Run(() => _itsModeDetector.ReadMode());
            var gameTask = Task.Run(() => _gameProcessDetector.AreGamesRunning());
            var mode = await modeTask;
            var gamesRunning = await gameTask;

            if (!UpdateConfirmedFixedState(mode, gamesRunning))
            {
                _statusText.Text = $"{T("Running")} | {T("PendingChange")} | {T("CurrentMode")}: {T(ModeKey(_currentItsMode))} | {T("Game")}: {FormatGameState()}";
                return;
            }

            var target = GetFixedTarget(_currentItsMode, _effectiveGameMode);
            _lastTarget = target;
            _targetText.Text = FormatTarget(target);
            _statusText.Text = $"{T("Running")} | {T("Strategy")}: {T("FixedRpm")} | {T("CurrentMode")}: {T(ModeKey(_currentItsMode))} | {T("Game")}: {FormatGameState()}";

            await ApplyFixedTargetAsync(target);
        }
        catch (Exception ex)
        {
            _running = false;
            ResetFanTargetState();
            _startButton.Content = T("Start");
            _statusText.Text = T("FanWriteError") + ": " + ex.GetType().Name + ": " + ex.Message;
            UpdateTrayMenu();
        }
        finally
        {
            _fixedControlSampling = false;
        }
    }

    private bool UpdateConfirmedFixedState(ItsMode sampledMode, bool sampledGamesRunning)
    {
        sampledGamesRunning = _settings.AutoDetectGames && sampledGamesRunning;

        if (!_hasConfirmedFixedState)
        {
            ConfirmFixedState(sampledMode, sampledGamesRunning);
            return true;
        }

        if (sampledMode == _confirmedItsMode && sampledGamesRunning == _confirmedGamesRunning)
        {
            _hasPendingFixedState = false;
            UpdateGameState(sampledGamesRunning);
            return true;
        }

        if (_hasPendingFixedState &&
            sampledMode == _pendingItsMode &&
            sampledGamesRunning == _pendingGamesRunning)
        {
            ConfirmFixedState(sampledMode, sampledGamesRunning);
            return true;
        }

        _pendingItsMode = sampledMode;
        _pendingGamesRunning = sampledGamesRunning;
        _hasPendingFixedState = true;
        return false;
    }

    private void ConfirmFixedState(ItsMode mode, bool gamesRunning)
    {
        _confirmedItsMode = mode;
        _confirmedGamesRunning = gamesRunning;
        _hasConfirmedFixedState = true;
        _hasPendingFixedState = false;
        _currentItsMode = mode;
        UpdateGameState(gamesRunning);
    }

    private void UpdateGameState(bool gamesRunning)
    {
        var now = DateTimeOffset.Now;
        if (gamesRunning)
        {
            _gamesRunning = true;
            if (_settings.FixedGameModeOverride == FixedGameModeOverride.GameUntilGamesEnd)
                _overrideGameSeenSinceArmed = true;
            if (_settings.FixedGameModeOverride == FixedGameModeOverride.NormalUntilGameStarts)
            {
                if (_overrideNormalSawNoGamesSinceArmed)
                {
                    _settings.FixedGameModeOverride = FixedGameModeOverride.None;
                    _settings.ManualGameMode = false;
                    CurveProfileStore.SaveSettings(_settings);
                }
                else
                {
                    _effectiveGameMode = false;
                    UpdateFixedModeCombo();
                    return;
                }
            }
            _effectiveGameMode = true;
            _lastGameStopTime = null;
            UpdateFixedModeCombo();
            return;
        }

        if (_gamesRunning)
            _lastGameStopTime = now;

        _gamesRunning = false;
        if (_settings.FixedGameModeOverride == FixedGameModeOverride.GameUntilGamesEnd && !_overrideGameSeenSinceArmed)
        {
            _effectiveGameMode = true;
            UpdateFixedModeCombo();
            return;
        }

        if (_settings.FixedGameModeOverride == FixedGameModeOverride.GameUntilGamesEnd && _overrideGameSeenSinceArmed)
        {
            _settings.FixedGameModeOverride = FixedGameModeOverride.None;
            _settings.ManualGameMode = false;
            _overrideGameSeenSinceArmed = false;
            CurveProfileStore.SaveSettings(_settings);
        }

        if (_settings.FixedGameModeOverride == FixedGameModeOverride.NormalUntilGameStarts)
        {
            _overrideNormalSawNoGamesSinceArmed = true;
            _effectiveGameMode = false;
            _lastGameStopTime = null;
            UpdateFixedModeCombo();
            return;
        }

        var holdSeconds = Math.Max(0, _settings.GameExitHoldSeconds);
        _effectiveGameMode = _lastGameStopTime is DateTimeOffset stoppedAt &&
                             now - stoppedAt < TimeSpan.FromSeconds(holdSeconds);
        UpdateFixedModeCombo();
    }

    private FanTargets GetFixedTarget(ItsMode mode, bool game)
    {
        var effectiveMode = mode == ItsMode.Unknown ? ItsMode.Intelligent : mode;
        return new FanTargets(
            CurveProfileStore.ClampFixedRpm(GetFixedRpmValue(effectiveMode, game, 1), _fanMinRpm, _fanMaxRpm),
            CurveProfileStore.ClampFixedRpm(GetFixedRpmValue(effectiveMode, game, 2), _fanMinRpm, _fanMaxRpm));
    }

    private async Task ApplyFixedTargetAsync(FanTargets target)
    {
        if (target == _lastAppliedTarget)
            return;

        await _fanIoLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (target.Fan1Rpm == 0 && target.Fan2Rpm == 0)
                    _fanController.RestoreAuto();
                else
                    _fanController.Apply(target.Fan1Rpm, target.Fan2Rpm);
            });
            _lastAppliedTarget = target;
            _lastFanWriteTime = DateTimeOffset.Now;
        }
        finally
        {
            _fanIoLock.Release();
        }
    }

    private int GetFixedRpmValue(ItsMode mode, bool game, int fan)
    {
        var settings = _settings.FixedRpm;
        return (mode, game, fan) switch
        {
            (ItsMode.PowerSaving, false, 1) => settings.PowerSavingNormalFan1Rpm,
            (ItsMode.PowerSaving, false, 2) => settings.PowerSavingNormalFan2Rpm,
            (ItsMode.PowerSaving, true, 1) => settings.PowerSavingGameFan1Rpm,
            (ItsMode.PowerSaving, true, 2) => settings.PowerSavingGameFan2Rpm,
            (ItsMode.Intelligent, false, 1) => settings.IntelligentNormalFan1Rpm,
            (ItsMode.Intelligent, false, 2) => settings.IntelligentNormalFan2Rpm,
            (ItsMode.Intelligent, true, 1) => settings.IntelligentGameFan1Rpm,
            (ItsMode.Intelligent, true, 2) => settings.IntelligentGameFan2Rpm,
            (ItsMode.Performance, false, 1) => settings.PerformanceNormalFan1Rpm,
            (ItsMode.Performance, false, 2) => settings.PerformanceNormalFan2Rpm,
            (ItsMode.Performance, true, 1) => settings.PerformanceGameFan1Rpm,
            (ItsMode.Performance, true, 2) => settings.PerformanceGameFan2Rpm,
            (ItsMode.Geek, false, 1) => settings.GeekNormalFan1Rpm,
            (ItsMode.Geek, false, 2) => settings.GeekNormalFan2Rpm,
            (ItsMode.Geek, true, 1) => settings.GeekGameFan1Rpm,
            (ItsMode.Geek, true, 2) => settings.GeekGameFan2Rpm,
            _ => GetFixedRpmValue(ItsMode.Intelligent, game, fan)
        };
    }

    private void SetFixedRpmValue(FixedRpmSettings settings, ItsMode mode, bool game, int fan, int value)
    {
        switch (mode, game, fan)
        {
            case (ItsMode.PowerSaving, false, 1):
                settings.PowerSavingNormalFan1Rpm = value;
                break;
            case (ItsMode.PowerSaving, false, 2):
                settings.PowerSavingNormalFan2Rpm = value;
                break;
            case (ItsMode.PowerSaving, true, 1):
                settings.PowerSavingGameFan1Rpm = value;
                break;
            case (ItsMode.PowerSaving, true, 2):
                settings.PowerSavingGameFan2Rpm = value;
                break;
            case (ItsMode.Intelligent, false, 1):
                settings.IntelligentNormalFan1Rpm = value;
                break;
            case (ItsMode.Intelligent, false, 2):
                settings.IntelligentNormalFan2Rpm = value;
                break;
            case (ItsMode.Intelligent, true, 1):
                settings.IntelligentGameFan1Rpm = value;
                break;
            case (ItsMode.Intelligent, true, 2):
                settings.IntelligentGameFan2Rpm = value;
                break;
            case (ItsMode.Performance, false, 1):
                settings.PerformanceNormalFan1Rpm = value;
                break;
            case (ItsMode.Performance, false, 2):
                settings.PerformanceNormalFan2Rpm = value;
                break;
            case (ItsMode.Performance, true, 1):
                settings.PerformanceGameFan1Rpm = value;
                break;
            case (ItsMode.Performance, true, 2):
                settings.PerformanceGameFan2Rpm = value;
                break;
            case (ItsMode.Geek, false, 1):
                settings.GeekNormalFan1Rpm = value;
                break;
            case (ItsMode.Geek, false, 2):
                settings.GeekNormalFan2Rpm = value;
                break;
            case (ItsMode.Geek, true, 1):
                settings.GeekGameFan1Rpm = value;
                break;
            case (ItsMode.Geek, true, 2):
                settings.GeekGameFan2Rpm = value;
                break;
        }
    }

    private void SaveFixedRpmSettingsFromUi()
    {
        if (_loadingSettings)
            return;

        var settings = new FixedRpmSettings();
        foreach (var mode in new[] { ItsMode.PowerSaving, ItsMode.Intelligent, ItsMode.Performance, ItsMode.Geek })
        {
            SetFixedRpmValue(settings, mode, false, 1, ParseFixedRpmBox(mode, false, 1));
            SetFixedRpmValue(settings, mode, false, 2, ParseFixedRpmBox(mode, false, 2));
            SetFixedRpmValue(settings, mode, true, 1, ParseFixedRpmBox(mode, true, 1));
            SetFixedRpmValue(settings, mode, true, 2, ParseFixedRpmBox(mode, true, 2));
        }

        _settings.FixedRpm = CurveProfileStore.NormalizeFixedRpmSettings(settings, _fanMinRpm, _fanMaxRpm);
        if (_settings.FixedSyncFanSpeeds)
            CopyFixedFan1ToFan2(_settings.FixedRpm);
        RefreshFixedRpmBoxes();
        CurveProfileStore.SaveSettings(_settings);
    }

    private int ParseFixedRpmBox(ItsMode mode, bool game, int fan)
    {
        if (_fixedRpmBoxes.TryGetValue((mode, game, fan), out var box) &&
            int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return GetFixedRpmValue(mode, game, fan);
    }

    private void RefreshFixedRpmBoxes()
    {
        _updatingFixedRpmBoxes = true;
        try
        {
            foreach (var ((mode, game, fan), box) in _fixedRpmBoxes)
                box.Text = GetFixedRpmValue(mode, game, fan).ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _updatingFixedRpmBoxes = false;
        }
    }

    private void SyncFixedRpmTextBox(ItsMode mode, bool game, int fan, string text)
    {
        if (_updatingFixedRpmBoxes || !_settings.FixedSyncFanSpeeds)
            return;

        var otherFan = fan == 1 ? 2 : 1;
        if (!_fixedRpmBoxes.TryGetValue((mode, game, otherFan), out var otherBox))
            return;

        _updatingFixedRpmBoxes = true;
        try
        {
            otherBox.Text = text;
        }
        finally
        {
            _updatingFixedRpmBoxes = false;
        }
    }

    private static void CopyFixedFan1ToFan2(FixedRpmSettings settings)
    {
        settings.PowerSavingNormalFan2Rpm = settings.PowerSavingNormalFan1Rpm;
        settings.PowerSavingGameFan2Rpm = settings.PowerSavingGameFan1Rpm;
        settings.IntelligentNormalFan2Rpm = settings.IntelligentNormalFan1Rpm;
        settings.IntelligentGameFan2Rpm = settings.IntelligentGameFan1Rpm;
        settings.PerformanceNormalFan2Rpm = settings.PerformanceNormalFan1Rpm;
        settings.PerformanceGameFan2Rpm = settings.PerformanceGameFan1Rpm;
        settings.GeekNormalFan2Rpm = settings.GeekNormalFan1Rpm;
        settings.GeekGameFan2Rpm = settings.GeekGameFan1Rpm;
    }

    private void UpdateFixedModeCombo()
    {
        _updatingFixedModeCombo = true;
        try
        {
            _fixedModeCombo.SelectedIndex = _effectiveGameMode ? 1 : 0;
        }
        finally
        {
            _updatingFixedModeCombo = false;
        }
    }

    private void SetManualFixedMode(bool gameMode)
    {
        _settings.FixedGameModeOverride = gameMode
            ? FixedGameModeOverride.GameUntilGamesEnd
            : FixedGameModeOverride.NormalUntilGameStarts;
        _settings.ManualGameMode = gameMode;
        _overrideGameSeenSinceArmed = false;
        _overrideNormalSawNoGamesSinceArmed = !gameMode && !_confirmedGamesRunning;
        _effectiveGameMode = gameMode;
        if (!gameMode)
            _lastGameStopTime = null;
        UpdateFixedModeCombo();
        CurveProfileStore.SaveSettings(_settings);
    }

    private void ToggleManualFixedMode()
    {
        SetManualFixedMode(!_effectiveGameMode);
    }

    private void UpdateFixedModeHotkeyButton()
    {
        _fixedModeHotkeyButton.Content = string.IsNullOrWhiteSpace(_settings.FixedModeHotkey)
            ? T("None")
            : _settings.FixedModeHotkey;
    }

    private void BeginFixedModeHotkeyCapture()
    {
        _capturingFixedModeHotkey = true;
        _fixedModeHotkeyButton.Content = T("PressShortcut");
        _fixedModeHotkeyButton.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (_capturingFixedModeHotkey)
        {
            CaptureFixedModeHotkey(args);
            return;
        }

        if (IsFixedModeHotkey(args))
        {
            ToggleManualFixedMode();
            args.Handled = true;
        }
    }

    private void CaptureFixedModeHotkey(KeyEventArgs args)
    {
        args.Handled = true;
        var key = RealKey(args);
        if (key is Key.Escape or Key.Back or Key.Delete)
        {
            _settings.FixedModeHotkey = "";
            _capturingFixedModeHotkey = false;
            UpdateFixedModeHotkeyButton();
            CurveProfileStore.SaveSettings(_settings);
            return;
        }

        if (IsModifierKey(key))
            return;

        _settings.FixedModeHotkey = FormatHotkey(Keyboard.Modifiers, key);
        _capturingFixedModeHotkey = false;
        UpdateFixedModeHotkeyButton();
        CurveProfileStore.SaveSettings(_settings);
    }

    private bool IsFixedModeHotkey(KeyEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_settings.FixedModeHotkey))
            return false;

        var key = RealKey(args);
        if (IsModifierKey(key))
            return false;

        return string.Equals(FormatHotkey(Keyboard.Modifiers, key), _settings.FixedModeHotkey, StringComparison.OrdinalIgnoreCase);
    }

    private static Key RealKey(KeyEventArgs args)
    {
        return args.Key switch
        {
            Key.System => args.SystemKey,
            Key.ImeProcessed => args.ImeProcessedKey,
            _ => args.Key
        };
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    }

    private static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private void ApplyStrategyVisibility()
    {
        var fixedMode = _settings.ControlStrategy == ControlStrategy.FixedRpm;
        if (_strategyTabs is not null)
        {
            _strategyTabs.SelectedIndex = fixedMode ? 0 : 1;
        }

        if (fixedMode)
        {
            _queuedTarget = null;
            _lastFan1TargetTime = null;
            _lastFan2TargetTime = null;
        }
    }

    private void OnStrategyTabChanged()
    {
        if (_loadingSettings || _strategyTabs is null)
            return;

        var selectedStrategy = _strategyTabs.SelectedIndex == 1 ? ControlStrategy.FanCurve : ControlStrategy.FixedRpm;
        if (_running && selectedStrategy != _settings.ControlStrategy)
        {
            ShowStopFirstWarning();
            _loadingSettings = true;
            _strategyTabs.SelectedIndex = _settings.ControlStrategy == ControlStrategy.FanCurve ? 1 : 0;
            _loadingSettings = false;
            return;
        }

        if (selectedStrategy == ControlStrategy.FanCurve && !_fanCurveWarningShownThisRun)
        {
            if (!ShowFanCurveWarningDialog())
            {
                _loadingSettings = true;
                _strategyTabs.SelectedIndex = 0;
                _loadingSettings = false;
                return;
            }
            _fanCurveWarningShownThisRun = true;
        }

        _settings.ControlStrategy = selectedStrategy;
        CurveProfileStore.SaveSettings(_settings);
        ApplyStrategyVisibility();
    }

    private void ShowStopFirstWarning()
    {
        MessageBox.Show(this, T("StopFanControlFirst"), T("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool ShowFanCurveWarningDialog()
    {
        var dialog = new Window
        {
            Title = T("FanCurveWarningTitle"),
            Owner = this,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = T("FanCurveWarning"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var continueButton = new Button { Content = T("Continue"), MinWidth = 86, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = T("Cancel"), MinWidth = 86, IsCancel = true };
        continueButton.Click += (_, _) => { dialog.DialogResult = true; };
        cancelButton.Click += (_, _) => { dialog.DialogResult = false; };
        buttons.Children.Add(continueButton);
        buttons.Children.Add(cancelButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }

    private void ResetFanTargetState()
    {
        _lastTarget = null;
        _lastAppliedTarget = null;
        _lastFan1TargetTime = null;
        _lastFan2TargetTime = null;
        _queuedTarget = null;
    }

    private void UpdateTemperatureUi(TemperatureSnapshot temps)
    {
        _cpuTempText.Text = FormatTemp(temps.CpuTempC);
        _gpuTempText.Text = FormatTemp(temps.GpuTempC);
        _vramTempText.Text = FormatTemp(temps.VramTempC);
        _lastCpuText = _cpuTempText.Text;
        _lastGpuText = _gpuTempText.Text;
        _lastVramText = _vramTempText.Text;
        UpdateTrayMenuMetrics();
    }

    private string FormatTarget(FanTargets? target)
    {
        if (target is null)
            return "--";

        if (target.Fan1Rpm == 0 && target.Fan2Rpm == 0)
            return T("FirmwareAuto");

        return $"F1 {target.Fan1Rpm} / F2 {target.Fan2Rpm} RPM";
    }

    private string FormatGameState()
    {
        if (_gamesRunning)
            return T("On");
        if (_effectiveGameMode)
            return T("Holding");
        return T("Off");
    }

    private static string ModeKey(ItsMode mode)
    {
        return mode switch
        {
            ItsMode.PowerSaving => "PowerSaving",
            ItsMode.Intelligent => "Intelligent",
            ItsMode.Performance => "Performance",
            ItsMode.Geek => "Geek",
            _ => "Unknown"
        };
    }

    private void SetResumeFanControlOnNextStart(bool value)
    {
        if (_settings.ResumeFanControlOnNextStart == value)
            return;

        _settings.ResumeFanControlOnNextStart = value;
        CurveProfileStore.SaveSettings(_settings);
    }

    private TemperatureSnapshot ReadTemperatures()
    {
        _temperatureReader ??= new TemperatureReader();
        return _temperatureReader.Read();
    }

    private static double? SmoothTemperature(double? previous, double? current, double smoothingSamples)
    {
        if (current is null)
            return previous;
        if (previous is null)
            return current;

        var samples = Math.Max(1, Math.Min(10, smoothingSamples));
        var alpha = 2.0 / (samples + 1.0);
        return previous.Value + (current.Value - previous.Value) * alpha;
    }

    private void UpdateHeatSoak(TemperatureSnapshot temps)
    {
        var hottest = new[] { temps.CpuTempC, temps.GpuTempC }
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty(double.NaN)
            .Max();
        if (double.IsNaN(hottest))
            return;

        var now = DateTimeOffset.Now;
        if (hottest >= HeatSoakEnterTempC)
        {
            _highTempSince ??= now;
            _heatSoakExitSamples.Clear();
            if (now - _highTempSince.Value >= HeatSoakDuration)
                _heatSoaked = true;
            return;
        }

        if (hottest < HeatSoakExitTempC)
        {
            TrackHeatSoakExitSample(now, hottest);
            if (CanExitHeatSoak())
            {
                _highTempSince = null;
                _heatSoaked = false;
                _heatSoakExitSamples.Clear();
            }
            return;
        }

        _heatSoakExitSamples.Clear();
        if (!_heatSoaked)
            _highTempSince = null;
    }

    private void TrackHeatSoakExitSample(DateTimeOffset now, double hottest)
    {
        _heatSoakExitSamples.Enqueue((now, hottest));
        while (_heatSoakExitSamples.Count > 0 &&
               now - _heatSoakExitSamples.Peek().Timestamp > HeatSoakExitAverageDuration)
        {
            _heatSoakExitSamples.Dequeue();
        }
    }

    private bool CanExitHeatSoak()
    {
        if (!_heatSoaked || _heatSoakExitSamples.Count < 2)
            return false;

        var span = _heatSoakExitSamples.Last().Timestamp - _heatSoakExitSamples.Peek().Timestamp;
        if (span < HeatSoakExitAverageDuration)
            return false;

        return _heatSoakExitSamples.Average(sample => sample.TempC) < HeatSoakExitTempC;
    }

    private FanTargets ApplyRampDown(FanTargets rawTarget, double rampDownRpmPerSecond)
    {
        if (rampDownRpmPerSecond <= 0)
            return rawTarget;
        if (!_heatSoaked)
            return rawTarget;
        if (!_running || _lastTarget is null)
            return rawTarget;

        var now = DateTimeOffset.Now;
        return new FanTargets(
            ApplyRampDown(rawTarget.Fan1Rpm, _lastTarget.Fan1Rpm, _lastFan1TargetTime, now, rampDownRpmPerSecond),
            ApplyRampDown(rawTarget.Fan2Rpm, _lastTarget.Fan2Rpm, _lastFan2TargetTime, now, rampDownRpmPerSecond));
    }

    private int ApplyRampDown(int rawTarget, int lastTarget, DateTimeOffset? lastTargetTime, DateTimeOffset now, double rampDownRpmPerSecond)
    {
        if (rawTarget >= lastTarget)
            return rawTarget;

        var elapsedSeconds = Math.Max(0.1, (now - (lastTargetTime ?? now)).TotalSeconds);
        var maxDrop = Math.Max(1, rampDownRpmPerSecond) * elapsedSeconds;
        var limited = Math.Max(rawTarget, lastTarget - maxDrop);
        return ClampForCurrentRange((int)Math.Ceiling(limited / 100.0) * 100);
    }

    private static string FormatTemp(double? value)
    {
        return value is null ? "-- \u00B0C" : $"{value:F1} \u00B0C";
    }

    private static ComboBox OptionCombo(params string[] options)
    {
        var comboBox = new ComboBox
        {
            Width = 62,
            IsEditable = false,
            Margin = new Thickness(0, 0, 8, 0)
        };
        foreach (var option in options)
            comboBox.Items.Add(option);
        comboBox.SelectedIndex = 0;
        return comboBox;
    }

    private void HookSettingsControls()
    {
        _languageCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings)
                return;
            _settings.Language = _languageCombo.SelectedIndex == 1 ? "en-US" : "zh-CN";
            CurveProfileStore.SaveSettings(_settings);
            ApplyLanguage();
        };

        _themeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings)
                return;
            _settings.Theme = _themeCombo.SelectedIndex == 1 ? "dark" : "light";
            CurveProfileStore.SaveSettings(_settings);
            ApplyTheme();
        };

        _intervalCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings)
                return;
            _settings.IntervalSeconds = SelectedNumber(_intervalCombo, 2.0);
            CurveProfileStore.SaveSettings(_settings);
            SyncTimerIntervals();
        };

        _gameExitHoldCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings)
                return;
            _settings.GameExitHoldSeconds = SelectedNumber(_gameExitHoldCombo, 20);
            CurveProfileStore.SaveSettings(_settings);
        };

        _editFanCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings)
                return;
            _settings.EditFan = _editFanCombo.SelectedIndex == 1 ? 2 : 1;
            CurveProfileStore.SaveSettings(_settings);
            ApplyCurveEditSettings();
        };

        _syncFanSpeedsCheck.Checked += (_, _) => UpdateBooleanSetting("syncFanSpeeds", true);
        _syncFanSpeedsCheck.Unchecked += (_, _) => UpdateBooleanSetting("syncFanSpeeds", false);
        _fixedSyncFanSpeedsCheck.Checked += (_, _) => UpdateBooleanSetting("fixedSyncFanSpeeds", true);
        _fixedSyncFanSpeedsCheck.Unchecked += (_, _) => UpdateBooleanSetting("fixedSyncFanSpeeds", false);
        _autoDetectGamesCheck.Checked += (_, _) => UpdateBooleanSetting("autoDetectGames", true);
        _autoDetectGamesCheck.Unchecked += (_, _) => UpdateBooleanSetting("autoDetectGames", false);
        _fixedModeCombo.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings || _updatingFixedModeCombo || _fixedModeCombo.SelectedIndex < 0)
                return;
            SetManualFixedMode(_fixedModeCombo.SelectedIndex == 1);
        };
        _fixedModeHotkeyButton.Click += (_, _) => BeginFixedModeHotkeyCapture();
        _startupCheck.Checked += (_, _) => UpdateBooleanSetting("startup", true);
        _startupCheck.Unchecked += (_, _) => UpdateBooleanSetting("startup", false);
        _startToTrayCheck.Checked += (_, _) => UpdateBooleanSetting("startToTray", true);
        _startToTrayCheck.Unchecked += (_, _) => UpdateBooleanSetting("startToTray", false);
        _minimizeToTrayCheck.Checked += (_, _) => UpdateBooleanSetting("minimizeToTray", true);
        _minimizeToTrayCheck.Unchecked += (_, _) => UpdateBooleanSetting("minimizeToTray", false);
        _closeToTrayCheck.Checked += (_, _) => UpdateBooleanSetting("closeToTray", true);
        _closeToTrayCheck.Unchecked += (_, _) => UpdateBooleanSetting("closeToTray", false);
    }

    private void LoadSettingsControls()
    {
        _loadingSettings = true;
        SelectComboValue(_intervalCombo, _settings.IntervalSeconds);
        if (_strategyTabs is not null)
            _strategyTabs.SelectedIndex = _settings.ControlStrategy == ControlStrategy.FanCurve ? 1 : 0;
        SelectComboValue(_gameExitHoldCombo, _settings.GameExitHoldSeconds);
        _editFanCombo.SelectedIndex = _settings.EditFan == 2 ? 1 : 0;
        _syncFanSpeedsCheck.IsChecked = _settings.SyncFanSpeeds;
        _fixedSyncFanSpeedsCheck.IsChecked = _settings.FixedSyncFanSpeeds;
        _autoDetectGamesCheck.IsChecked = _settings.AutoDetectGames;
        _effectiveGameMode = _settings.FixedGameModeOverride == FixedGameModeOverride.GameUntilGamesEnd;
        UpdateFixedModeCombo();
        UpdateFixedModeHotkeyButton();
        _languageCombo.SelectedIndex = _settings.Language == "en-US" ? 1 : 0;
        _themeCombo.SelectedIndex = _settings.Theme == "dark" ? 1 : 0;
        _startupCheck.IsChecked = _settings.StartWithWindows;
        _startToTrayCheck.IsChecked = _settings.StartToTray;
        _minimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        _closeToTrayCheck.IsChecked = _settings.CloseToTray;
        RefreshFixedRpmBoxes();
        _loadingSettings = false;
        ApplyStrategyVisibility();
    }

    private void UpdateBooleanSetting(string setting, bool value)
    {
        if (_loadingSettings)
            return;

        switch (setting)
        {
            case "startup":
                _settings.StartWithWindows = value;
                ApplyStartupSetting();
                break;
            case "startToTray":
                _settings.StartToTray = value;
                ApplyStartupSetting();
                break;
            case "minimizeToTray":
                _settings.MinimizeToTray = value;
                break;
            case "closeToTray":
                _settings.CloseToTray = value;
                break;
            case "syncFanSpeeds":
                _settings.SyncFanSpeeds = value;
                ApplyCurveEditSettings();
                break;
            case "fixedSyncFanSpeeds":
                _settings.FixedSyncFanSpeeds = value;
                if (value)
                {
                    SaveFixedRpmSettingsFromUi();
                }
                break;
            case "autoDetectGames":
                _settings.AutoDetectGames = value;
                if (value)
                {
                    _settings.FixedGameModeOverride = FixedGameModeOverride.None;
                    _settings.ManualGameMode = false;
                    _overrideGameSeenSinceArmed = false;
                    _overrideNormalSawNoGamesSinceArmed = false;
                    _hasConfirmedFixedState = false;
                    _hasPendingFixedState = false;
                }
                else
                {
                    SetManualFixedMode(_effectiveGameMode);
                    return;
                }
                break;
        }
        CurveProfileStore.SaveSettings(_settings);
        ApplyLanguage();
        UpdateTrayMenu();
    }

    private void ApplyCurveEditSettings()
    {
        _cpuChart.SetEditFan(_settings.EditFan);
        _gpuChart.SetEditFan(_settings.EditFan);
        _cpuChart.SetSyncFanSpeeds(_settings.SyncFanSpeeds);
        _gpuChart.SetSyncFanSpeeds(_settings.SyncFanSpeeds);
    }

    private void SyncTimerIntervals()
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.5, _settings.IntervalSeconds));
        _timer.Interval = interval;
        _trayMenuTimer.Interval = interval;
    }

    private bool IsChinese => _settings.Language != "en-US";
    private bool IsDark => _settings.Theme == "dark";

    private string T(string key)
    {
        return IsChinese ? key switch
        {
            "AppTitle" => "ThinkBook \u98ce\u6247\u63a7\u5236",
            "Profile" => "\u65b9\u6848",
            "Name" => "\u540d\u79f0",
            "Strategy" => "\u63a7\u5236\u65b9\u5f0f",
            "FixedRpm" => "\u56fa\u5b9a\u8f6c\u901f",
            "Interval" => "\u5237\u65b0\u95f4\u9694",
            "TempSmoothing" => "\u6e29\u5ea6\u5e73\u6ed1",
            "RampDown" => "\u964d\u901f\u9650\u5236",
            "GameExitHold" => "\u6e38\u620f\u9000\u51fa\u5ef6\u65f6",
            "FixedMode" => "\u6a21\u5f0f",
            "FixedModeHotkey" => "\u5feb\u6377\u952e",
            "FixedSyncFanSpeeds" => "\u540c\u6b65\u56fa\u5b9a\u8f6c\u901f",
            "AutoDetectGames" => "\u81ea\u52a8\u68c0\u6d4b\u6e38\u620f",
            "EditFan" => "\u7f16\u8f91",
            "SyncFanSpeeds" => "\u540c\u6b65\u8f6c\u901f",
            "Language" => "\u8bed\u8a00",
            "Theme" => "\u4e3b\u9898",
            "Light" => "\u6d45\u8272",
            "Dark" => "\u6df1\u8272",
            "Fan1" => "\u98ce\u6247 1",
            "Fan2" => "\u98ce\u6247 2",
            "Target" => "\u76ee\u6807",
            "CpuCurve" => "CPU \u66f2\u7ebf",
            "GpuCurve" => "GPU \u66f2\u7ebf",
            "TemperatureAxis" => "\u6e29\u5ea6 (\u00B0C)",
            "Save" => "\u4fdd\u5b58",
            "Refresh" => "\u5237\u65b0",
            "Start" => "\u542f\u52a8",
            "Stop" => "\u505c\u6b62",
            "Idle" => "\u7a7a\u95f2",
            "Stopping" => "\u505c\u6b62\u4e2d...",
            "Running" => "\u8fd0\u884c\u4e2d",
            "Monitoring" => "\u76d1\u63a7\u4e2d",
            "HeatSoak" => "\u70ed\u6d78",
            "On" => "\u5f00",
            "Off" => "\u5173",
            "Saved" => "\u5df2\u4fdd\u5b58",
            "ControllerEnabled" => "\u98ce\u6247\u63a7\u5236\u5df2\u542f\u7528",
            "ControllerResumed" => "\u5df2\u6062\u590d\u4e0a\u6b21\u672a\u505c\u6b62\u7684\u98ce\u6247\u63a7\u5236",
            "RestoringAuto" => "\u6b63\u5728\u6062\u590d\u81ea\u52a8\u98ce\u6247\u63a7\u5236...",
            "AutoRestored" => "\u5df2\u6062\u590d\u81ea\u52a8\u98ce\u6247\u63a7\u5236",
            "RestoreAutoFailed" => "\u6062\u590d\u81ea\u52a8\u5931\u8d25",
            "StopBeforeSwitch" => "\u5207\u6362\u65b9\u6848\u524d\u8bf7\u5148\u505c\u6b62\u63a7\u5236\u5668\u3002",
            "FanCurve" => "\u98ce\u6247\u66f2\u7ebf",
            "FanCurveWarningTitle" => "\u98ce\u6247\u66f2\u7ebf\u8b66\u544a",
            "FanCurveWarning" => "\u98ce\u6247\u66f2\u7ebf\u4f1a\u9891\u7e41\u5199\u5165 Lenovo WMI\uff0c\u53ef\u80fd\u5bfc\u81f4 Fn \u4eae\u5ea6\u7b49\u529f\u80fd\u5361\u987f\u3002\u8bf7\u8c28\u614e\u4f7f\u7528\u3002",
            "Continue" => "\u7ee7\u7eed",
            "Cancel" => "\u53d6\u6d88",
            "Warning" => "\u8b66\u544a",
            "StopFanControlFirst" => "\u8bf7\u5148\u505c\u6b62\u98ce\u6247\u63a7\u5236\uff01",
            "MonitorError" => "\u76d1\u63a7\u9519\u8bef",
            "FanReadError" => "\u98ce\u6247\u8bfb\u53d6\u9519\u8bef",
            "FanWriteError" => "\u98ce\u6247\u5199\u5165\u9519\u8bef",
            "Startup" => "\u5f00\u673a\u81ea\u542f",
            "StartToTray" => "\u542f\u52a8\u5230\u6258\u76d8",
            "MinimizeToTray" => "\u6700\u5c0f\u5316\u5230\u6258\u76d8",
            "CloseToTray" => "\u5173\u95ed\u65f6\u6700\u5c0f\u5316",
            "ShowWindow" => "\u663e\u793a\u7a97\u53e3",
            "Exit" => "\u9000\u51fa",
            "Normal" => "\u5e73\u65f6",
            "Game" => "\u6e38\u620f",
            "NormalFan1" => "\u5e73\u65f6 FAN1",
            "NormalFan2" => "\u5e73\u65f6 FAN2",
            "GameFan1" => "\u6e38\u620f FAN1",
            "GameFan2" => "\u6e38\u620f FAN2",
            "PowerSaving" => "\u7701\u7535",
            "Intelligent" => "\u667a\u80fd",
            "Performance" => "\u6027\u80fd",
            "Geek" => "\u6781\u5ba2",
            "Unknown" => "\u672a\u77e5",
            "FirmwareAuto" => "\u9ed8\u8ba4\u81ea\u52a8",
            "CurrentMode" => "\u5f53\u524d\u6a21\u5f0f",
            "Holding" => "\u5ef6\u65f6",
            "PendingChange" => "\u7b49\u5f85\u4e8c\u6b21\u786e\u8ba4",
            "None" => "\u65e0",
            "PressShortcut" => "\u8bf7\u6309\u5feb\u6377\u952e",
            "FixedRpmNote" => "0 = \u56fa\u4ef6\u9ed8\u8ba4\u81ea\u52a8\u3002\u975e 0 \u8f6c\u901f\u4f1a\u81ea\u52a8\u9650\u5236\u5230\u68c0\u6d4b\u5230\u7684\u98ce\u6247\u8303\u56f4\u3002",
            _ => key
        } : key switch
        {
            "AppTitle" => "ThinkBook Fan Control",
            "Strategy" => "Mode",
            "FixedRpm" => "Fixed RPM",
            "TempSmoothing" => "Temp smoothing",
            "RampDown" => "Ramp down",
            "GameExitHold" => "Game hold",
            "FixedMode" => "Mode",
            "FixedModeHotkey" => "Hotkey",
            "FixedSyncFanSpeeds" => "Sync fixed speeds",
            "AutoDetectGames" => "Auto detect games",
            "EditFan" => "Edit",
            "SyncFanSpeeds" => "Sync speeds",
            "Fan1" => "Fan 1",
            "Fan2" => "Fan 2",
            "CpuCurve" => "CPU Curve",
            "GpuCurve" => "GPU Curve",
            "TemperatureAxis" => "Temperature (\u00B0C)",
            "Idle" => "Idle",
            "Stopping" => "Stopping...",
            "HeatSoak" => "Heat soak",
            "ControllerEnabled" => "Controller enabled",
            "ControllerResumed" => "Resumed previously active fan control",
            "RestoringAuto" => "Restoring automatic fan control...",
            "AutoRestored" => "Automatic fan control restored",
            "RestoreAutoFailed" => "Restore auto failed",
            "StopBeforeSwitch" => "Stop the controller before switching profiles.",
            "FanCurve" => "Fan curve",
            "FanCurveWarningTitle" => "Fan curve warning",
            "FanCurveWarning" => "Fan curve mode writes Lenovo WMI frequently and may make Fn brightness or similar controls stutter. Use it carefully.",
            "Continue" => "Continue",
            "Cancel" => "Cancel",
            "Warning" => "Warning",
            "StopFanControlFirst" => "Please stop fan control first!",
            "MonitorError" => "Monitor error",
            "FanReadError" => "Fan read error",
            "FanWriteError" => "Fan write error",
            "Startup" => "Start with Windows",
            "StartToTray" => "Start to tray",
            "MinimizeToTray" => "Minimize to tray",
            "CloseToTray" => "Close to tray",
            "ShowWindow" => "Show window",
            "Exit" => "Exit",
            "Normal" => "Normal",
            "Game" => "Game",
            "NormalFan1" => "Normal FAN1",
            "NormalFan2" => "Normal FAN2",
            "GameFan1" => "Game FAN1",
            "GameFan2" => "Game FAN2",
            "PowerSaving" => "Power saving",
            "Intelligent" => "Intelligent",
            "Performance" => "Performance",
            "Geek" => "Geek",
            "Unknown" => "Unknown",
            "FirmwareAuto" => "Firmware auto",
            "CurrentMode" => "Current mode",
            "Holding" => "Holding",
            "PendingChange" => "Waiting for confirmation",
            "None" => "None",
            "PressShortcut" => "Press shortcut",
            "FixedRpmNote" => "0 = firmware auto. Non-zero RPM values are clamped to the detected fan range.",
            _ => key
        };
    }

    private void ApplyLanguage()
    {
        Title = T("AppTitle");
        var fontFamilyName = IsChinese ? "Segoe UI, SimSun" : "Segoe UI";
        FontFamily = new FontFamily(fontFamilyName);
        _cpuMetricTitle.Text = "CPU";
        _gpuMetricTitle.Text = "GPU";
        _vramMetricTitle.Text = "VRAM";
        _fan1MetricTitle.Text = T("Fan1");
        _fan2MetricTitle.Text = T("Fan2");
        _targetMetricTitle.Text = T("Target");
        _profileLabel.Text = T("Profile");
        _nameLabel.Text = T("Name");
        _intervalLabel.Text = T("Interval");
        _smoothingLabel.Text = T("TempSmoothing");
        _rampDownLabel.Text = T("RampDown");
        _gameExitHoldLabel.Text = T("GameExitHold");
        _fixedModeLabel.Text = T("FixedMode");
        _fixedModeHotkeyLabel.Text = T("FixedModeHotkey");
        _editFanLabel.Text = T("EditFan");
        _languageLabel.Text = T("Language");
        _themeLabel.Text = T("Theme");
        _saveButton.Content = T("Save");
        _refreshButton.Content = T("Refresh");
        _syncFanSpeedsCheck.Content = T("SyncFanSpeeds");
        _fixedSyncFanSpeedsCheck.Content = T("FixedSyncFanSpeeds");
        _autoDetectGamesCheck.Content = T("AutoDetectGames");
        UpdateFixedModeHotkeyButton();
        _startupCheck.Content = T("Startup");
        _startToTrayCheck.Content = T("StartToTray");
        _minimizeToTrayCheck.Content = T("MinimizeToTray");
        _closeToTrayCheck.Content = T("CloseToTray");
        _startButton.Content = _running ? T("Stop") : T("Start");
        _fixedStrategyTab!.Header = T("FixedRpm");
        _fanCurveStrategyTab!.Header = T("FanCurve");
        _cpuTab!.Header = T("CpuCurve");
        _gpuTab!.Header = T("GpuCurve");
        if (_fixedRpmHeader is not null)
            _fixedRpmHeader.Text = T("FixedRpm");
        if (_fixedColumnLabels.TryGetValue((false, 1), out var normalFan1Label))
            normalFan1Label.Text = T("NormalFan1");
        if (_fixedColumnLabels.TryGetValue((false, 2), out var normalFan2Label))
            normalFan2Label.Text = T("NormalFan2");
        if (_fixedColumnLabels.TryGetValue((true, 1), out var gameFan1Label))
            gameFan1Label.Text = T("GameFan1");
        if (_fixedColumnLabels.TryGetValue((true, 2), out var gameFan2Label))
            gameFan2Label.Text = T("GameFan2");
        if (_fixedRpmNote is not null)
            _fixedRpmNote.Text = T("FixedRpmNote");
        foreach (var (mode, label) in _fixedModeLabels)
            label.Text = T(ModeKey(mode));
        _cpuChart.SetLabels(T("CpuCurve"), T("TemperatureAxis"));
        _gpuChart.SetLabels(T("GpuCurve"), T("TemperatureAxis"));
        _cpuChart.SetFontFamily(fontFamilyName);
        _gpuChart.SetFontFamily(fontFamilyName);
        if (!_running && (_statusText.Text is "Idle" or "\u7a7a\u95f2"))
            _statusText.Text = T("Idle");

        _loadingSettings = true;
        SetComboItems(_languageCombo, ["\u4e2d\u6587", "English"], IsChinese ? 0 : 1);
        SetComboItems(_themeCombo, [T("Light"), T("Dark")], IsDark ? 1 : 0);
        SetComboItems(_fixedModeCombo, [T("Normal"), T("Game")], _effectiveGameMode ? 1 : 0);
        SetComboItems(_editFanCombo, [T("Fan1"), T("Fan2")], _settings.EditFan == 2 ? 1 : 0);
        _loadingSettings = false;
        ApplyStrategyVisibility();
        UpdateTrayMenu();
    }

    private void ApplyTheme()
    {
        var background = Brush(IsDark ? "#111827" : "#ffffff");
        var surface = Brush(IsDark ? "#1f2937" : "#ffffff");
        var border = Brush(IsDark ? "#374151" : "#d1d5db");
        var text = Brush(IsDark ? "#f9fafb" : "#111827");
        var muted = Brush(IsDark ? "#d1d5db" : "#4b5563");

        Background = background;
        if (_root is not null)
            _root.Background = background;
        foreach (var tabs in new[] { _strategyTabs, _curveTabs })
        {
            if (tabs is null)
                continue;
            tabs.Background = background;
            tabs.Foreground = text;
            tabs.BorderBrush = border;
        }
        if (_bottomBorder is not null)
            _bottomBorder.Background = background;
        _statusText.Foreground = muted;

        foreach (var metric in _metricBorders)
        {
            metric.Background = surface;
            metric.BorderBrush = border;
        }

        foreach (var label in _labels)
            label.Foreground = muted;
        foreach (var checkBox in new[] { _syncFanSpeedsCheck, _fixedSyncFanSpeedsCheck, _autoDetectGamesCheck, _startupCheck, _startToTrayCheck, _minimizeToTrayCheck, _closeToTrayCheck })
            checkBox.Foreground = muted;
        foreach (var value in new[] { _cpuTempText, _gpuTempText, _vramTempText, _fan1Text, _fan2Text, _targetText })
            value.Foreground = text;

        _cpuChart.SetTheme(IsDark);
        _gpuChart.SetTheme(IsDark);
    }

    private static void SetComboItems(ComboBox comboBox, IReadOnlyList<string> items, int selectedIndex)
    {
        comboBox.Items.Clear();
        foreach (var item in items)
            comboBox.Items.Add(item);
        comboBox.SelectedIndex = Math.Max(0, Math.Min(items.Count - 1, selectedIndex));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void AddLabeledControl(Panel panel, TextBlock label, UIElement control)
    {
        if (!_labels.Contains(label))
            _labels.Add(label);
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 8, 6)
        };
        group.Children.Add(label);
        group.Children.Add(control);
        panel.Children.Add(group);
    }

    private static void AddStandaloneControl(Panel panel, UIElement control)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 8, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        group.Children.Add(control);
        panel.Children.Add(group);
    }

    private static void SelectComboValue(ComboBox comboBox, double value)
    {
        if (value <= 0 && comboBox.Items.Contains("inf"))
        {
            comboBox.SelectedItem = "inf";
            return;
        }

        var text = value.ToString("0.##", CultureInfo.InvariantCulture);
        if (comboBox.Items.Contains(text))
        {
            comboBox.SelectedItem = text;
            return;
        }

        var nearest = comboBox.Items
            .OfType<string>()
            .Where(item => double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .OrderBy(item => Math.Abs(double.Parse(item, CultureInfo.InvariantCulture) - value))
            .FirstOrDefault();

        comboBox.SelectedItem = nearest ?? comboBox.Items[0];
    }

    private static double SelectedNumber(ComboBox comboBox, double fallback)
    {
        return comboBox.SelectedItem is string text &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private double SelectedRampDown()
    {
        return string.Equals(_rampDownCombo.SelectedItem as string, "inf", StringComparison.OrdinalIgnoreCase)
            ? 0
            : SelectedNumber(_rampDownCombo, 20);
    }

    private Border Metric(TextBlock title, TextBlock value)
    {
        var panel = new StackPanel();
        if (!_labels.Contains(title))
            _labels.Add(title);
        panel.Children.Add(title);
        panel.Children.Add(value);
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            Child = panel
        };
        _metricBorders.Add(border);
        return border;
    }

    private static TextBlock MetricValue()
    {
        return new TextBlock
        {
            Text = "--",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0)
        };
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
    }

    private List<string> ProfileLabels()
    {
        return _profiles.Select((profile, index) => $"{index + 1}: {profile.Name}").ToList();
    }
}

