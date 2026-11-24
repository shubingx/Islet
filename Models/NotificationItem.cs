using System.Windows.Media;
using Islet.ViewModels;

namespace Islet.Models;

public sealed class NotificationItem : ViewModelBase
{
    private bool _isRead;

    public string AppName { get; init; } = "";
    public string Badge { get; init; } = "";
    public Brush BadgeBrush { get; init; } = Brushes.Gray;
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTime Time { get; init; } = DateTime.Now;

    public string TimeText => Time.ToString("HH:mm");

    public bool IsRead
    {
        get => _isRead;
        set
        {
            if (Set(ref _isRead, value)) OnPropertyChanged(nameof(IsUnread));
        }
    }

    public bool IsUnread => !_isRead;
}
