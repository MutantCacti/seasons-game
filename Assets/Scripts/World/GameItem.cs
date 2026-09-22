using System.Linq;
using UnityEngine;


[CreateAssetMenu(
    fileName = "GameItem",
    menuName = "Items/GameItem"
)]
public class GameItem : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private Sprite icon;
    [SerializeField] private TerrainTile tile;
    [SerializeField] private GameObject prefab;
    [SerializeField] private SoundObject placeSound;


    [Header("all tags must be present on the tile for this item to be placeable")]
    [SerializeField] private string[] placedOnTags;

    [Header("any of these tags present on the tile makes the item unplaceable")]
    [SerializeField] private string[] antiPlacedOnTags;


    public string Id => id;
    public string Name => name; // Object.name, the name in the inspector
    public Sprite Icon => icon;
    public TerrainTile Tile => tile;
    public GameObject Prefab => prefab;
    public SoundObject PlaceSound => placeSound;


    public bool IsPlaceableOn(TerrainTile target)
    {
        if (target == null) return false;
        if (target.Tags == null) return false;

        if (placedOnTags != null && !placedOnTags.All(tag => target.Tags.Contains(tag))) return false;
        if (antiPlacedOnTags != null && !antiPlacedOnTags.All(antiTag => !target.Tags.Contains(antiTag))) return false;

        return true;
    }
}
