namespace UMP.App.Services;

/// <summary>Commande reversible — stocke l'action et son inverse</summary>
public interface IUndoCommand
{
    void Execute();
    void Undo();
    string Description { get; }
    bool HasChanged => true;
}

/// <summary>Commande generique qui capture une propriete avant/apres modification</summary>
public class PropertyCommand<T> : IUndoCommand
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action? _onApply;
    public string Description { get; }

    public PropertyCommand(string description, Action<T> setter, T oldValue, T newValue, Action? onApply = null)
    {
        Description = description;
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
        _onApply = onApply;
    }

    public void Execute() { _setter(_newValue); _onApply?.Invoke(); }
    public void Undo() { _setter(_oldValue); _onApply?.Invoke(); }
}

/// <summary>Commande composite — groupe plusieurs commandes en une seule action Undo</summary>
public class CompositeCommand : IUndoCommand
{
    private readonly List<IUndoCommand> _commands = new();
    public string Description { get; }

    public CompositeCommand(string description) { Description = description; }

    public void Add(IUndoCommand cmd) => _commands.Add(cmd);
    public void Execute() { foreach (var c in _commands) c.Execute(); }
    public void Undo() { for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(); }
}

/// <summary>Commande ajout/suppression dans une liste</summary>
public class ListAddCommand<T> : IUndoCommand
{
    private readonly List<T> _list;
    private readonly T _item;
    private readonly int _index;
    private readonly Action? _onApply;
    public string Description { get; }

    public ListAddCommand(string description, List<T> list, T item, int index = -1, Action? onApply = null)
    {
        Description = description;
        _list = list;
        _item = item;
        _index = index < 0 ? list.Count : index;
        _onApply = onApply;
    }

    public void Execute() { _list.Insert(_index, _item); _onApply?.Invoke(); }
    public void Undo() { _list.Remove(_item); _onApply?.Invoke(); }
}

public class ListRemoveCommand<T> : IUndoCommand
{
    private readonly List<T> _list;
    private readonly T _item;
    private int _index;
    private readonly Action? _onApply;
    public string Description { get; }

    public ListRemoveCommand(string description, List<T> list, T item, Action? onApply = null)
    {
        Description = description;
        _list = list;
        _item = item;
        _index = list.IndexOf(item);
        _onApply = onApply;
    }

    public void Execute() { _index = _list.IndexOf(_item); _list.Remove(_item); _onApply?.Invoke(); }
    public void Undo() { _list.Insert(Math.Min(_index, _list.Count), _item); _onApply?.Invoke(); }
}

/// <summary>Service Undo/Redo base sur le pattern Command</summary>
public class UndoRedoService
{
    private readonly Stack<IUndoCommand> _undoStack = new();
    private readonly Stack<IUndoCommand> _redoStack = new();
    private const int MaxHistory = 100;

    public void Execute(IUndoCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        if (_undoStack.Count > MaxHistory)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = Math.Min(temp.Length - 1, MaxHistory - 1); i >= 0; i--)
                _undoStack.Push(temp[i]);
        }
    }

    /// <summary>Enregistrer une commande deja executee (pour les modifications faites par l'UI)</summary>
    public void Push(IUndoCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public void Clear() { _undoStack.Clear(); _redoStack.Clear(); }
}
