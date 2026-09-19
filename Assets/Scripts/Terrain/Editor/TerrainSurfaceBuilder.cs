using System.Collections.Generic;

// Runs can be built in two modes

// Render confines every run to a single tile. This is necessary for the rendered mesh
// because it needs to look up the correct sprite to texture the run with and a run that
// spans more than one tile gets fucked up UVs (idrk what Unity is doing under the hood
// but it doesn't work unless I separate them)

// Collision doesn't worry about UVs so it scans in nice big rows and merges them together.

public static class TerrainSurfaceBuilder
{
    public static TerrainSurfaces Build(TerrainRaster raster, int voidDepth, bool perTile)
    {
        TerrainSurfaces surfaces = new TerrainSurfaces();

        for (int levelIndex = 0; levelIndex < raster.levelHeights.Length; levelIndex++)
        {
            EmitTops(raster, surfaces, levelIndex, perTile);
            EmitWalls(raster, surfaces, levelIndex, 1, 0, voidDepth, perTile);
            EmitWalls(raster, surfaces, levelIndex, -1, 0, voidDepth, perTile);
            EmitWalls(raster, surfaces, levelIndex, 0, 1, voidDepth, perTile);
            EmitWalls(raster, surfaces, levelIndex, 0, -1, voidDepth, perTile);
        }

        return surfaces;
    }


    // MARK: tops


    private struct OpenTop
    {
        public int startU;
        public int endU;
        public int startV;
    }


    // Big thanks to Claude Opus 5 for the greedy meshing optimisation (EmitTops, FlushTops, AddTopRun)
    // What I believe this is doing is merging runs as long as they don't cross a tile boundary
    // but Claude is just spouting jargon and corpo chat trying to make it seem fancier than it is
    private static void EmitTops(TerrainRaster raster, TerrainSurfaces surfaces, int levelIndex, bool perTile)
    {
        bool[] mask = raster.levelMasks[levelIndex];
        int pixelsPerTile = raster.pixelsPerTile;

        List<OpenTop> open = new List<OpenTop>();
        List<OpenTop> current = new List<OpenTop>();
        List<OpenTop> next = new List<OpenTop>();

        for (int v = 0; v < raster.pixelHeight; v++)
        {
            // a rendered quad may not straddle a tile row, because the row above uses a different slice
            if (perTile && v % pixelsPerTile == 0)
            {
                FlushTops(raster, surfaces, levelIndex, open, v);
            }

            current.Clear();
            int runStart = -1;

            for (int u = 0; u <= raster.pixelWidth; u++)
            {
                bool exposed = u < raster.pixelWidth
                    && mask[v * raster.pixelWidth + u]
                    && !IsCoveredAbove(raster, levelIndex, u, v);

                // in perTile mode a span also has to stop at each tile column
                bool boundary = perTile && u % pixelsPerTile == 0;

                if (runStart >= 0 && (!exposed || boundary))
                {
                    current.Add(new OpenTop { startU = runStart, endU = u, startV = v });
                    runStart = -1;
                }

                if (exposed && runStart < 0) runStart = u;
            }

            next.Clear();
            int openIndex = 0;
            int currentIndex = 0;

            while (openIndex < open.Count || currentIndex < current.Count)
            {
                if (currentIndex >= current.Count)
                {
                    AddTopRun(raster, surfaces, levelIndex, open[openIndex++], v);
                }
                else if (openIndex >= open.Count)
                {
                    next.Add(current[currentIndex++]);
                }
                else if (open[openIndex].startU < current[currentIndex].startU)
                {
                    AddTopRun(raster, surfaces, levelIndex, open[openIndex++], v);
                }
                else if (current[currentIndex].startU < open[openIndex].startU)
                {
                    next.Add(current[currentIndex++]);
                }
                else if (open[openIndex].endU == current[currentIndex].endU)
                {
                    next.Add(open[openIndex++]);  // extend: keep the older startV so the quad grows
                    currentIndex++;
                }
                else
                {
                    AddTopRun(raster, surfaces, levelIndex, open[openIndex++], v);
                    next.Add(current[currentIndex++]);
                }
            }

            List<OpenTop> swap = open;
            open = next;
            next = swap;
        }

        FlushTops(raster, surfaces, levelIndex, open, raster.pixelHeight);
    }


