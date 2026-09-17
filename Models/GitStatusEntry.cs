using Avalonia.Media;

namespace Vetala.Models;

public sealed record GitStatusEntry(
    string AbsolutePath,
    string RelativePath,
    char StatusCode,
    string StatusText)
{
    private static readonly IBrush DeletedBrush = new SolidColorBrush(Color.Parse("#C74E39"));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#73C991"));
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#E2C08D"));

    public string BadgeText => StatusCode.ToString();

    public IBrush BadgeBrush => StatusCode switch
    {
        'D' => DeletedBrush,
        'A' or 'U' or 'R' or 'C' => GreenBrush,
        _ => ModifiedBrush
    };
}
