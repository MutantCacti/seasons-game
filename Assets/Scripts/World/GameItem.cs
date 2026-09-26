using System.Linq;
using UnityEngine;


[CreateAssetMenu(
    fileName = "GameItem",
    menuName = "Items/GameItem"
)]
public class GameItem : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] [TextArea] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField] private TerrainTile tile;
    [SerializeField] private GameObject prefab;
    [SerializeField] private SoundObject placeSound;


    [Header("all tags must be present on the tile for this item to be placeable")]
    [SerializeField] private string[] placedOnTags;

    [Header("any of these tags present on the tile makes the item unplaceable")]
    [SerializeField] private string[] antiPlacedOnTags;

    [Header("growth: once placed, this is replaced by growsInto after growthDays (none = never grows)")]
    [SerializeField] private GameItem growsInto;
    [Tooltip("in clock moments of a quarter day each: 0.25 = the next sunrise/noon/sunset/midnight, 1 = the same moment tomorrow")]
    [SerializeField] [Min(0.25f)] private float growthDays = 1.0f;
    [Tooltip("played when this grows; falls back to the grown item's place sound, then the default")]
    [SerializeField] private SoundObject growSound;


    public string Id => id;
    public string Name => name; // Object.name, the name in the inspector
    public string Description => description;
    public Sprite Icon => icon;
    public TerrainTile Tile => tile;
    public GameObject Prefab => prefab;
    public SoundObject PlaceSound => placeSound;
    public GameItem GrowsInto => growsInto;
    public float GrowthDays => growthDays;
    public SoundObject GrowSound => growSound;

    // growthDays as a count of clock moments, never less than the next one
    public int GrowthMoments => Mathf.Max(1, Mathf.RoundToInt(growthDays * WorldStateManager.MOMENTS_PER_DAY));


    public bool IsPlaceableOn(TerrainTile target)
    {
        if (target == null) return false;
        if (target.Tags == null) return false;

        if (placedOnTags != null && !placedOnTags.All(tag => target.Tags.Contains(tag))) return false;
        if (antiPlacedOnTags != null && !antiPlacedOnTags.All(antiTag => !target.Tags.Contains(antiTag))) return false;

        return true;
    }
}
