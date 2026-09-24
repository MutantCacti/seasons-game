using UnityEngine;


// The serialized slots of a Puzzle page: one CompendiumEntry per row, each with a PuzzleStatusRow
// beside it on the same object. How many rows fit is decided here, in the prefab, and
// PuzzleTabManager reads it from Entries.Length.
public class PuzzlePageContainer : MonoBehaviour
{
    [SerializeField] private CompendiumEntry[] entries;

    public CompendiumEntry[] Entries { get { return entries; } }
}
