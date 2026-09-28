using UnityEngine;


// Faces an item the same way on every load. The yaw used to be Random.Range in Start, so a seed or a
// lily pad came back facing somewhere new every save/reload (and a rebuilt entity, like growth, re-rolled
// too). Now it comes from the cell the item sits on, so reload lands on the angle the player saw.
//
// Cell, not guid: the placement ghost has no EntityId and no guid (PlacementController prunes things
// like that), so only a position seed lets the preview show the exact angle the item will land at.
//
// This sits on the Sprite child of the seed/lily pad prefabs, and it sets world rotation outright rather
// than adding to it: the ghost re-enables on every preview move, and the angle is derived from the cell,
// so adding would stack a new yaw on the old one each time
public class RandomYRotation : MonoBehaviour
{
    // its own trait salt, so the angle survives EntitySeed gaining other traits
    private const uint SALT_ROTATION = 0xB5297A4Du;


    private Quaternion baseRotation;


    // Awake, before the first OnEnable can overwrite it: the prefab's own rotation is the sprite's pitch
    // (the seed quads are pitched 90 on x), and the yaw is applied on top of that
    private void Awake()
    {
        baseRotation = transform.rotation;
    }


    // In OnEnable, not Start: the ghost is parked inactive and re-activated by ShowPreview, which has
    // already put it at the cell, and a real spawn instantiates at its cell. Both want the cell read here
    private void OnEnable()
    {
        Vector3 position = transform.position;

        uint seed = EntitySeed.FromCell(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
        float angle = EntitySeed.Trait01(seed, SALT_ROTATION) * 360.0f - 180.0f;

        // about world Y, as the old random yaw was, so the pitched sprite still stands up the same way
        transform.rotation = Quaternion.Euler(0.0f, angle, 0.0f) * baseRotation;
    }
}
