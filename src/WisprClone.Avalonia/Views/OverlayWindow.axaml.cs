using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WisprClone.ViewModels;

namespace WisprClone.Views;

/// <summary>
/// Overlay window for displaying transcription (Avalonia version).
/// </summary>
public partial class OverlayWindow : Window
{
    private OverlayViewModel? _viewModel;

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

            // Bind window position changes only
            // Note: Show/Hide is handled by App.axaml.cs via ShowOverlayRequested/HideOverlayRequested events
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(OverlayViewModel.WindowLeft))
                    Position = new PixelPoint((int)vm.WindowLeft, Position.Y);
                else if (args.PropertyName == nameof(OverlayViewModel.WindowTop))
                    Position = new PixelPoint(Position.X, (int)vm.WindowTop);
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
            if (!string.IsNullOrWhiteSpace(text) && text != "Press Ctrl+Ctrl to start...")
            {
                await Clipboard.SetTextAsync(text);
            }
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Hide();
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
}
