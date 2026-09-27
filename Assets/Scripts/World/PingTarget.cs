using UnityEngine;


// Marks the point on a prefab that anything pointing at "where this entity is" should use, instead of
// the entity's root or the top of whatever it draws: a completion's ping, the ingredients flying out
// of it, and the compendium's "go to". Put it on an empty child where the point belongs (a tree's
// crown, the book's cover) and the prefab says where it is, without any puzzle having to know.
//
// A prefab may carry several; any one wins, whichever the search finds first
public class PingTarget : MonoBehaviour
{
    // The marker in this entity's hierarchy, if it has one. The only place the search exists
    public static PingTarget Find(EntityId entity)
    {
        if (entity == null) return null;

        return entity.GetComponentInChildren<PingTarget>(true);
    }


    // Where to point: the entity's marker when it has one, else the entity itself. Null only for a null
    // entity, so a caller that already has one can read .position without checking again
    public static Transform Resolve(EntityId entity)
    {
        if (entity == null) return null;

        PingTarget marker = Find(entity);

        return marker != null ? marker.transform : entity.transform;
    }


    // No fields to set, but the director has to see where the marker landed inside a prefab
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1.0f, 0.8f, 0.2f);

        Gizmos.DrawWireSphere(transform.position, 0.12f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.12f, transform.position + Vector3.up * 0.35f);
    }
}
