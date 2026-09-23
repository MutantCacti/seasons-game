using System.Collections.Generic;
using UnityEngine;



[CreateAssetMenu(
    fileName = "PuzzleRegistry",
    menuName = "Puzzles/Puzzle Registry"
)]
public class PuzzleRegistry : ScriptableObject
{
    [SerializeField] private List<Puzzle> puzzles = new List<Puzzle>();

    // new worlds start with these
    [SerializeField] private List<Puzzle> startingPuzzles = new List<Puzzle>();


    [System.NonSerialized] private Dictionary<string, Puzzle> puzzleById;


    public IReadOnlyList<Puzzle> Puzzles { get { return puzzles; } }
    public IReadOnlyList<Puzzle> StartingPuzzles { get { return startingPuzzles; } }
    public int Count { get { return puzzles != null ? puzzles.Count : 0; } }


    // MARK: lookup


    public bool TryGetPuzzleById(string id, out Puzzle puzzle)
    {
        puzzle = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return puzzleById.TryGetValue(id, out puzzle) && puzzle != null;
    }


    public bool TryGetIdByPuzzle(Puzzle puzzle, out string id)
    {
        id = null;
        if (puzzle == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(puzzle.Id) &&
            puzzleById.TryGetValue(puzzle.Id, out Puzzle registered) &&
            registered == puzzle)
        {
            id = puzzle.Id;
            return true;
        }

        Debug.LogError($"PuzzleRegistry: '{puzzle.name}' is not registered or its id '{puzzle.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the puzzles list
    private void OnValidate()
    {
        puzzleById = null;
    }


    public void Rebuild()
    {
        puzzleById = new Dictionary<string, Puzzle>();
        if (puzzles == null) return;

        for (int i = 0; i < puzzles.Count; i++)
        {
            Puzzle puzzle = puzzles[i];

            if (puzzle == null)
            {
                Debug.LogError($"PuzzleRegistry: entry {i} is empty", this);
                continue;
            }

            if (string.IsNullOrEmpty(puzzle.Id))
            {
                Debug.LogError($"PuzzleRegistry: '{puzzle.name}' has no Id, so it can never be saved or loaded", puzzle);
                continue;
            }

            // first entry wins on duplicate
            if (puzzleById.TryGetValue(puzzle.Id, out Puzzle existing))
            {
                Debug.LogError($"PuzzleRegistry: duplicate Id '{puzzle.Id}' on '{puzzle.name}', keeping '{existing.name}' and ignoring this one", puzzle);
                continue;
            }

            puzzleById.Add(puzzle.Id, puzzle);
        }
    }


    private void EnsureLookup()
    {
        if (puzzleById != null) return;

        Rebuild();
    }
}
