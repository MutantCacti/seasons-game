using System;
using UnityEngine;


// The root of every animal. Behaviour-specific movers subclass this; the base only carries what any
// of them needs to know about itself. AnimalManager binds it on spawn and on load, and reads the
// transform back into the save every LateUpdate, so a mover only ever has to move.
//
// It also owns what the status panel shows and edits for this one animal: its name and its star.
// Both live in its save row, so they are world state and roll back with a checkpoint.
[RequireComponent(typeof(EntityId))]
[DisallowMultipleComponent]
public class AnimalController : MonoBehaviour
{
    private Animal animal;
    private EntityId entityId;

    // the save row this animal writes through to. owned by AnimalManager, never by a mover
    private AnimalSave save;


    public const int MAX_NAME_LENGTH = 20;


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


    // Trimmed and capped. Empty, or the species name itself, goes back to the species name, so a
    // player who clears the field gets the default rather than a nameless animal
    public void SetCustomName(string newName)
    {
        if (save == null) return;

        string trimmed = newName != null ? newName.Trim() : "";
        if (trimmed.Length > MAX_NAME_LENGTH) trimmed = trimmed.Substring(0, MAX_NAME_LENGTH);
        if (animal != null && trimmed == animal.Name) trimmed = "";

        if ((save.customName ?? "") == trimmed) return;

        save.customName = trimmed;
        SaveStatus();
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
}
