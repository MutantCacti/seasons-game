using UnityEngine;

public static class TerrainAtlasValidation
{
    public static Texture2D FindTileAtlas(TerrainRaster raster)
    {
        foreach (Sprite[] level in raster.spriteByCell)
        {
            foreach (Sprite sprite in level)
            {
                if (sprite != null) return sprite.texture;
            }
        }

        return null;
    }


    public static void WarnIfUnsuitable(Texture2D atlas, UnityEngine.Object context)
    {
        if (atlas == null) return;

        // IMPORTANT: Point filtering **MUST** be used !!! None of this works in bilinear !!!
        if (atlas.filterMode != FilterMode.Point)
        {
            Debug.LogWarning($"Atlas '{atlas.name}' is not point filtered - neighbouring slices will bleed into every quad edge", context);
        }

        if (atlas.mipmapCount > 1)
        {
            Debug.LogWarning($"Atlas '{atlas.name}' has mipmaps - lower mips will blend neighbouring slices together", context);
        }
    }


    // The wall atlas must match the tile atlas resolution
    public static bool ValidateWallAtlas(TerrainRaster raster, Texture2D wallAtlas, UnityEngine.Object context)
    {
        if (wallAtlas == null)
        {
            Debug.LogWarning("No wall atlas assigned - walls will be built with a fallback world projection", context);
            return false;
        }

        Texture2D tileAtlas = FindTileAtlas(raster);
        if (tileAtlas == null) return false;

        if (wallAtlas.width != tileAtlas.width || wallAtlas.height != tileAtlas.height)
        {
            Debug.LogError($"Wall atlas '{wallAtlas.name}' is {wallAtlas.width}x{wallAtlas.height} but tile atlas " +
                $"'{tileAtlas.name}' is {tileAtlas.width}x{tileAtlas.height} - they must share a layout", context);
            return false;
        }

        WarnIfUnsuitable(wallAtlas, context);
        return true;
    }
}
