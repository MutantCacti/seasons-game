using System.Collections.Generic;
using System.Linq;
using UnityEngine;


//
[CreateAssetMenu(
    fileName = "Majority of Nine Patch",
    menuName = "Puzzles/Majority of Nine Patch"
)]
public class MajorityOfNinePatch : ScriptedPuzzle
{

    private static readonly Vector3Int[] CENTRE_OFFSETS =
    {
        //top row
        new Vector3Int(-1, 0, 1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1),
        //center row
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        //bottom row
        new Vector3Int(-1, 0, -1),
        new Vector3Int(0, 0, -1),
        new Vector3Int(1, 0, -1),
    };

    [SerializeField] private GameItem cattail;

    [System.NonSerialized] private List<Vector3> cattailPositions;
    [System.NonSerialized] private bool hasLastPlaced;
    [System.NonSerialized] private Vector3Int lastPlaced;


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        if (item != cattail) return;

        lastPlaced = cell;
        hasLastPlaced = true;

        TryComplete(context);
    }


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit) {
        credit = PuzzleCredit.None;

        if (cattail == null) {
            Debug.LogError("PlantSeeds: either or both seeds_1 and seeds_2 are null, so this puzzle can never complete", this);
            return false;
        }

        if (!Check3X3AroundPlaced(context)) return false;

        credit = PuzzleCredit.Cell(CreditCell(context));
        return true;
    }

    private bool Check3X3AroundPlaced(WorldContext context) {
        if (cattailPositions == null) cattailPositions = new List<Vector3>();
        cattailPositions.Clear();

        context.CollectPositionsOf(cattail, cattailPositions);

        if (cattailPositions.Count < 5) return false;

        HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();

        foreach (Vector3 position in cattailPositions) {
            occupied.Add(context.Terrain.WorldToCell(position));
        }


        foreach (Vector3Int cell in occupied) {
            if (IsMajority3x3(occupied, cell)) return true;
        }


        return false;
    }

    private bool IsMajority3x3(HashSet<Vector3Int> occupied, Vector3Int cell) {
        int count = 0;
        for (int i = 0; i < CENTRE_OFFSETS.Length; i++) {
            if (occupied.Contains(cell + CENTRE_OFFSETS[i])) count++;
        }

        if (count >= 5) return true;

        return false;
    }

    private Vector3Int CreditCell(WorldContext context) {
        if (hasLastPlaced) {
            hasLastPlaced = false;
            return lastPlaced;
        }

        return default;
    }
    
}
