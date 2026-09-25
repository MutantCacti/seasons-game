using UnityEngine;

// Editor only terrain baking pipeline settings
// See ./Editor/TerrainBakeSettingsEditor.cs
public class TerrainBakeSettings : MonoBehaviour
{
    public int pixelsPerTile = 32;                           // sprite resolution; must match the tileset
    [Range(0.0f, 1.0f)] public float alphaThreshold = 0.5f;  // alpha threshold for solid pixels
    public float voidDepth = -4.0f;                          // world y that walls descend up to when nothing's below them

    public Texture2D wallAtlas;     // must be laid out the same as the tile atlas; slice N textures walls under tiles N
    public Material wallMaterial;
    public Material topMaterial;
    public Material seaMaterial;    // any Seasons/Sea material; a re-bake puts it on the Sea Level renderer

    public string outputFolder = "Assets/Terrain";
    public string collisionLayerName = "Terrain";
    public string seaLayerName = "Water";
}
