using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThinkBookFanControl;

internal sealed class DeviceInformationWindow : Window
{
    private readonly Func<string, string> _t;
    private readonly bool _isDark;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _left = new();
    private readonly StackPanel _middle = new();
    private readonly StackPanel _right = new();
    private readonly TextBlock _loading = new();
    private readonly List<Border> _cards = [];

    public DeviceInformationWindow(Func<string, string> translate, bool isDark, FontFamily fontFamily, double fontSize)
    {
        _t = translate;
        _isDark = isDark;
        Title = _t("DeviceInformation");
        Width = 1050;
        Height = 720;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Content = BuildLayout();
        ApplyTheme();
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private UIElement BuildLayout()
    {
        _loading.Text = _t("ReadingDeviceInformation");
        _loading.HorizontalAlignment = HorizontalAlignment.Center;
        _loading.VerticalAlignment = VerticalAlignment.Center;

        var columns = new Grid { Margin = new Thickness(14, 0, 14, 14) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _left.Margin = new Thickness(0, 0, 5, 0);
        _middle.Margin = new Thickness(5, 0, 5, 0);
        _right.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(_left, 0); columns.Children.Add(_left);
        Grid.SetColumn(_middle, 1); columns.Children.Add(_middle);
        Grid.SetColumn(_right, 2); columns.Children.Add(_right);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = columns
        };
        var close = new Button { Content = _t("Close"), MinWidth = 88, Margin = new Thickness(0, 0, 14, 12), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(scroll);
        Grid.SetRow(close, 1); root.Children.Add(close);
        root.Children.Add(_loading);
        return root;
    }

    private async Task LoadAsync()
    {
        try
        {
            var infoTask = Task.Run(DeviceInformationService.ReadAll, _lifetime.Token);
            var warrantyTask = WarrantyService.GetWarrantyAsync(_lifetime.Token);
            var info = await infoTask;
            if (_lifetime.IsCancellationRequested) return;
            Render(info);
            var warranty = await warrantyTask;
            if (!_lifetime.IsCancellationRequested) _left.Children.Add(BuildWarrantyCard(warranty));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loading.Text = string.Format(_t("DeviceInformationReadFailedFormat"), ex.Message);
            _loading.Visibility = Visibility.Visible;
        }
    }

    private void Render(DeviceInformationSnapshot info)
    {
        _loading.Visibility = Visibility.Collapsed;
        var identity = new List<(string, string)>
        {
            (_t("DeviceName"), info.DeviceName),
            (_t("DeviceModel"), info.Identity.Model),
            (_t("ProductNumber"), info.Identity.ProductNumber),
            (_t("SerialNumber"), info.Identity.SerialNumber),
            (_t("BiosVersion"), info.Identity.BiosVersion),
            (_t("EcVersion"), info.Identity.EcVersion),
            (_t("DeviceId"), info.DeviceId),
            (_t("ProductId"), info.WindowsProductId)
        };
        _left.Children.Add(BuildCard(_t("Device"), identity));

        if (info.Cpu is not null)
            _left.Children.Add(BuildCard("CPU", [(info.Cpu.Name, string.Format(_t("CpuTopologyFormat"), info.Cpu.Cores, info.Cpu.Threads))]));

        _middle.Children.Add(BuildCard("GPU", info.Gpus.Select(g =>
            (g.Name, g.UsesSharedMemory
                ? _t("SharedGraphicsMemory")
                : g.DedicatedMemoryBytes.HasValue
                    ? string.Format(_t("VideoMemoryFormat"), FormatBytes(g.DedicatedMemoryBytes.Value))
                    : _t("NoInformation")))));

        var ramHeader = string.Format(_t("InstalledMemoryFormat"), FormatBytes(info.InstalledMemoryBytes), FormatBytes(info.UsableMemoryBytes));
        _middle.Children.Add(BuildCard(_t("Memory"), info.Memory.Select(m =>
            ($"{FormatBytes(m.Capacity)}  {m.Type}  {m.Speed} MHz", JoinNonEmpty(m.Locator, m.Manufacturer, m.PartNumber))), ramHeader));

        var totalStorage = info.Partitions.Aggregate<PartitionInfo, ulong>(0, (sum, p) => sum + p.TotalBytes);
        _right.Children.Add(BuildCard(_t("Storage"), info.Partitions.Select(p =>
            ($"{p.Name}  {string.Format(_t("UsedTotalFormat"), FormatBytes(p.UsedBytes), FormatBytes(p.TotalBytes))}", EmptyAsNoInfo(p.DiskModel))),
            string.Format(_t("TotalStorageFormat"), FormatBytes(totalStorage))));

        if (info.Motherboard is not null)
            _right.Children.Add(BuildCard(_t("Motherboard"),
                [(EmptyAsNoInfo(info.Motherboard.Product), JoinNonEmpty(info.Motherboard.Manufacturer, info.Motherboard.Version)),
                 (_t("SerialNumber"), EmptyAsNoInfo(info.Motherboard.SerialNumber))]));

        _right.Children.Add(BuildCard(_t("Displays"), info.Displays.Select((d, i) =>
            ($"{_t("Display")} {i + 1}: {d.Name}", $"{d.Width} × {d.Height} @ {d.RefreshRate} Hz"))));
        ApplyTheme();
    }

    private Border BuildCard(string title, IEnumerable<(string Primary, string Secondary)> rows, string? subtitle = null)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, subtitle is null ? 12 : 3) });
        if (!string.IsNullOrWhiteSpace(subtitle))
            content.Children.Add(new TextBlock { Text = subtitle, Opacity = .72, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        var any = false;
        foreach (var (primary, secondary) in rows)
        {
            any = true;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 11) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var first = new TextBlock { Text = EmptyAsNoInfo(primary), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var second = new TextBlock { Text = EmptyAsNoInfo(secondary), Opacity = .72, FontSize = Math.Max(11, FontSize - 1), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(first); Grid.SetRow(second, 1); row.Children.Add(second); content.Children.Add(row);
        }
        if (!any) content.Children.Add(new TextBlock { Text = _t("NoInformation"), Opacity = .72 });
        var card = NewCard(content);
        return card;
    }

    private Border BuildWarrantyCard(WarrantySnapshot snapshot)
    {
        var state = _t(snapshot.State switch
        {
            WarrantyState.InWarranty => "WarrantyInCoverage",
            WarrantyState.Expired => "WarrantyExpired",
            WarrantyState.NotStarted => "WarrantyNotStarted",
            _ => "WarrantyUnavailable"
        });
        var rows = new List<(string, string)>
        {
            (_t("WarrantyStartDate"), FormatDate(snapshot.StartDate)),
            (_t("WarrantyEndDate"), FormatDate(snapshot.EndDate)),
            (_t("WarrantyProgress"), $"{snapshot.ProgressPercentage}%")
        };
        var card = BuildCard(_t("WarrantyInformation"), rows, state);
        var tooltip = new List<string>();
        if (snapshot.IsStale) tooltip.Add(_t("WarrantyCached"));
        if (!string.IsNullOrWhiteSpace(snapshot.Error)) tooltip.Add(string.Format(_t("WarrantyQueryFailedFormat"), snapshot.Error));
        card.ToolTip = tooltip.Count == 0 ? null : string.Join(Environment.NewLine, tooltip);
        return card;
    }

    private Border NewCard(UIElement child)
    {
        var card = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = child };
        _cards.Add(card);
        return card;
    }

    private void ApplyTheme()
    {
        Background = Brush(_isDark ? "#111827" : "#f5f7fa");
        Foreground = Brush(_isDark ? "#f9fafb" : "#111827");
        foreach (var card in _cards)
        {
            card.Background = Brush(_isDark ? "#1f2937" : "#ffffff");
            card.BorderBrush = Brush(_isDark ? "#374151" : "#d9dee7");
        }
    }

    private string FormatDate(DateOnly? date) => date?.ToString(_t("WarrantyDateFormat"), CultureInfo.InvariantCulture) ?? _t("NoInformation");
    private string EmptyAsNoInfo(string? value) => string.IsNullOrWhiteSpace(value) ? _t("NoInformation") : value;
    private static string JoinNonEmpty(params string[] values) => string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes; var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
    private static SolidColorBrush Brush(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
}
