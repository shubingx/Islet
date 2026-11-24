using Islet.ViewModels;

namespace Islet.Models;

public sealed class TodoItem : ViewModelBase
{
    private bool _isDone;

    public string Text { get; init; } = "";
    public string Due { get; init; } = "";

    public bool IsDone
    {
        get => _isDone;
        set => Set(ref _isDone, value);
    }
}
