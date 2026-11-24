using System.Windows.Media;

namespace Islet.Models;

/// <summary>Snapshot of what's playing. Position is the value at the moment the snapshot was taken.</summary>
public sealed record TrackInfo(
    string Title,
    string Artist,
    string Source,
    TimeSpan Duration,
    TimeSpan Position,
    bool IsPlaying,
    bool CanSeek,
    ImageSource? Cover,
    Color Accent);
