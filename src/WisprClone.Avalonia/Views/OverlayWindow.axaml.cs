using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WisprClone.ViewModels;

namespace WisprClone.Views;

/// <summary>
/// Converts bool (IsCurrentWord) to highlight foreground brush.
/// </summary>
public class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? HighlightBrush : NormalBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool (IsCurrentWord) to background brush.
/// </summary>
public class BoolToBackgroundBrushConverter : IValueConverter
{
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#40FFFF00"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? HighlightBrush : TransparentBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts bool (IsCurrentWord) to font weight.
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? FontWeight.Bold : FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts IsLineBreak bool to width/height for forcing line breaks in WrapPanel.
/// </summary>
public class LineBreakWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isLineBreak = value is true;
        string? param = parameter as string;

        if (isLineBreak)
        {
            // For line breaks: full width to force wrap, minimal height
            if (param == "height")
                return 4.0; // Small height for line spacing
            return 10000.0; // Very large width to force wrap
        }
        else
        {
            // For regular words: auto size
            return double.NaN;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Overlay window for displaying transcription (Avalonia version).
/// </summary>
public partial class OverlayWindow : Window
{
    private OverlayViewModel? _viewModel;
    private const double MinWindowHeight = 100;
    private const double MaxWindowHeight = 600;

    public OverlayWindow()
    {
        InitializeComponent();

        // Track mouse enter/leave for auto-hide logic
        PointerEntered += (_, _) =>
        {
            if (_viewModel != null)
                _viewModel.IsMouseOverWindow = true;
        };

        PointerExited += (_, _) =>
        {
            if (_viewModel != null)
                _viewModel.IsMouseOverWindow = false;
        };

        // Track position changes
        PositionChanged += OnWindowPositionChanged;

        // Listen to TextBlock size changes for auto-resize
        TranscriptionTextBlock.PropertyChanged += (_, args) =>
        {
            if (args.Property.Name == "Bounds")
            {
                AdjustWindowHeight();
            }
        };
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.WindowLeft = e.Point.X;
            _viewModel.WindowTop = e.Point.Y;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is OverlayViewModel vm)
        {
            _viewModel = vm;

            // Bind window position changes and auto-scroll on text change
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(OverlayViewModel.WindowLeft))
                    Position = new PixelPoint((int)vm.WindowLeft, Position.Y);
                else if (args.PropertyName == nameof(OverlayViewModel.WindowTop))
                    Position = new PixelPoint(Position.X, (int)vm.WindowTop);
                else if (args.PropertyName == nameof(OverlayViewModel.TranscriptionText))
                    ScrollToBottom();
                else if (args.PropertyName == nameof(OverlayViewModel.IsListening) && vm.IsListening)
                    ResetWindowHeight();
                else if (args.PropertyName == nameof(OverlayViewModel.CurrentWordIndex))
                    ScrollToCurrentWord(vm.CurrentWordIndex);
            };

            // Set initial position
            Position = new PixelPoint((int)vm.WindowLeft, (int)vm.WindowTop);
        }
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null && Clipboard != null)
        {
            var text = _viewModel.TranscriptionText;
            if (!string.IsNullOrWhiteSpace(text) &&
                text != "Press Ctrl+Ctrl to start..." &&
                text != "Ctrl+Ctrl: STT | Shift+Shift: TTS" &&
                text != "Listening...")
            {
                await Clipboard.SetTextAsync(text);
            }
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Hide();
    }

    private void PauseResumeButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleTtsPauseResume();
    }

    private void StopTtsButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.StopTts();
    }

    private void RunTtsButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.RunTts();
    }

    private void ComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.IsDropdownOpen = true;
    }

    private void ComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.IsDropdownOpen = false;
    }

    private void ScrollToBottom()
    {
        // Scroll the ScrollViewer to the bottom to show latest text
        Dispatcher.UIThread.Post(() =>
        {
            TranscriptionScrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background); // Use Background priority to let layout complete first
    }

    private void ScrollToCurrentWord(int wordIndex)
    {
        if (wordIndex < 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Get the ItemsControl container
                var itemsControl = TtsWordDisplay;
                if (itemsControl == null || itemsControl.ItemCount == 0)
                {
                    Log($"[Scroll] ItemsControl null or empty. wordIndex={wordIndex}");
                    return;
                }

                var scrollViewer = TtsScrollViewer;

                // Try to get the container for the current word
                var container = itemsControl.ContainerFromIndex(wordIndex);
                if (container is Control control)
                {
                    // Get control bounds relative to scroll viewer
                    var controlBounds = control.Bounds;

                    Log($"[Scroll] Word {wordIndex}/{itemsControl.ItemCount}: " +
                        $"Control bounds=({controlBounds.X:F1},{controlBounds.Y:F1},{controlBounds.Width:F1},{controlBounds.Height:F1}), " +
                        $"ScrollViewer: Offset={scrollViewer?.Offset.Y:F1}, Viewport={scrollViewer?.Viewport.Height:F1}, Extent={scrollViewer?.Extent.Height:F1}");

                    // Check if control is visible in viewport
                    if (scrollViewer != null)
                    {
                        // Get the position of the control relative to the ScrollViewer's content
                        var transform = control.TransformToVisual(scrollViewer);
                        if (transform != null)
                        {
                            var posInScrollViewer = transform.Value.Transform(new Point(0, 0));
                            var bottomOfControl = posInScrollViewer.Y + controlBounds.Height;
                            var viewportBottom = scrollViewer.Viewport.Height;

                            Log($"[Scroll] Control position in ScrollViewer: Y={posInScrollViewer.Y:F1}, " +
                                $"BottomOfControl={bottomOfControl:F1}, ViewportBottom={viewportBottom:F1}, " +
                                $"IsVisible={(posInScrollViewer.Y >= 0 && bottomOfControl <= viewportBottom)}");

                            // If control bottom is below viewport, scroll down
                            if (bottomOfControl > viewportBottom)
                            {
                                var newOffset = scrollViewer.Offset.Y + (bottomOfControl - viewportBottom) + 10; // +10 padding
                                Log($"[Scroll] Scrolling down: newOffset={newOffset:F1}");
                                scrollViewer.Offset = new Vector(0, newOffset);
                            }
                            // If control top is above viewport, scroll up
                            else if (posInScrollViewer.Y < 0)
                            {
                                var newOffset = scrollViewer.Offset.Y + posInScrollViewer.Y - 10; // -10 padding
                                Log($"[Scroll] Scrolling up: newOffset={newOffset:F1}");
                                scrollViewer.Offset = new Vector(0, Math.Max(0, newOffset));
                            }
                        }
                        else
                        {
                            Log($"[Scroll] TransformToVisual returned null, using BringIntoView");
                            control.BringIntoView();
                        }
                    }
                    else
                    {
                        // Fallback to BringIntoView
                        control.BringIntoView();
                    }
                }
                else
                {
                    Log($"[Scroll] No container for word {wordIndex}, using fallback scroll");
                    // Fallback: estimate scroll position based on word index
                    if (scrollViewer != null && itemsControl.ItemCount > 0)
                    {
                        // Calculate approximate scroll position
                        var progress = (double)wordIndex / itemsControl.ItemCount;
                        var maxScroll = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
                        if (maxScroll > 0)
                        {
                            var newOffset = progress * maxScroll;
                            Log($"[Scroll] Fallback: progress={progress:F2}, maxScroll={maxScroll:F1}, newOffset={newOffset:F1}");
                            scrollViewer.Offset = new Vector(0, newOffset);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Scroll] Error: {ex.Message}");
            }
        });
    }

    private static void Log(string message)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WisprClone", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"wispr_{DateTime.Now:yyyy-MM-dd}.log");
            var logLine = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            File.AppendAllText(logFile, logLine + Environment.NewLine);
        }
        catch { }
    }

    private void ResetWindowHeight()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Height = MinWindowHeight;
        });
    }

    private void AdjustWindowHeight()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Calculate desired height based on content
            var desiredHeight = MainBorder.DesiredSize.Height + 20; // 20 for window margin
            desiredHeight = Math.Clamp(desiredHeight, MinWindowHeight, MaxWindowHeight);

            if (Math.Abs(Height - desiredHeight) > 5) // Only resize if difference is significant
            {
                Height = desiredHeight;
            }
        });
    }
}