    private static void FlushTops(TerrainRaster raster, TerrainSurfaces surfaces, int levelIndex,
        List<OpenTop> open, int endV)
    {
        for (int i = 0; i < open.Count; i++)
        {
            AddTopRun(raster, surfaces, levelIndex, open[i], endV);
        }

        open.Clear();
    }


    private static void AddTopRun(TerrainRaster raster, TerrainSurfaces surfaces, int levelIndex, OpenTop run, int endV)
    {
        int pixelsPerTile = raster.pixelsPerTile;

        surfaces.tops.Add(new TopRun
        {
            levelIndex = levelIndex,
            startU = run.startU,
            endU = run.endU,
            startV = run.startV,
            endV = endV,
            ownerCellX = raster.cellOriginX + run.startU / pixelsPerTile,
            ownerCellY = raster.cellOriginY + run.startV / pixelsPerTile,
        });
    }


    // MARK: walls


    private static void EmitWalls(TerrainRaster raster, TerrainSurfaces surfaces, int levelIndex,
        int du, int dv, int voidDepth, bool perTile)
    {
        int top = raster.levelHeights[levelIndex];
        int pixelsPerTile = raster.pixelsPerTile;
        int outerCount = du != 0 ? raster.pixelWidth : raster.pixelHeight;
        int innerCount = du != 0 ? raster.pixelHeight : raster.pixelWidth;

        for (int outer = 0; outer < outerCount; outer++)
        {
            int runStart = -1;
            int runBottom = 0;
            int runTile = -1;

            for (int inner = 0; inner <= innerCount; inner++)
            {
                int u = du != 0 ? outer : inner;
                int v = du != 0 ? inner : outer;

                int bottom = 0;
                bool exposed = inner < innerCount && TryGetWallBottom(raster, levelIndex, u, v, du, dv, voidDepth, out bottom);
                int tile = perTile ? inner / pixelsPerTile : 0;

                if (runStart >= 0 && (!exposed || bottom != runBottom || tile != runTile))
                {
                    AddWallRun(raster, surfaces, levelIndex, du, dv, outer, runStart, inner, runBottom, top);
                    runStart = -1;
                }

                if (exposed && runStart < 0)
                {
                    runStart = inner;
                    runBottom = bottom;
                    runTile = tile;
                }
            }
        }
    }


    private static void AddWallRun(TerrainRaster raster, TerrainSurfaces surfaces, int levelIndex,
        int du, int dv, int outer, int startInner, int endInner, int bottom, int top)
    {
        int pixelsPerTile = raster.pixelsPerTile;
        int outerCell = outer / pixelsPerTile;
        int innerCell = startInner / pixelsPerTile;

        surfaces.walls.Add(new WallRun
        {
            levelIndex = levelIndex,
            du = du,
            dv = dv,
            outer = outer,
            startInner = startInner,
            endInner = endInner,
            bottom = bottom,
            top = top,
            ownerCellX = raster.cellOriginX + (du != 0 ? outerCell : innerCell),
            ownerCellY = raster.cellOriginY + (du != 0 ? innerCell : outerCell),
        });
    }


    // Cast along the layers below this one to find the required height of the wall
    private static bool TryGetWallBottom(TerrainRaster raster, int levelIndex, int u, int v, int du, int dv, int voidDepth, out int bottom)
    {
        bottom = voidDepth;

        bool[] mask = raster.levelMasks[levelIndex];
        if (!mask[v * raster.pixelWidth + u]) return false;

        int nu = u + du;
        int nv = v + dv;
        bool neighbourInside = nu >= 0 && nu < raster.pixelWidth && nv >= 0 && nv < raster.pixelHeight;

        if (neighbourInside && mask[nv * raster.pixelWidth + nu]) return false;

        for (int below = levelIndex - 1; below >= 0; below--)
        {
            bool[] belowMask = raster.levelMasks[below];
            bool ownSolid = belowMask[v * raster.pixelWidth + u];
            bool neighbourSolid = neighbourInside && belowMask[nv * raster.pixelWidth + nu];

            if (ownSolid || neighbourSolid)
            {
                bottom = raster.levelHeights[below];
                break;
            }
        }

        return bottom < raster.levelHeights[levelIndex];
    }


    // The mesh is a single surface so covered tiles are completely ignored
    private static bool IsCoveredAbove(TerrainRaster raster, int levelIndex, int u, int v)
    {
        for (int above = levelIndex + 1; above < raster.levelMasks.Length; above++)
        {
            if (raster.levelMasks[above][v * raster.pixelWidth + u]) return true;
        }

        return false;
    }
}
