using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;


// Any unfilled slot falls back to a random fill from FillVariants
// don't change the slot order or everything breaks
[System.Serializable]
public struct TerrainEdges
{
    public Sprite[] Slots;


    public const int SLOT_COUNT = 47;

    public const int N = 1 << 0;
    public const int NE = 1 << 1;
    public const int E = 1 << 2;
    public const int SE = 1 << 3;
    public const int S = 1 << 4;
    public const int SW = 1 << 5;
    public const int W = 1 << 6;
    public const int NW = 1 << 7;

    public const int CARDINALS = N | E | S | W;

    private static readonly int[,] Diagonals =
    {
        { NE, N, E },
        { SE, S, E },
        { SW, S, W },
        { NW, N, W },
    };

    private static readonly int[] shapes = new int[SLOT_COUNT];
    private static readonly int[] slotByMask = new int[256];

    public static readonly string[] SlotNames = new string[SLOT_COUNT];


    static TerrainEdges()
    {
        List<int> distinct = new List<int>();

        for (int mask = 0; mask < 256; mask++)
        {
            int shape = Canonicalise(mask);
            if (!distinct.Contains(shape)) distinct.Add(shape);
        }

        // fewest holes first
        distinct.Sort((a, b) =>
        {
            int byCardinals = MissingCardinals(a).CompareTo(MissingCardinals(b));
            return byCardinals != 0 ? byCardinals : a.CompareTo(b);
        });

        for (int slot = 0; slot < SLOT_COUNT; slot++)
        {
            shapes[slot] = distinct[slot];
            SlotNames[slot] = Describe(distinct[slot]);
        }

        for (int mask = 0; mask < 256; mask++)
        {
            slotByMask[mask] = distinct.IndexOf(Canonicalise(mask));
        }
    }


    // Drops the diagonals that cannot be seen
    public static int Canonicalise(int mask)
    {
        int shape = mask & CARDINALS;

        for (int i = 0; i < 4; i++)
        {
            int diagonal = Diagonals[i, 0];
            bool matters = (mask & Diagonals[i, 1]) != 0 && (mask & Diagonals[i, 2]) != 0;

            if (matters && (mask & diagonal) != 0) shape |= diagonal;
        }

        return shape;
    }


    public static int SlotForMask(int mask)
    {
        return slotByMask[mask & 0xFF];
    }


    public static int ShapeForSlot(int slot)
    {
        return shapes[slot];
    }


    // true when this diagonal is shut by a missing cardinal
    // i.e. whether the NW diagonal is occupied doesn't matter when N and W are both empty
    public static bool IsDiagonalMoot(int shape, int diagonal)
    {
        for (int i = 0; i < 4; i++)
        {
            if (Diagonals[i, 0] != diagonal) continue;

            return (shape & Diagonals[i, 1]) == 0 || (shape & Diagonals[i, 2]) == 0;
        }

        return false;
    }


    public static int MissingCardinals(int shape)
    {
        int missing = 0;

        if ((shape & N) == 0) missing++;
        if ((shape & E) == 0) missing++;
        if ((shape & S) == 0) missing++;
        if ((shape & W) == 0) missing++;

        return missing;
    }


    private static string Describe(int shape)
    {
        List<string> open = new List<string>();

        if ((shape & N) == 0) open.Add("N");
        if ((shape & E) == 0) open.Add("E");
        if ((shape & S) == 0) open.Add("S");
        if ((shape & W) == 0) open.Add("W");

        if ((shape & NE) == 0 && !IsDiagonalMoot(shape, NE)) open.Add("NE");
        if ((shape & SE) == 0 && !IsDiagonalMoot(shape, SE)) open.Add("SE");
        if ((shape & SW) == 0 && !IsDiagonalMoot(shape, SW)) open.Add("SW");
        if ((shape & NW) == 0 && !IsDiagonalMoot(shape, NW)) open.Add("NW");

        return open.Count == 0 ? "interior" : "open " + string.Join(" ", open);
    }


    public Sprite this[int slot]
    {
        get
        {
            return Slots != null && slot >= 0 && slot < Slots.Length ? Slots[slot] : null;
        }
        set
        {
            if (Slots == null || Slots.Length != SLOT_COUNT) System.Array.Resize(ref Slots, SLOT_COUNT);

            Slots[slot] = value;
        }
    }


    public Sprite ForMask(int mask)
    {
        return this[SlotForMask(mask)];
    }
}


