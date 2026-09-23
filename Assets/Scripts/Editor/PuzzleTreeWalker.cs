using System.Collections.Generic;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


// Walks the puzzle unlock graph from the starting puzzles, breadth-first, and groups the puzzles
// by depth. Both registry editors order their lists from this single walk so they cannot drift
// apart.
public static class PuzzleTreeWalker
{
    public class Result
    {
        public List<List<Puzzle>> Levels { get; private set; }
        public HashSet<Puzzle> Reached { get; private set; }


        public Result(List<List<Puzzle>> levels, HashSet<Puzzle> reached)
        {
            Levels = levels;
            Reached = reached;
        }
    }


    // index 0 = starting puzzles, index 1 = what they unlock, and so on. Each level is sorted
    // A->Z by asset name with duplicates removed. Reached holds every puzzle the walk visits, so
    // a registered puzzle missing from it can never be unlocked.
    public static Result Walk(IReadOnlyList<Puzzle> startingPuzzles)
    {
        Result result = new Result(new List<List<Puzzle>>(), new HashSet<Puzzle>());

        List<Puzzle> current = new List<Puzzle>();

        if (startingPuzzles != null)
        {
            for (int i = 0; i < startingPuzzles.Count; i++)
            {
                Puzzle puzzle = startingPuzzles[i];
                if (puzzle == null) continue;

                if (result.Reached.Add(puzzle)) current.Add(puzzle);
            }
        }

        current.Sort(CompareByName);

        while (current.Count > 0)
        {
            result.Levels.Add(current);

            List<Puzzle> next = new List<Puzzle>();

            for (int i = 0; i < current.Count; i++)
            {
                IReadOnlyList<Puzzle> unlocks = current[i].Unlocks;
                if (unlocks == null) continue;

                for (int j = 0; j < unlocks.Count; j++)
                {
                    Puzzle unlocked = unlocks[j];
                    if (unlocked == null) continue;
                    if (result.Reached.Contains(unlocked)) continue;

                    next.Add(unlocked);
                }
            }

            // a puzzle unlocked by two puzzles at this depth appears once, and the level is A->Z
            next.Sort(CompareByName);

            current = new List<Puzzle>();
            for (int i = 0; i < next.Count; i++)
            {
                if (i > 0 && next[i] == next[i - 1]) continue;

                current.Add(next[i]);
                result.Reached.Add(next[i]);
            }
        }

        return result;
    }


    private static int CompareByName(Puzzle a, Puzzle b)
    {
        return string.CompareOrdinal(a.name, b.name);
    }
}
