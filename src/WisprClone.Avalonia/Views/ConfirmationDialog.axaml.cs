using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WisprClone.Views;

/// <summary>
/// A simple confirmation dialog window.
/// </summary>
public partial class ConfirmationDialog : Window
{
    public bool Result { get; private set; }

    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(string title, string message) : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        Title = title;
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void NoButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
