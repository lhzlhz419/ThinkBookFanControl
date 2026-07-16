using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThinkBookFanControl;

internal sealed class BootLogoCustomizationWindow : Window
{
    private readonly Func<string, string> _t;
    private readonly bool _isDark;
    private readonly Grid _preview = new();
    private readonly TextBlock _resolution = new();
    private readonly TextBlock _formats = new();
    private readonly CheckBox _showLoading = new();
    private readonly Button _customize = new();
    private readonly Button _reset = new();
    private readonly Button _confirm = new();
    private readonly Button _close = new();
    private readonly ProgressBar _busy = new();
    private BiosLogoState? _state;
    private string? _selectedPath;
    private byte[]? _selectedImage;
    private bool _resetPending;
    private bool _loading;

    public BootLogoCustomizationWindow(
        Window owner,
        Func<string, string> translate,
        bool isDark,
        FontFamily fontFamily,
        double fontSize)
    {
        _t = translate;
        _isDark = isDark;
        Owner = owner;
        Title = _t("BootLogoCustomizationTitle");
        Width = 760;
        Height = 720;
        MinWidth = 680;
        MinHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Content = BuildLayout();
        ApplyTheme();
        Loaded += async (_, _) => await LoadStateAsync();
    }

    private UIElement BuildLayout()
    {
        var title = new TextBlock
        {
            Text = _t("BootLogoCustomizationTitle"),
            FontSize = FontSize + 8,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        };

        _preview.Height = 360;
        _preview.Background = Brushes.Black;
        _preview.ClipToBounds = true;

        _busy.IsIndeterminate = true;
        _busy.Width = 110;
        _busy.Height = 6;
        _busy.Visibility = Visibility.Collapsed;
        _busy.HorizontalAlignment = HorizontalAlignment.Center;
        _busy.VerticalAlignment = VerticalAlignment.Center;
        _preview.Children.Add(_busy);

        _resolution.FontWeight = FontWeights.SemiBold;
        _resolution.Margin = new Thickness(0, 16, 0, 8);
        _formats.FontWeight = FontWeights.SemiBold;
        _formats.Margin = new Thickness(0, 0, 0, 10);

        _showLoading.Content = _t("ShowWindowsLoadingIcon");
        _showLoading.Margin = new Thickness(0, 8, 0, 0);
        _showLoading.Checked += (_, _) => UpdateDirtyState();
        _showLoading.Unchecked += (_, _) => UpdateDirtyState();

        ConfigureButton(_customize, _t("Customize"));
        _customize.Click += (_, _) => SelectImage();
        ConfigureButton(_reset, _t("ResetToDefault"));
        _reset.Click += (_, _) => SelectDefault();
        ConfigureButton(_confirm, _t("Confirm"), primary: true);
        _confirm.Click += async (_, _) => await ApplyAsync();
        _confirm.IsEnabled = false;

        ConfigureButton(_close, _t("Close"));
        _close.MinWidth = 90;
        _close.Margin = new Thickness(8, 0, 0, 0);
        _close.IsCancel = true;
        _close.Click += (_, _) => Close();

        var customizeRow = new Grid { Margin = new Thickness(0, 10, 0, 16) };
        customizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_customize, 1);
        customizeRow.Children.Add(_customize);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_reset);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_close);
        right.Children.Add(_confirm);
        Grid.SetColumn(right, 1);
        footer.Children.Add(right);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(title);
        content.Children.Add(_preview);
        content.Children.Add(_resolution);
        content.Children.Add(_formats);
        content.Children.Add(_showLoading);
        content.Children.Add(customizeRow);
        content.Children.Add(footer);
        return content;
    }

    private async Task LoadStateAsync()
    {
        SetBusy(true);
        try
        {
            _state = await Task.Run(BiosAdvancedController.ReadLogoState);
            _showLoading.IsChecked = _state.ShowWindowsLoading;
            _resolution.Text = string.Format(
                _t("BootLogoResolutionFormat"),
                _state.Info.Width,
                _state.Info.Height);
            _formats.Text = string.Format(
                _t("BootLogoFormatsFormat"),
                string.Join(", ", BiosAdvancedController.GetSupportedLogoFormats(_state.Info)));
            ShowPreview(_state.CurrentImage);
            UpdateDirtyState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(_t("AdvancedToolkitFailedFormat"), ex.Message),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectImage()
    {
        if (_state is null) return;
        var dialog = new OpenFileDialog
        {
            Title = _t("BootLogoCustomizationTitle"),
            Filter = BiosAdvancedController.BuildLogoFilter(_state.Info),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            ShowPreview(bytes);
            _selectedPath = dialog.FileName;
            _selectedImage = bytes;
            _resetPending = false;
            UpdateDirtyState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectDefault()
    {
        if (_state is null) return;
        _selectedPath = null;
        _selectedImage = null;
        _resetPending = true;
        _showLoading.IsChecked = true;
        ShowPreview(null);
        UpdateDirtyState();
    }

    private async Task ApplyAsync()
    {
        if (_state is null || !_confirm.IsEnabled) return;
        var firstText = _resetPending
            ? _t("BootLogoResetConfirmFirst")
            : _t("BootLogoApplyConfirmFirst");
        if (!Confirm(firstText)) return;
        var secondText = _resetPending
            ? _t("BootLogoResetConfirmSecond")
            : _t("BootLogoApplyConfirmSecond");
        if (!Confirm(secondText)) return;

        SetBusy(true);
        try
        {
            var showLoading = _showLoading.IsChecked == true;
            await Task.Run(() =>
            {
                if (_resetPending)
                    BiosAdvancedController.ResetBootLogo();
                else if (!string.IsNullOrWhiteSpace(_selectedPath))
                    BiosAdvancedController.SetBootLogo(_selectedPath);
                BiosAdvancedController.SetWindowsLoading(showLoading);
            });
            MessageBox.Show(this, _t("BootLogoSuccess"), Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(_t("AdvancedToolkitFailedFormat"), ex.Message),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowPreview(byte[]? imageBytes)
    {
        _preview.Children.Clear();
        if (imageBytes is { Length: > 0 })
        {
            using var stream = new MemoryStream(imageBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _preview.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(28)
            });
        }
        else
        {
            var lenovo = new Border
            {
                Width = 230,
                Height = 86,
                Background = Brush("#e2231a"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Lenovo",
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 49,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            _preview.Children.Add(lenovo);
        }

        if (_showLoading.IsChecked == true)
        {
            _preview.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Width = 72,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 28)
            });
        }
        _preview.Children.Add(_busy);
    }

    private void UpdateDirtyState()
    {
        if (_state is null) return;
        ShowPreview(_resetPending ? null : _selectedImage ?? _state.CurrentImage);
        _confirm.IsEnabled = !_loading &&
            (_resetPending || _selectedPath is not null ||
             _showLoading.IsChecked != _state.ShowWindowsLoading);
        _reset.IsEnabled = !_loading && (_state.Info.Enabled || _state.CurrentImage is not null || !_state.ShowWindowsLoading);
    }

    private void SetBusy(bool busy)
    {
        _loading = busy;
        _busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _customize.IsEnabled = !busy;
        _showLoading.IsEnabled = !busy;
        _confirm.IsEnabled = !busy && _confirm.IsEnabled;
        _reset.IsEnabled = !busy && _reset.IsEnabled;
        _close.IsEnabled = !busy;
        if (!busy) UpdateDirtyState();
    }

    private void ConfigureButton(Button button, string text, bool primary = false)
    {
        button.Content = text;
        button.MinWidth = 112;
        button.Height = 40;
        button.Margin = new Thickness(8, 0, 0, 0);
        if (primary)
        {
            button.Background = Brush("#86bdf8");
            button.Foreground = Brush("#111827");
        }
    }

    private void ApplyTheme()
    {
        Background = Brush(_isDark ? "#18181b" : "#f8fafc");
        Foreground = Brush(_isDark ? "#e5e7eb" : "#111827");
        _showLoading.Foreground = Foreground;
        foreach (var button in new[] { _customize, _reset, _close })
        {
            button.Background = Brush(_isDark ? "#34343c" : "#e5e7eb");
            button.Foreground = Foreground;
            button.BorderBrush = Brush(_isDark ? "#4b4b55" : "#aeb6c2");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
