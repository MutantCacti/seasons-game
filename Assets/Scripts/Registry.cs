using System.Collections.Generic;


// The shared surface of every id-keyed ScriptableObject registry:
// an ordered entry list plus a string-id lookup in both directions.
public interface Registry<T>
{
    IReadOnlyList<T> Entries { get; }
    int Count { get; }
    bool TryGetById(string id, out T entry);
    bool TryGetIdByEntry(T entry, out string id);
    void Rebuild();
}
