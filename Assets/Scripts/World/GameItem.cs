using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


// One thing that can be done to a placed entity of its owning item: dropping `applied` on it turns it
// into `becomes`. Authored on the target, not the applied item, so what a seed can be turned into
// lives with the seed
[Serializable]
public class ItemReaction
{
    [SerializeField] private GameItem applied;
    [SerializeField] private GameItem becomes;


    public GameItem Applied { get { return applied; } }
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

    [Header("game effect: dropping `applied` on a placed entity of this item turns it into `becomes`")]
    [SerializeField] private ItemReaction[] reactions;


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

    // no tile and no prefab: a reagent unless something names it in a reaction, see
    // GameItemRegistry.IsReagent
    public bool IsReagentShape => tile == null && prefab == null;

    // an authored list at all, whatever is on it: the reaction walk in PuzzleManager follows it
    public bool HasReactions => reactions != null && reactions.Length > 0;


    // growthDays as a count of clock moments, never less than the next one
    public int GrowthMoments => Mathf.Max(1, Mathf.RoundToInt(growthDays * WorldStateManager.MOMENTS_PER_DAY));


    // What a placed one of this item becomes when applied is dropped on it, or null when it doesn't
    // react to it. The first complete reaction naming applied wins: an entry with no becomes is
    // skipped, so a half-authored reaction can never replace an entity with nothing
    public GameItem ReactionInto(GameItem applied)
    {
        if (applied == null || reactions == null) return null;

        for (int i = 0; i < reactions.Length; i++)
        {
            ItemReaction reaction = reactions[i];

            if (reaction == null || reaction.Applied != applied) continue;
            if (reaction.Becomes == null) continue;

            return reaction.Becomes;
        }

        return null;
    }


    public bool IsPlaceableOn(TerrainTile target)
    {
        if (target == null) return false;
        if (target.Tags == null) return false;

        if (placedOnTags != null && !placedOnTags.All(tag => target.Tags.Contains(tag))) return false;
        if (antiPlacedOnTags != null && !antiPlacedOnTags.All(antiTag => !target.Tags.Contains(antiTag))) return false;

        return true;
    }
}
