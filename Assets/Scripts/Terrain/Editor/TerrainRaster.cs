using System.Collections.Generic;
using UnityEngine;

// Stores bool bitmaps of filled/unfilled pixels in the tilemap for each layer
// also stores the sprite associated with each cell so TerrainRasterizer can look up the texture atlas
public class TerrainRaster
{
    public int pixelsPerTile;


    // combined bounds in cells
    public int cellOriginX;
    public int cellOriginY;
    public int cellWidth;
    public int cellHeight;


    // combined bounds in pixels
    public int pixelWidth;
    public int pixelHeight;


    public float[] levelHeights;     // world y per level, ascending order; not necessarily 1 unit apart
    public bool[][] levelMasks;      // per level, pixelWidth * pixelHeight
    public Sprite[][] spriteByCell;  // per level, cellWidth * cellHeight


    public bool IsSolid(int levelIndex, int u, int v)
    {
        return levelMasks[levelIndex][v * pixelWidth + u];
    }


    public Sprite GetSprite(int levelIndex, int cellX, int cellY)
    {
        int localX = cellX - cellOriginX;
        int localY = cellY - cellOriginY;

        if (localX < 0 || localX >= cellWidth || localY < 0 || localY >= cellHeight) return null;

        return spriteByCell[levelIndex][localY * cellWidth + localX];
    }


    // NB: The Grid is rotated 90 degrees in the x axis for 2D tilemap in 3D scene reasons
    public float PixelToWorldX(int u)
    {
        return cellOriginX + (float)u / pixelsPerTile;
    }


    public float PixelToWorldZ(int v)
    {
        return cellOriginY + (float)v / pixelsPerTile;
    }
}


// A rectangle of top surface pixels on one level
// these can be merged where their lengths are exactly the same
public struct TopRun
{
    public int levelIndex;
    public int startU;  // inclusive
    public int endU;    // exclusive
    public int startV;  // inclusive
    public int endV;    // exclusive
    public int ownerCellX;
    public int ownerCellY;
}


// A rectangle of wall surface pixels in one cell
// and with only on cell's worth of height
public struct WallRun
{
    public int levelIndex;
    public int du;
    public int dv;
    public int outer;
    public int startInner;  // inclusive
    public int endInner;    // exclusive
    public float bottom;    // world y the wall falls to
    public float top;       // world y of the surface that owns it
    public int ownerCellX;
    public int ownerCellY;
}


public class TerrainSurfaces
{
    public readonly List<TopRun> tops = new List<TopRun>();
    public readonly List<WallRun> walls = new List<WallRun>();
}
