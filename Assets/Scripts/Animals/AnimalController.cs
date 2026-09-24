using UnityEngine;


// The root of every animal. Behaviour-specific movers subclass this; the base only carries what any
// of them needs to know about itself. AnimalManager binds it on spawn and on load, and reads the
// transform back into the save every LateUpdate, so a mover only ever has to move.
[RequireComponent(typeof(EntityId))]
[DisallowMultipleComponent]
public class AnimalController : MonoBehaviour
{
    private Animal animal;
    private EntityId entityId;

    // the save row this animal writes through to. owned by AnimalManager, never by a mover
    private AnimalSave save;


    public Animal Animal { get { return animal; } }
    public EntityId EntityId { get { return entityId; } }

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
}
