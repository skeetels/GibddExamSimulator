using System.Windows.Media;

namespace GibddExamSimulator.ViewModels;

public enum PageKind
{
    Loading,
    Pairing,
    Home,
    Ready,
    Exam,
    Supplementary,
    Result,
    Review
}

public sealed class QuestionNavigationItem : ObservableObject
{
    private Brush _background = Brushes.White;
    private Brush _foreground = Brushes.Black;
    private Brush _border = new SolidColorBrush(Color.FromRgb(92, 103, 112));

    public required int Index { get; init; }
    public required string Number { get; init; }
    public Brush Background { get => _background; set => SetProperty(ref _background, value); }
    public Brush Foreground { get => _foreground; set => SetProperty(ref _foreground, value); }
    public Brush Border { get => _border; set => SetProperty(ref _border, value); }
}

public sealed class AnswerChoiceItem : ObservableObject
{
    private Brush _background = Brushes.White;
    private Brush _border = new SolidColorBrush(Color.FromRgb(169, 177, 184));
    private bool _isEnabled = true;

    public required int Number { get; init; }
    public required string Text { get; init; }
    public string DisplayText => $"{Number}. {Text}";
    public Brush Background { get => _background; set => SetProperty(ref _background, value); }
    public Brush Border { get => _border; set => SetProperty(ref _border, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}

public sealed class ExamQuestionPreviewItem
{
    public required int Index { get; init; }
    public required int Number { get; init; }
    public required string QuestionText { get; init; }
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
    public required string ProgressText { get; init; }
    public required Brush ProgressBrush { get; init; }
    public required Brush Background { get; init; }
    public required Brush Border { get; init; }
}

public sealed record ReviewErrorItem(
    int Sequence,
    string Heading,
    string QuestionText,
    string SelectedAnswer,
    string CorrectAnswer,
    string Explanation,
    string? ImagePath,
    long ResponseTimeMs);

public sealed record PairedDeviceItem(
    Guid DeviceId,
    string Title,
    string LastActivity,
    bool IsCurrentDevice,
    string ActionCaption);
