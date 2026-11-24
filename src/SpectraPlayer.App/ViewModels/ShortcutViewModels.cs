using System.Collections.ObjectModel;
using System.ComponentModel;
using SpectraPlayer.App.Input;
using SpectraPlayer.Core.Shortcuts;

namespace SpectraPlayer.App.ViewModels;

/// <summary>One key an action can be triggered by, shown as a chip in Settings.</summary>
public sealed record ShortcutChip(ShortcutActionRow Row, Gesture Gesture)
{
    public string Text => string.Join(" + ", Gesture.Caps);
}

/// <summary>One action in the Settings list: its name, its keys, and whether it is being re-assigned right now.</summary>
public sealed class ShortcutActionRow(PlayerAction action, string title) : INotifyPropertyChanged
{
    private bool _isRecording;

    public PlayerAction Action { get; } = action;
    public string Title { get; } = title;
    public ObservableCollection<ShortcutChip> Chips { get; } = [];

    public bool HasNoKey => Chips.Count == 0;
    public bool IsCustomised => KeyMap.Current.IsCustomised(Action.ToString());

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isRecording == value) return;
            _isRecording = value;
            Raise(nameof(IsRecording));
        }
    }

    /// <summary>Re-reads the keys from the shortcut table.</summary>
    public void Refresh()
    {
        Chips.Clear();
        foreach (var gesture in KeyMap.BindingsOf(Action)) Chips.Add(new ShortcutChip(this, gesture));
        Raise(nameof(HasNoKey));
        Raise(nameof(IsCustomised));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ShortcutActionGroup(string Title, IReadOnlyList<ShortcutActionRow> Rows);
