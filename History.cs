namespace KareNot;

// Undo / redo by snapshots of one video's notes taken before each change.
sealed class UndoHistory
{
    readonly List<(string Video, List<Note> Notes, string Label)> undo = new(), redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Record(NoteStore store, string video, string label)
    {
        if (video == null) return;
        undo.Add((video, store.Snapshot(video), label));
        if (undo.Count > 200) undo.RemoveAt(0);
        redo.Clear();
    }

    // Returns the label and video of the undone change, or null when there is nothing to undo.
    public (string Label, string Video)? Undo(NoteStore store) => Move(store, undo, redo);
    public (string Label, string Video)? Redo(NoteStore store) => Move(store, redo, undo);

    static (string, string)? Move(NoteStore store, List<(string Video, List<Note> Notes, string Label)> from, List<(string Video, List<Note> Notes, string Label)> to)
    {
        if (from.Count == 0) return null;
        var (video, notes, label) = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add((video, store.Snapshot(video), label));
        store.Restore(video, notes);
        return (label, video);
    }
}
