using System;
using System.Collections.Generic;


// JsonUtility writes an enum as its int, so these values are pinned: never reorder or renumber them
public enum NotificationType
{
    CompletePuzzle = 0,
    FailPuzzle = 1,
}


// A notification is pure data. What opening one does is decided by its type alone, in
// NotificationManager, so it behaves the same after a reload as it did when it was raised.
[Serializable]
public class NotificationSave
{
    public NotificationType type;
    public string targetEntityGuid;     // an EntityId guid, resolved through EntityManager; may be empty
    public string puzzleId;
}


// every notification the player has not opened yet
[Serializable]
public class NotificationSaveData
{
    public List<NotificationSave> pending = new List<NotificationSave>();


    // (type, puzzleId) is the identity of a notification. -1 when there is none.
    // Sort reorders the list on every save, so an index is only good until the next Save()
    public int IndexOf(NotificationType type, string puzzleId)
    {
        if (pending == null) return -1;

        for (int i = 0; i < pending.Count; i++)
        {
            NotificationSave entry = pending[i];
            if (entry == null) continue;

            if (entry.type == type && entry.puzzleId == puzzleId) return i;
        }

        return -1;
    }


    // Deterministic output (identical worlds write identical files)
    public void Sort()
    {
        if (pending == null) return;

        pending.Sort((a, b) =>
        {
            int byPuzzle = string.CompareOrdinal(SafePuzzleId(a), SafePuzzleId(b));
            if (byPuzzle != 0) return byPuzzle;

            return SafeType(a).CompareTo(SafeType(b));
        });
    }


    private static string SafePuzzleId(NotificationSave entry)
    {
        return entry != null && entry.puzzleId != null ? entry.puzzleId : "";
    }


    private static int SafeType(NotificationSave entry)
    {
        return entry != null ? (int)entry.type : -1;
    }
}
