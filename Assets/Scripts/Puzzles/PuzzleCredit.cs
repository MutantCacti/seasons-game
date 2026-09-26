using System.Globalization;
using UnityEngine;


// Who or what a puzzle's completion is credited to: an entity (a tree, an animal) or a terrain cell
// (the dirt that finished Break Ground). The ping points at it and the compendium's row goes to it.
// None is allowed; the completion then just counts.
//
// It is saved wherever an entity guid is saved (completedBy, the pending queue, a notification's
// target), so the save format doesn't change: a cell is written "@x,levelMm,z", the same millimetre
// level key the terrain save uses, so it survives the level list being edited. Every reader of those
// strings asks IsCellKey first; an entity guid never starts with '@'.
public readonly struct PuzzleCredit
{
    private const char CELL_MARK = '@';


    public readonly EntityId entity;
    public readonly bool hasCell;
    public readonly Vector3Int cell;


    private PuzzleCredit(EntityId entity, bool hasCell, Vector3Int cell)
    {
        this.entity = entity;
        this.hasCell = hasCell;
        this.cell = cell;
    }


    public static PuzzleCredit None { get { return default; } }
    public static PuzzleCredit Entity(EntityId entity) { return new PuzzleCredit(entity, false, default); }
    public static PuzzleCredit Cell(Vector3Int cell) { return new PuzzleCredit(null, true, cell); }


    // MARK: keys


    public static bool IsCellKey(string key)
    {
        return !string.IsNullOrEmpty(key) && key[0] == CELL_MARK;
    }


    // null when the cell's level is not a level of this terrain
    public static string ToCellKey(Vector3Int cell, TerrainManager terrain)
    {
        if (terrain == null || cell.y < 0 || cell.y >= terrain.LevelCount) return null;

        int levelMm = TerrainManager.ToLevelMillimetres(terrain.GetLevelHeight(cell.y));

        return string.Format(CultureInfo.InvariantCulture, "{0}{1},{2},{3}", CELL_MARK, cell.x, levelMm, cell.z);
    }


    public static bool TryParseCellKey(string key, out int x, out int levelMm, out int z)
    {
        x = levelMm = z = 0;
        if (!IsCellKey(key)) return false;

        string[] parts = key.Substring(1).Split(',');
        if (parts.Length != 3) return false;

        return int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out levelMm)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
    }


    // the top middle of the cell a key names, where a ping hovers and the camera pans to
    public static bool TryResolveCellKey(string key, TerrainManager terrain, out Vector3 surface)
    {
        surface = default;
        if (terrain == null) return false;
        if (!TryParseCellKey(key, out int x, out int levelMm, out int z)) return false;
        if (!terrain.TryGetLevelAtMillimetres(levelMm, out int level)) return false;

        surface = terrain.CellToWorld(new Vector3Int(x, level, z));
        return true;
    }
}
