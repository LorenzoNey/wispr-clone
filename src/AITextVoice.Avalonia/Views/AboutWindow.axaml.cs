using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Reflection;
using System.Threading.Tasks;
using AITextVoice.Services.Interfaces;

namespace AITextVoice.Views;

public partial class AboutWindow : Window
{
    private readonly IUpdateService? _updateService;
    private string? _downloadedFilePath;

    public AboutWindow()
    {
        InitializeComponent();

        // Set version from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
        }

        UpdateStatusText.Text = "Update service not available";
    }

    public AboutWindow(IUpdateService updateService) : this()
    {
        _updateService = updateService;
        CheckForUpdates();
    }

    private async void CheckForUpdates()
    {
        if (_updateService == null)
        {
            UpdateStatusText.Text = "Update service not available";
            return;
        }

        UpdateStatusText.Text = "Checking for updates...";

        try
        {
            var hasUpdate = await _updateService.CheckForUpdatesAsync();
            if (hasUpdate && _updateService.LatestVersion != null)
            {
                var latest = _updateService.LatestVersion;
                UpdateStatusText.Text = $"Update available: v{latest.Major}.{latest.Minor}.{latest.Build}";
                UpdateStatusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF28A745"));
                UpdateButton.IsVisible = true;
            }
            else
            {
                UpdateStatusText.Text = "You're running the latest version";
                UpdateStatusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFB0B0B0"));
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Failed to check: {ex.Message}";
        }
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;

        UpdateButton.IsEnabled = false;
        UpdateProgressBar.IsVisible = true;
        UpdateProgressBar.Value = 0;
        UpdateStatusText.Text = "Downloading update...";

        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateProgressBar.Value = p;
                UpdateStatusText.Text = $"Downloading... {p:F0}%";
            });
        });

        try
        {
            var filePath = await _updateService.DownloadUpdateAsync(progress);
            if (filePath != null)
            {
                UpdateProgressBar.Value = 100;
                _downloadedFilePath = filePath;

                if (OperatingSystem.IsMacOS())
                {
                    // macOS: Immediately install
                    UpdateStatusText.Text = "Installing update... The app will restart.";

                    await Task.Delay(500);

                    _updateService.LaunchMacOSUpdate(filePath, () =>
                    {
                        Environment.Exit(0);
                    });
                }
                else
                {
                    // Windows/Linux: Launch installer
                    UpdateStatusText.Text = "Download complete! Opening installer...";
                    _updateService.LaunchInstaller(filePath);
                    UpdateButton.IsEnabled = true;
                }
            }
            else
            {
                UpdateStatusText.Text = "Download failed. Please try again.";
                UpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Download failed: {ex.Message}";
            UpdateButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UserGuideButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/LorenzoNey/aitextvoice/blob/main/docs/USER_GUIDE.md");
    }

    private void GitHubButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/LorenzoNey/aitextvoice");
    }

    private static void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Silently fail if unable to open browser
        }
    }
}
