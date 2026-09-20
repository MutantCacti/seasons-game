using UnityEngine;
using UnityEngine.Tilemaps;

// Simple visibility helper for the terrain pipeline
// Disables tilemap rendering and enables baked terrain mesh rendering on Awake
public class TerrainRuntimeVisibility : MonoBehaviour
{
    [SerializeField] private MeshRenderer[] bakedRenderers;


    private void Awake()
    {
        foreach (TilemapRenderer tilemapRenderer in GetComponentsInChildren<TilemapRenderer>(true))
        {
            tilemapRenderer.enabled = false;
        }

        if (bakedRenderers == null) return;

        foreach (MeshRenderer bakedRenderer in bakedRenderers)
        {
            if (bakedRenderer != null) bakedRenderer.enabled = true;
        }
    }


    public void SetBakedRenderers(MeshRenderer[] renderers)
    {
        bakedRenderers = renderers;
    }


    // used by TerrainBakeSettings' preview toggle
    public MeshRenderer[] BakedRenderers { get { return bakedRenderers; } }
}
