using System.Collections.Generic;
using UnityEngine;


// Done once there is no wasteland left to see: every top-of-column cell above the risen sea is
// something else. Only tops count, since a cell buried under a cliff never changes and would make it
// impossible. The count is a whole-map scan, so it runs on events (a placement, a spread, the load and
// unlock catch-up), never per frame.
//
// Credited to the cell that changed last; caught up after a load or an unlock, the last of the
// player's grass, and failing that the book.
//
// While it is active it raises the worm's mob cap per spawn rule, so there are enough worms to finish the
// job: breeding to one cap and the 3x3 patch to a higher one, so breeding can't fill the room a player's
// patch needs. A patch is the only way a worm reaches the island worms can't burrow across to.
//
// Once its dirt and water are spent it isn't judged stuck while a live worm can still reach some
// wasteland the spreading rules can still green (CanStillProgress). That can come back after a flag (a
// worm is born within reach), so it un-flags rather than skips.
[CreateAssetMenu(
    fileName = "Its A Small World",
    menuName = "Puzzles/Its A Small World"
)]
public class ItsASmallWorld : ScriptedPuzzle
{
    private static readonly Vector2Int[] ORTHOGONAL =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };


    [SerializeField] private TerrainTile wasteland;

    [Tooltip("the placed item whose tile is grass (water, watered onto dirt); its cells stand in for the credit after a load or an unlock")]
    [SerializeField] private GameItem grass;

    [Tooltip("the animal that greens wasteland; its prefab's WormController gives the spreading and burrowing rules")]
    [SerializeField] private Animal worm;

    [Tooltip("mob caps per spawn rule while this is active. a rule not listed keeps its animal's own cap")]
    [SerializeField] private List<RuleCap> spawnCaps = new List<RuleCap>();


    [System.Serializable]
    private struct RuleCap
    {
        public AnimalSpawnRule rule;
        [Min(0)] public int cap;
    }


    [System.NonSerialized] private bool hasLastCell;
    [System.NonSerialized] private Vector3Int lastCell;
    [System.NonSerialized] private bool loggedUnusable;

    // one entry per column, reused across scans: its top level (-1 for none), its top tile, and whether
    // the risen sea is over it
    [System.NonSerialized] private int originX;
    [System.NonSerialized] private int originZ;
    [System.NonSerialized] private int width;
    [System.NonSerialized] private int depth;
    [System.NonSerialized] private int[] tops;
    [System.NonSerialized] private TerrainTile[] topTiles;
    [System.NonSerialized] private bool[] underSea;

    // scratch for the two reach walks
    [System.NonSerialized] private bool[] spreadable;
    [System.NonSerialized] private bool[] visited;
    [System.NonSerialized] private List<int> frontier;
    [System.NonSerialized] private List<AnimalController> scratchWorms;
    [System.NonSerialized] private List<Vector3Int> scratchCells;


    // MARK: events


    public override void OnItemPlaced(WorldContext context, GameItem item, Vector3Int cell)
    {
        lastCell = cell;
        hasLastCell = true;

        TryComplete(context);
    }


    // a worm's dirt is the only thing that takes wasteland away by itself
    public override void OnTerrainMutated(WorldContext context, Vector3Int cell, TerrainTile from, TerrainTile to)
    {
        if (from != wasteland) return;

        lastCell = cell;
        hasLastCell = true;

        TryComplete(context);
    }


    public override int MobCapFor(AnimalSpawnRule rule)
    {
        if (rule == null || spawnCaps == null) return 0;

        int cap = 0;

        for (int i = 0; i < spawnCaps.Count; i++)
        {
            if (spawnCaps[i].rule == rule) cap = Mathf.Max(cap, spawnCaps[i].cap);
        }

        return cap;
    }


    // MARK: completion


    protected override bool IsSatisfied(WorldContext context, out PuzzleCredit credit)
    {
        credit = PuzzleCredit.None;

        if (wasteland == null)
        {
            LogUnusableOnce($"ItsASmallWorld: '{name}' has no wasteland tile, so it can never complete");
            return false;
        }

        if (!Scan(context.Terrain)) return false;
        if (CountVisibleWasteland() > 0) return false;

        credit = Credit(context);
        return true;
    }


    // the cell that changed last; caught up, the player's last grass, else the book
    private PuzzleCredit Credit(WorldContext context)
    {
        if (hasLastCell)
        {
            hasLastCell = false;
            return PuzzleCredit.Cell(lastCell);
        }

        if (grass != null)
        {
            if (scratchCells == null) scratchCells = new List<Vector3Int>();

            context.CollectPlacedCells(grass, scratchCells);
            if (scratchCells.Count > 0) return PuzzleCredit.Cell(scratchCells[scratchCells.Count - 1]);
        }

        EntityId book = CompendiumUIManager.Instance != null
            ? CompendiumUIManager.Instance.GetComponentInParent<EntityId>()
            : null;

        return book != null ? PuzzleCredit.Entity(book) : PuzzleCredit.None;
    }


    // MARK: soft lock


    // a live worm, and wasteland it can get to that the spreading rules can still green
    protected override bool CanStillProgress(WorldContext context)
    {
        WormController rules = WormRules();
        if (rules == null) return false;

        if (scratchWorms == null) scratchWorms = new List<AnimalController>();

        context.CollectAnimals(worm, scratchWorms);
        if (scratchWorms.Count == 0) return false;

        if (!Scan(context.Terrain)) return false;
        if (MarkSpreadable(rules) == 0) return false;

        bool reachable = WormsReachSpreadable(rules, scratchWorms, true);
        scratchWorms.Clear();

        return reachable;
    }


    private WormController WormRules()
    {
        if (worm == null || worm.Prefab == null) return null;

        WormController rules = worm.Prefab.GetComponent<WormController>();

        if (rules == null)
        {
            LogUnusableOnce($"ItsASmallWorld: '{name}' worm's prefab has no WormController, so it's judged stuck " +
                "the moment its items are spent");
        }

        return rules;
    }


    // MARK: scanning


    // one pass over every column's top. False with no terrain
    private bool Scan(TerrainManager terrain)
    {
        if (terrain == null) return false;

        terrain.GetWorldXZBounds(out Vector2 min, out Vector2 max);

        originX = Mathf.RoundToInt(min.x);
        originZ = Mathf.RoundToInt(min.y);
        width = Mathf.RoundToInt(max.x) - originX;
        depth = Mathf.RoundToInt(max.y) - originZ;

        int size = Mathf.Max(0, width * depth);

        if (tops == null || tops.Length != size)
        {
            tops = new int[size];
            topTiles = new TerrainTile[size];
            underSea = new bool[size];
            spreadable = new bool[size];
            visited = new bool[size];
        }

        // the level the sea settles at, not the live sheet: a level it is rising over floods soon
        // anyway, and the spread already refuses it
        SeaLevel sea = SeaLevel.Instance;
        bool risen = Application.isPlaying && sea != null && sea.IsRisen;

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = z * width + x;
                int top = terrain.GetTopLevel(originX + x, originZ + z);

                tops[i] = top;
                topTiles[i] = top >= 0 ? terrain.GetTile(new Vector3Int(originX + x, top, originZ + z)) : null;
                underSea[i] = top >= 0 && risen && terrain.GetLevelHeight(top) < sea.Y;
            }
        }

        return true;
    }


    private bool IsVisibleWasteland(int i)
    {
        return tops[i] >= 0 && !underSea[i] && topTiles[i] == wasteland;
    }


    private int CountVisibleWasteland()
    {
        int count = 0;

        for (int i = 0; i < tops.Length; i++)
        {
            if (IsVisibleWasteland(i)) count++;
        }

        return count;
    }


    // Marks the wasteland the spreading rules can still reach (TerrainSpread.CanSpread): it touches the
    // frontier orthogonally within a level, or touches wasteland that does, since that turns into
    // frontier once it's dirt. Returns how many
    private int MarkSpreadable(WormController rules)
    {
        if (frontier == null) frontier = new List<int>();
        frontier.Clear();

        for (int i = 0; i < tops.Length; i++)
        {
            spreadable[i] = false;
            if (!IsVisibleWasteland(i)) continue;

            if (TouchesFrontier(i, rules))
            {
                spreadable[i] = true;
                frontier.Add(i);
            }
        }

        // a flood out through wasteland from there; frontier doubles as the queue
        for (int head = 0; head < frontier.Count; head++)
        {
            int from = frontier[head];
            int fx = from % width;
            int fz = from / width;

            for (int d = 0; d < ORTHOGONAL.Length; d++)
            {
                int x = fx + ORTHOGONAL[d].x;
                int z = fz + ORTHOGONAL[d].y;
                if (x < 0 || z < 0 || x >= width || z >= depth) continue;

                int i = z * width + x;
                if (spreadable[i] || !IsVisibleWasteland(i)) continue;
                if (Mathf.Abs(tops[i] - tops[from]) > 1) continue;

                spreadable[i] = true;
                frontier.Add(i);
            }
        }

        return frontier.Count;
    }


    // TerrainSpread's frontier test: an orthogonal top within a level holding one of Spread Next To
    // (or Spread To when that's empty). The neighbour may be under the sea: the riverbed counts
    private bool TouchesFrontier(int i, WormController rules)
    {
        int cx = i % width;
        int cz = i / width;

        for (int d = 0; d < ORTHOGONAL.Length; d++)
        {
            int x = cx + ORTHOGONAL[d].x;
            int z = cz + ORTHOGONAL[d].y;
            if (x < 0 || z < 0 || x >= width || z >= depth) continue;

            int n = z * width + x;
            if (tops[n] < 0 || Mathf.Abs(tops[n] - tops[i]) > 1) continue;

            if (IsFrontierTile(topTiles[n], rules)) return true;
        }

        return false;
    }


    private static bool IsFrontierTile(TerrainTile tile, WormController rules)
    {
        if (tile == null) return false;

        TerrainTile[] nextTo = rules.SpreadNextTo;
        if (nextTo == null || nextTo.Length == 0) return tile == rules.SpreadTo;

        for (int i = 0; i < nextTo.Length; i++)
        {
            if (nextTo[i] == tile) return true;
        }

        return false;
    }


    // Walks out from each worm's column the way a worm gets about: coming up anywhere it can stand
    // within its burrow radius, on any level. True once it reaches spreadable wasteland, or stands next
    // to some, since a worm churns the eight columns around where it surfaces. Without stopEarly it walks
    // everywhere a worm can get to, leaving it in visited, for the debug count
    private bool WormsReachSpreadable(WormController rules, List<AnimalController> worms, bool stopEarly)
    {
        for (int i = 0; i < visited.Length; i++) visited[i] = false;

        frontier.Clear();

        for (int w = 0; w < worms.Count; w++)
        {
            if (worms[w] == null) continue;

            Vector3 at = worms[w].transform.position;
            int x = Mathf.FloorToInt(at.x) - originX;
            int z = Mathf.FloorToInt(at.z) - originZ;
            if (x < 0 || z < 0 || x >= width || z >= depth) continue;

            int i = z * width + x;
            if (visited[i]) continue;

            visited[i] = true;
            frontier.Add(i);
        }

        int reach = Mathf.Max(1, rules.BurrowRadius);

        for (int head = 0; head < frontier.Count; head++)
        {
            int from = frontier[head];
            int fx = from % width;
            int fz = from / width;

            if (stopEarly && NextToSpreadable(fx, fz)) return true;

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = fx + dx;
                    int z = fz + dz;
                    if (x < 0 || z < 0 || x >= width || z >= depth) continue;

                    int i = z * width + x;
                    if (visited[i]) continue;
                    if (tops[i] < 0 || underSea[i] || !rules.CanStandOn(topTiles[i])) continue;

                    visited[i] = true;
                    frontier.Add(i);
                }
            }
        }

        return false;
    }


    // the column or one of the eight around it
    private bool NextToSpreadable(int cx, int cz)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx;
                int z = cz + dz;
                if (x < 0 || z < 0 || x >= width || z >= depth) continue;

                if (spreadable[z * width + x]) return true;
            }
        }

        return false;
    }


    // MARK: debug


    // how much is left, and whether all of it can still be greened. Play mode only: it reads the live
    // terrain and worms
    [ContextMenu("Log Wasteland")]
    private void LogWasteland()
    {
        WorldEvents context = WorldEvents.Instance;

        if (!Application.isPlaying || context == null || !Scan(context.Terrain))
        {
            Debug.LogWarning($"ItsASmallWorld: '{name}' can only count wasteland in play mode, with a world loaded", this);
            return;
        }

        int remaining = CountVisibleWasteland();

        WormController rules = WormRules();

        if (rules == null)
        {
            Debug.Log($"ItsASmallWorld: '{name}' {remaining} visible wasteland left; no worm rules to judge the rest by", this);
            return;
        }

        int spreadableCount = MarkSpreadable(rules);

        // how many of the spreadable ones some live worm can get to
        if (scratchWorms == null) scratchWorms = new List<AnimalController>();
        context.CollectAnimals(worm, scratchWorms);

        int wormReachable = CountWormReachable(rules, scratchWorms);
        int wormCount = scratchWorms.Count;
        scratchWorms.Clear();

        Debug.Log($"ItsASmallWorld: '{name}' {remaining} visible wasteland left. " +
            $"{remaining - spreadableCount} can never be greened by spreading (islands, cliffs of 2+ levels, " +
            $"under the sea); {spreadableCount - wormReachable} more that spreading could green but none of " +
            $"the {wormCount} live worms can reach", this);
    }


    // spreadable cells within a worm's churn of anywhere a live worm can get to
    private int CountWormReachable(WormController rules, List<AnimalController> worms)
    {
        WormsReachSpreadable(rules, worms, false);

        int count = 0;

        for (int i = 0; i < tops.Length; i++)
        {
            if (spreadable[i] && VisitedAround(i % width, i / width)) count++;
        }

        return count;
    }


    private bool VisitedAround(int cx, int cz)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx;
                int z = cz + dz;
                if (x < 0 || z < 0 || x >= width || z >= depth) continue;

                if (visited[z * width + x]) return true;
            }
        }

        return false;
    }


    // once, not on every event: an unfinished asset would otherwise fill the console on every placement
    private void LogUnusableOnce(string message)
    {
        if (loggedUnusable) return;

        loggedUnusable = true;
        Debug.LogError(message, this);
    }


    // MARK: validation


    private void OnValidate()
    {
        if (wasteland == null) Debug.LogError($"ItsASmallWorld: '{name}' has no wasteland tile, so it can never complete", this);
        if (worm == null) Debug.LogError($"ItsASmallWorld: '{name}' has no worm, so its soft lock never waits for worms", this);

        for (int i = 0; spawnCaps != null && i < spawnCaps.Count; i++)
        {
            if (spawnCaps[i].rule == null) Debug.LogWarning($"ItsASmallWorld: '{name}' spawn cap {i} has no rule, so it raises nothing", this);
        }

        WormController rules = worm != null && worm.Prefab != null ? worm.Prefab.GetComponent<WormController>() : null;

        if (rules != null && wasteland != null && rules.SpreadFrom != wasteland)
        {
            Debug.LogWarning($"ItsASmallWorld: '{name}' worm spreads from '{(rules.SpreadFrom != null ? rules.SpreadFrom.name : "nothing")}', " +
                $"not '{wasteland.name}', so no worm will ever finish it", this);
        }
    }
}