[CreateAssetMenu(
    fileName="TerrainTile",
    menuName="Terrain/Terrain Tile"
)]
public class TerrainTile : TileBase
{
    public string Id;
    public string[] Tags;
    public Sprite[] FillVariants;
    public int VariantSeed;
    public TerrainEdges Edges;


    // custom FNV-1a implementation for determinism
    // https://mojoauth.com/hashing/fnv-1a-in-c
    private const uint FNV_OFFSET_BASIS = 2166136261u;
    private const uint FNV_PRIME = 16777619u;


    // must stay in this order, the baked Edges masks depend on it
    private static readonly Vector3Int[] NEIGHBOUR_OFFSETS =
    {
        new Vector3Int(0, 1, 0),    // N
        new Vector3Int(1, 1, 0),    // NE
        new Vector3Int(1, 0, 0),    // E
        new Vector3Int(1, -1, 0),   // SE
        new Vector3Int(0, -1, 0),   // S
        new Vector3Int(-1, -1, 0),  // SW
        new Vector3Int(-1, 0, 0),   // W
        new Vector3Int(-1, 1, 0),   // NW
    };


    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        tileData.sprite = SpriteFor(position, tilemap);
        tileData.color = Color.white;
        tileData.transform = Matrix4x4.identity;
        tileData.flags = TileFlags.LockTransform | TileFlags.LockColor;
        tileData.colliderType = Tile.ColliderType.None;
    }


    // At runtime the map never changes shape, so this only ever fires in the editor
    public override void RefreshTile(Vector3Int position, ITilemap tilemap)
    {
        base.RefreshTile(position, tilemap);

        foreach (Vector3Int offset in NEIGHBOUR_OFFSETS)
        {
            tilemap.RefreshTile(position + offset);
        }
    }


    // Whatever art this tile has for the cell's shape, or a random fill when it has none.
    // Interior is just another shape, so a tileset can pin it to one sprite by filling that
    // slot instead of leaving it to FillVariants
    public Sprite SpriteFor(Vector3Int position, ITilemap tilemap)
    {
        Sprite edge = Edges.ForMask(NeighbourMask(position, tilemap));

        return edge != null ? edge : VariantFor(position);
    }


    // Same thing for callers that have a Tilemap rather than an ITilemap
    // because apparently these things are completely unreconcilable by Unity
    public Sprite SpriteFor(Vector3Int position, Tilemap tilemap)
    {
        Sprite edge = Edges.ForMask(NeighbourMask(position, tilemap));

        return edge != null ? edge : VariantFor(position);
    }


    public static int NeighbourMask(Vector3Int position, ITilemap tilemap)
    {
        int mask = 0;

        for (int bit = 0; bit < NEIGHBOUR_OFFSETS.Length; bit++)
        {
            if (tilemap.GetTile(position + NEIGHBOUR_OFFSETS[bit]) != null) mask |= 1 << bit;
        }

        return mask;
    }


    // same mask, for code that has a Tilemap rather than the ITilemap the tile system passes in
    public static int NeighbourMask(Vector3Int position, Tilemap tilemap)
    {
        int mask = 0;

        for (int bit = 0; bit < NEIGHBOUR_OFFSETS.Length; bit++)
        {
            if (tilemap.GetTile(position + NEIGHBOUR_OFFSETS[bit]) != null) mask |= 1 << bit;
        }

        return mask;
    }


    // select a random variant based on position, seeded per tile asset
    // the same position always returns the same sprite
    public Sprite VariantFor(Vector3Int position)
    {
        if (FillVariants == null || FillVariants.Length == 0) return null;

        uint hash = FNV_OFFSET_BASIS;
        hash = Fold(hash, VariantSeed);
        hash = Fold(hash, position.x);
        hash = Fold(hash, position.y);
        // hash = Fold(hash, position.z); // position is tilemap cell, so z is always 0

        return FillVariants[(int)(Avalanche(hash) % (uint)FillVariants.Length)];
    }


    // Add one int to the hash, little endian
    private static uint Fold(uint hash, int value)
    {
        unchecked
        {
            hash = (hash ^ (byte)(value & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 8) & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 16) & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 24) & 0xFF)) * FNV_PRIME;
            return hash;
        }
    }


    // https://blog.teamleadnet.com/2012/08/murmurhash3-ultra-fast-hash-algorithm.html
    // avalanching a hash means scrambling it so that if even one bit is different
    // from another hash pre-avalanche, post-avalanche average half the bits will be different.
    // this is apparently necessary to avoid structure in the low bits of the FNV-1a algorithm
    private static uint Avalanche(uint hash)
    {
        unchecked
        {
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            hash *= 0x846ca68bu;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
