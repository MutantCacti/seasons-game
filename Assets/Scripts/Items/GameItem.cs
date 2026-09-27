using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


// One thing that can be done to a placed entity of its owning item: placing `catalyst` on it turns it
// into `becomes`
[Serializable]
public class ItemReaction
{
    [SerializeField] private GameItem catalyst;
    [SerializeField] private GameItem becomes;


    public GameItem Catalyst { get { return catalyst; } }
    public GameItem Becomes { get { return becomes; } }
}


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


    [Header("any tag must be present on the tile for this item to be placeable")]
    [SerializeField] private string[] placedOnTags;

    [Header("any of these tags present on the tile makes the item unplaceable")]
    [SerializeField] private string[] antiPlacedOnTags;

    [Header("replaced by growsInto after growthDays if set")]
    [SerializeField] private GameItem growsInto;
    [Tooltip("in clock moments of a quarter day each: 0.25 = the next sunrise/noon/sunset/midnight, 1 = the same moment tomorrow")]
    [SerializeField] [Min(0.25f)] private float growthDays = 1.0f;
    [Tooltip("played when this grows; falls back to the grown item's place sound, then the default")]
    [SerializeField] private SoundObject growSound;

    [Header("placing `catalyst` on this item turns it into `becomes`")]
    [SerializeField] private ItemReaction[] reactions;

    [Tooltip("transitional items (a sprout on its way to a flower, say) are still placed and saved, " +
        "they are just kept out of the compendium, which the player reads as a catalogue")]
    [SerializeField] private bool hideInCompendium;


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
    public IReadOnlyList<ItemReaction> Reactions => reactions;
    public bool HideInCompendium => hideInCompendium;

    // no tile and no prefab: a reagent unless something names it in a reaction, see
    // GameItemRegistry.IsReagent
    public bool IsReagentShape => tile == null && prefab == null;


    // growthDays as a count of clock moments, never less than the next one
    public int GrowthMoments => Mathf.Max(1, Mathf.RoundToInt(growthDays * WorldStateManager.MOMENTS_PER_DAY));


    // What a placed one of this item becomes when catalyst is dropped on it, or null when it doesn't
    // react to it. The first complete reaction naming catalyst wins: an entry with no becomes is
    // skipped, so a half-authored reaction can never replace an entity with nothing
    public GameItem ReactionInto(GameItem catalyst)
    {
        if (catalyst == null || reactions == null) return null;

        for (int i = 0; i < reactions.Length; i++)
        {
            ItemReaction reaction = reactions[i];

            if (reaction == null || reaction.Catalyst != catalyst) continue;
            if (reaction.Becomes == null) continue;

            return reaction.Becomes;
        }

        return null;
    }


    public bool IsPlaceableOn(TerrainTile target)
    {
        if (target == null) return false;
        if (target.Tags == null) return false;

        if (placedOnTags != null && !placedOnTags.Any(tag => target.Tags.Contains(tag))) return false;
        if (antiPlacedOnTags != null && !antiPlacedOnTags.All(antiTag => !target.Tags.Contains(antiTag))) return false;

        return true;
    }
}
