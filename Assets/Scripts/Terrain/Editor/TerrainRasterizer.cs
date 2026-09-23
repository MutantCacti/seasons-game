using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Generates a TerrainRaster by converting tilemap sprite alpha into a bool bitmap
public static class TerrainRasterizer
{
    public static TerrainRaster Rasterize(List<TerrainManager.Level> levels, int pixelsPerTile, float alphaThreshold, UnityEngine.Object context)
    {
        if (pixelsPerTile <= 0)
        {
            Debug.LogError("pixelsPerTile must be positive", context);
            return null;
        }

        if (levels.Count == 0) return null;

        // the raster covers every level, so it spans the union of all their bounds
        int cellMinX = int.MaxValue, cellMinY = int.MaxValue;
        int cellMaxX = int.MinValue, cellMaxY = int.MinValue;

        foreach (TerrainManager.Level level in levels)
        {
            BoundsInt bounds = level.tilemap.cellBounds;

            cellMinX = Mathf.Min(cellMinX, bounds.xMin);
            cellMinY = Mathf.Min(cellMinY, bounds.yMin);
            cellMaxX = Mathf.Max(cellMaxX, bounds.xMax);
            cellMaxY = Mathf.Max(cellMaxY, bounds.yMax);
        }

        TerrainRaster raster = new TerrainRaster();
        raster.pixelsPerTile = pixelsPerTile;
        raster.cellOriginX = cellMinX;
        raster.cellOriginY = cellMinY;
        raster.cellWidth = cellMaxX - cellMinX;
        raster.cellHeight = cellMaxY - cellMinY;
        raster.pixelWidth = raster.cellWidth * pixelsPerTile;
        raster.pixelHeight = raster.cellHeight * pixelsPerTile;
        raster.levelHeights = new float[levels.Count];
        raster.levelMasks = new bool[levels.Count][];
        raster.spriteByCell = new Sprite[levels.Count][];

        Dictionary<Sprite, bool[]> maskCache = new Dictionary<Sprite, bool[]>();

        for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
        {
            raster.levelHeights[levelIndex] = levels[levelIndex].height;
            raster.levelMasks[levelIndex] = new bool[raster.pixelWidth * raster.pixelHeight];
            raster.spriteByCell[levelIndex] = new Sprite[raster.cellWidth * raster.cellHeight];

            RasterizeTilemap(raster, levelIndex, levels[levelIndex].tilemap, maskCache, alphaThreshold, context);
        }

        return raster;
    }


    private static void RasterizeTilemap(TerrainRaster raster, int levelIndex, Tilemap tilemap,
        Dictionary<Sprite, bool[]> maskCache, float alphaThreshold, UnityEngine.Object context)
    {
        bool[] mask = raster.levelMasks[levelIndex];
        Sprite[] sprites = raster.spriteByCell[levelIndex];
        int pixelsPerTile = raster.pixelsPerTile;

        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (!tilemap.HasTile(cell)) continue;

            Sprite sprite = tilemap.GetSprite(cell);
            if (sprite == null) continue;

            bool[] tileMask = GetTileMask(sprite, maskCache, pixelsPerTile, alphaThreshold, context);
            if (tileMask == null) continue;

            int cellU = cell.x - raster.cellOriginX;
            int cellV = cell.y - raster.cellOriginY;
            sprites[cellV * raster.cellWidth + cellU] = sprite;

            int baseU = cellU * pixelsPerTile;
            int baseV = cellV * pixelsPerTile;

            for (int py = 0; py < pixelsPerTile; py++)
            {
                int maskRow = (baseV + py) * raster.pixelWidth + baseU;
                int tileRow = py * pixelsPerTile;

                for (int px = 0; px < pixelsPerTile; px++)
                {
                    if (tileMask[tileRow + px]) mask[maskRow + px] = true;
                }
            }
        }
    }


    private static bool[] GetTileMask(Sprite sprite, Dictionary<Sprite, bool[]> maskCache,
        int pixelsPerTile, float alphaThreshold, UnityEngine.Object context)
    {
        if (maskCache.TryGetValue(sprite, out bool[] cached)) return cached;

        Texture2D texture = sprite.texture;
        Rect fullRect = sprite.rect;          // the authored slice
        Rect tightRect = sprite.textureRect;  // what survived Unity trimming transparent edges
        bool[] mask = null;

        if (!texture.isReadable)
        {
            Debug.LogError($"Texture '{texture.name}' is not readable - tick Read/Write in its import settings", context);
        }
        else if ((int)fullRect.width != pixelsPerTile || (int)fullRect.height != pixelsPerTile)
        {
            Debug.LogWarning($"Sprite '{sprite.name}' is {(int)fullRect.width}x{(int)fullRect.height}, expected {pixelsPerTile} square - skipping", context);
        }
        else
        {
            mask = new bool[pixelsPerTile * pixelsPerTile];

            int tightWidth = (int)tightRect.width;
            int tightHeight = (int)tightRect.height;

            // a fully transparent sprite can be skipped
            if (tightWidth > 0 && tightHeight > 0)
            {
                Color[] pixels = texture.GetPixels((int)tightRect.x, (int)tightRect.y, tightWidth, tightHeight);

                // if the tile palette import settings' Mesh Type is set to "Tight,"
                // the tiles get converted from 32x32 to 29x27 or whatever their opaque
                // bounding box simplifies to. Ideally use fullRect, but also just
                // round it here so we don't have to worry about it at all
                int offsetX = Mathf.RoundToInt(sprite.textureRectOffset.x);
                int offsetY = Mathf.RoundToInt(sprite.textureRectOffset.y);

                for (int py = 0; py < tightHeight; py++)
                {
                    int maskY = offsetY + py;
                    if (maskY < 0 || maskY >= pixelsPerTile) continue;

                    for (int px = 0; px < tightWidth; px++)
                    {
                        int maskX = offsetX + px;
                        if (maskX < 0 || maskX >= pixelsPerTile) continue;

                        mask[maskY * pixelsPerTile + maskX] = pixels[py * tightWidth + px].a >= alphaThreshold;
                    }
                }
            }
        }

        maskCache[sprite] = mask;
        return mask;
    }
}
