using System.Windows.Media;
using Islet.ViewModels;

namespace Islet.Models;

public sealed class NotificationItem : ViewModelBase
{
    private bool _isRead;

    /// <summary>Windows notification id (0 for notifications Islet made itself).</summary>
    public uint Id { get; init; }
    public string AppName { get; init; } = "";
    /// <summary>AppUserModelID of the sender, used to open the app on click.</summary>
    public string AppId { get; init; } = "";
    public string Badge { get; init; } = "";
    public Brush BadgeBrush { get; init; } = Brushes.Gray;
    /// <summary>App logo from Windows, or null to show the letter badge.</summary>
    public ImageSource? Icon { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Where it was said, e.g. Discord "#社課 · 一般"; empty for direct messages.</summary>
    public string Context { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTime Time { get; init; } = DateTime.Now;

    public bool HasIcon => Icon is not null;
    public bool HasNoIcon => Icon is null;
    public bool HasBody => Body.Length > 0;
    public bool HasContext => Context.Length > 0;

    public string TimeText => Time.Date == DateTime.Today ? Time.ToString("HH:mm") : Time.ToString("M/d HH:mm");

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
