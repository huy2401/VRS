using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using VSR.Services;

namespace VSR.Views;

/// <summary>Hộp thoại cập nhật phiên bản ứng dụng VSR.</summary>
public sealed class RequiredUpdateDialog : Window
{
    private readonly string _downloadUrl;
    private readonly UpdateService _updateService;

    private readonly Button _updateButton;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;
    private readonly TextBlock _errorText;

    public RequiredUpdateDialog(string message, string downloadUrl, UpdateService? updateService = null)
    {
        _downloadUrl = downloadUrl;
        _updateService = updateService ?? new UpdateService();

        Title = "Cập nhật VSR";
        Width = 520;
        Height = 270;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var titleBlock = new TextBlock
        {
            Text = "Có phiên bản VSR mới",
            FontSize = 21,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#1E3A8A"))
        };

        var messageBlock = new TextBlock
        {
            Text = $"{message}\nBạn cần cập nhật trước khi đăng nhập. Bản mới sẽ tự tải, thay thế VSR.exe và khởi động lại ứng dụng.",
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#334155"))
        };

        _progressBar = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#E2E8F0")),
            Foreground = new SolidColorBrush(Color.Parse("#2563EB")),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 4, 0, 0)
        };

        _statusText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Color.Parse("#2563EB")),
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _errorText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#DC2626")),
            IsVisible = false
        };

        _updateButton = new Button
        {
            Content = "📥 Tải và cập nhật ngay",
            Background = new SolidColorBrush(Color.Parse("#2563EB")),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Padding = new Thickness(20, 10),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        _updateButton.Click += async (_, _) => await StartUpdateAsync();

        var buttonContainer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { _updateButton }
        };

        Content = new Border
        {
            Padding = new Thickness(26),
            Background = Brushes.White,
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    titleBlock,
                    messageBlock,
                    _progressBar,
                    _statusText,
                    _errorText,
                    buttonContainer
                }
            }
        };
    }

    private async Task StartUpdateAsync()
    {
        _updateButton.IsEnabled = false;
        _updateButton.Content = "Đang cập nhật...";
        _errorText.IsVisible = false;
        _progressBar.IsVisible = true;
        _statusText.IsVisible = true;
        _statusText.Text = "Đang kết nối tải bản cập nhật...";

        var progress = new Progress<(long BytesRead, long TotalBytes, double Percentage)>(report =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (report.TotalBytes > 0)
                {
                    _progressBar.IsIndeterminate = false;
                    _progressBar.Value = Math.Clamp(report.Percentage, 0, 100);

                    double mbRead = report.BytesRead / (1024.0 * 1024.0);
                    double mbTotal = report.TotalBytes / (1024.0 * 1024.0);
                    _statusText.Text = $"Đang tải: {mbRead:F1} MB / {mbTotal:F1} MB ({report.Percentage:F0}%)...";
                }
                else
                {
                    _progressBar.IsIndeterminate = true;
                    double mbRead = report.BytesRead / (1024.0 * 1024.0);
                    _statusText.Text = $"Đang tải: {mbRead:F1} MB...";
                }
            });
        });

        var (success, message) = await _updateService.DownloadAndInstallAsync(_downloadUrl, progress);

        if (success)
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = 100;
            _statusText.Text = "Tải hoàn tất! Đang khởi động lại ứng dụng...";
            _statusText.Foreground = new SolidColorBrush(Color.Parse("#16A34A"));

            await Task.Delay(1200);

            // Thoát ứng dụng để tiến trình updater cmd ghi đè VSR.exe và tự bật lại
            Environment.Exit(0);
        }
        else
        {
            _progressBar.IsVisible = false;
            _statusText.IsVisible = false;
            _errorText.Text = $"Cập nhật thất bại: {message}";
            _errorText.IsVisible = true;
            _updateButton.IsEnabled = true;
            _updateButton.Content = "Thử lại tải và cập nhật";
        }
    }
}
