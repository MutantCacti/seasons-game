using System;
using DG.Tweening;
using UnityEngine;


// The root of every animal. Behaviour-specific movers subclass this; the base only carries what any
// of them needs to know about itself. AnimalManager binds it on spawn and on load, and reads the
// transform back into the save every LateUpdate, so a mover only ever has to move.
//
// It also owns what the status panel shows and edits for this one animal: its name and its star.
// Both live in its save row, so they are world state and roll back with a checkpoint.
//
// And the little reactions that make it feel alive when the player deals with it: growing under the
// pointer, a hop when it is focused, a bigger one when it is renamed. A hop is a height, not a move:
// a mover that places the animal every frame adds ReactionOffset to where it puts it.
[RequireComponent(typeof(EntityId))]
[DisallowMultipleComponent]
public class AnimalController : MonoBehaviour
{
    private Animal animal;
    private EntityId entityId;

    // the save row this animal writes through to. owned by AnimalManager, never by a mover
    private AnimalSave save;


    public const int MAX_NAME_LENGTH = 20;


    [Header("Reactions")]
    [SerializeField] private float hoverGrow = 1.05f;
    [SerializeField] private float hoverDuration = 0.12f;
    [SerializeField] private float focusHopHeight = 0.08f;     // world units
    [SerializeField] private float focusHopDuration = 0.22f;
    [SerializeField] private float renameHopHeight = 0.14f;
    [SerializeField] private float renameHopDuration = 0.3f;


    private Vector3 restScale;
    private bool restScaleKnown;
    private Tween hoverTween;

    private float hopHeight;
    private Tween hopTween;


    public Animal Animal { get { return animal; } }
    public EntityId EntityId { get { return entityId; } }


    // the name or the star changed
    public event Action StatusChanged;

    // false until AnimalManager has bound it, e.g. during the instantiating Awake
    public bool Bound { get { return animal != null; } }

    internal AnimalSave Save { get { return save; } }


    // Only AnimalManager calls this, straight after instantiating
    internal void Bind(Animal boundAnimal, EntityId boundEntityId, AnimalSave boundSave)
    {
        animal = boundAnimal;
        entityId = boundEntityId;
        save = boundSave;
    }


    // MARK: status


    // what the player called it, else the species name
    public string DisplayName
    {
        get
        {
            if (save != null && !string.IsNullOrEmpty(save.customName)) return save.customName;
            return animal != null ? animal.Name : name;
        }
    }


    public bool Highlighted { get { return save != null && save.highlighted; } }


    // The guid of the entity it calls home, empty for none. It lives in the save row, so it rolls back
    // with a checkpoint; a change reaches disk with the next save, like the position does
    public string HomeGuid
    {
        get { return save != null && !string.IsNullOrEmpty(save.homeGuid) ? save.homeGuid : ""; }
        protected set { if (save != null) save.homeGuid = value ?? ""; }
    }


    // The clock reading of its last birth: its own, or its last baby's. 0 is a save from before
    // stamping, which a breeding rule sets to the clock instead of reading as long overdue
    public int BredDay { get { return save != null ? save.bredDay : 0; } }
    public float BredCycleTime { get { return save != null ? save.bredCycleTime : 0.0f; } }


    // World state like the name and the star, so it rolls back with a checkpoint. Writes through to
    // the save at once: a birth that is not on disk can be born again after a reload
    public void SetBredStamp(int day, float cycleTime)
    {
        if (save == null) return;

        save.bredDay = day;
        save.bredCycleTime = cycleTime;

        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();
    }


    // Trimmed and capped. Empty, or the species name itself, goes back to the species name, so a
    // player who clears the field gets the default rather than a nameless animal. True when the name
    // actually changed, which is when it is worth celebrating
    public bool SetCustomName(string newName)
    {
        if (save == null) return false;

        string trimmed = newName != null ? newName.Trim() : "";
        if (trimmed.Length > MAX_NAME_LENGTH) trimmed = trimmed.Substring(0, MAX_NAME_LENGTH);
        if (animal != null && trimmed == animal.Name) trimmed = "";

        if ((save.customName ?? "") == trimmed) return false;

        save.customName = trimmed;
        SaveStatus();

        return true;
    }


    public void SetHighlighted(bool highlighted)
    {
        if (save == null || save.highlighted == highlighted) return;

        save.highlighted = highlighted;
        SaveStatus();
    }


    private void SaveStatus()
    {
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.Save();

        StatusChanged?.Invoke();
    }


    // MARK: happiness


    private static readonly string[] NO_TIPS = new string[0];


    // 0 miserable .. 1 content, worked out live from the world around it, so nothing is saved. The
    // status panel reads it a few times a second while open. An animal that doesn't say is content
    public virtual float Happiness { get { return 1.0f; } }


    // What is bothering it, one line each, stated as problems rather than fixes: the player finds
    // the fix. Empty when nothing is. Read alongside Happiness
    public virtual string[] GetHappinessTips()
    {
        return NO_TIPS;
    }


    // A line for when nothing is bothering it, shown in the tips' place. Not a problem, so the bar still
    // reads full. It should hold still while it is being read: pick once and keep it, don't re-roll
    // every time it is asked. Null for none
    public virtual string GetHappyLine()
    {
        return null;
    }


    // MARK: reactions


    // where a hop has lifted it right now; a mover adds this to the position it sets
    public Vector3 ReactionOffset { get { return Vector3.up * hopHeight; } }


    // a little bigger while the pointer is on it, so it reads as clickable
    public void SetHovered(bool hovered)
    {
        if (!restScaleKnown)
        {
            restScale = transform.localScale;
            restScaleKnown = true;
        }

        if (hoverTween != null) hoverTween.Kill();

        hoverTween = transform.DOScale(hovered ? restScale * hoverGrow : restScale, hoverDuration)
            .SetEase(Ease.OutQuad)
            .SetLink(gameObject);
    }


    // it was just clicked and is now the one being looked at
    public virtual void OnFocused()
    {
        Hop(focusHopHeight, focusHopDuration);
    }


    // it was just given a new name, and should look like it heard
    public virtual void OnRenamed()
    {
        Hop(renameHopHeight, renameHopDuration);
    }


    // up quickly, down a touch slower, like a small jump
    protected void Hop(float height, float duration)
    {
        if (hopTween != null) hopTween.Kill();

        hopHeight = 0.0f;

        Sequence hop = DOTween.Sequence();
        hop.Append(DOTween.To(() => hopHeight, value => hopHeight = value, height, duration * 0.4f).SetEase(Ease.OutQuad));
        hop.Append(DOTween.To(() => hopHeight, value => hopHeight = value, 0.0f, duration * 0.6f).SetEase(Ease.InQuad));
        hop.SetLink(gameObject);

        hopTween = hop;
    }


    // MARK: ground


    // How much this animal's own ground rules want one of its kind placed on this tile, 0 for never.
    // The base allows anywhere at the same weight; a mover that avoids or prefers ground (a worm) says
    // so here, so a rule that puts a new one down honours the tiles it would walk on itself
    public virtual float SpawnWeightOn(TerrainTile tile)
    {
        return 1.0f;
    }
}
