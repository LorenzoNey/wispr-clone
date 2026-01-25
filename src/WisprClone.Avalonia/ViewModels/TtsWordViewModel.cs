using CommunityToolkit.Mvvm.ComponentModel;

namespace WisprClone.ViewModels;

/// <summary>
/// ViewModel representing a single word for TTS highlighting display.
/// </summary>
public partial class TtsWordViewModel : ObservableObject
{
    /// <summary>
    /// The word text to display.
    /// </summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>
    /// Whether this word is currently being spoken.
    /// </summary>
    [ObservableProperty]
    private bool _isCurrentWord;

    /// <summary>
    /// Character position of this word in the original text.
    /// </summary>
    [ObservableProperty]
    private int _startIndex;

    /// <summary>
    /// Length of the word in characters.
    /// </summary>
    [ObservableProperty]
    private int _length;

    /// <summary>
    /// Whether this is a space/punctuation-only token (for proper spacing).
    /// </summary>
    [ObservableProperty]
    private bool _isWhitespace;

    /// <summary>
    /// Whether this represents a line break (newline).
    /// </summary>
    [ObservableProperty]
    private bool _isLineBreak;

    public TtsWordViewModel()
    {
    }

    public TtsWordViewModel(string text, int startIndex, int length, bool isWhitespace = false, bool isLineBreak = false)
    {
        _text = text;
        _startIndex = startIndex;
        _length = length;
        _isWhitespace = isWhitespace;
        _isLineBreak = isLineBreak;
    }
}
