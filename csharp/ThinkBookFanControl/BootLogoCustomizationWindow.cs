using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
    private readonly FrameworkElement _busy;
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
        _busy = CreateSpinner(44, Brush("#8b95a5"));
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
        ConfigureButton(_confirm, _t("Confirm"));
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
        _reset.HorizontalAlignment = HorizontalAlignment.Left;
        _reset.MinWidth = 152;
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
            var bootSpinner = CreateSpinner(38, Brush("#30343b"));
            bootSpinner.HorizontalAlignment = HorizontalAlignment.Center;
            bootSpinner.VerticalAlignment = VerticalAlignment.Bottom;
            bootSpinner.Margin = new Thickness(0, 0, 0, 24);
            _preview.Children.Add(bootSpinner);
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

    private void ConfigureButton(Button button, string text)
    {
        button.Content = text;
        button.MinWidth = 112;
        button.Height = 40;
        button.Margin = new Thickness(8, 0, 0, 0);
        button.Padding = new Thickness(18, 4, 18, 4);
    }

    private void ApplyTheme()
    {
        Background = Brush(_isDark ? "#18181b" : "#f8fafc");
        Foreground = Brush(_isDark ? "#e5e7eb" : "#111827");
        _showLoading.Foreground = Foreground;
        foreach (var button in new[] { _customize, _reset, _close, _confirm })
        {
            button.Style = CreateButtonStyle();
        }
    }

    private Style CreateButtonStyle()
    {
        var normal = Brush(_isDark ? "#374151" : "#e5e7eb");
        var hover = Brush(_isDark ? "#465365" : "#d7dce3");
        var pressed = Brush(_isDark ? "#2b3442" : "#c7cdd6");
        var borderBrush = Brush(_isDark ? "#4b5563" : "#aeb6c2");

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hover));
        template.Triggers.Add(hoverTrigger);
        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, pressed));
        template.Triggers.Add(pressedTrigger);
        var disabledTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(Button.OpacityProperty, 0.42));
        template.Triggers.Add(disabledTrigger);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Button.BackgroundProperty, normal));
        style.Setters.Add(new Setter(Button.ForegroundProperty, Foreground));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, borderBrush));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Button.TemplateProperty, template));
        return style;
    }

    private static FrameworkElement CreateSpinner(double size, Brush color)
    {
        const int dotCount = 8;
        var canvas = new Canvas
        {
            Width = size,
            Height = size,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var dotSize = Math.Max(3, size * 0.11);
        var radius = (size - dotSize) / 2;
        var center = size / 2;
        for (var index = 0; index < dotCount; index++)
        {
            var angle = index * Math.PI * 2 / dotCount;
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = color,
                Opacity = 0.2 + 0.8 * (index + 1) / dotCount
            };
            Canvas.SetLeft(dot, center + Math.Sin(angle) * radius - dotSize / 2);
            Canvas.SetTop(dot, center - Math.Cos(angle) * radius - dotSize / 2);
            canvas.Children.Add(dot);
        }

        var rotation = new RotateTransform();
        canvas.RenderTransform = rotation;
        rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
        return canvas;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
