using UnityEngine;


// Base class for a prefab-specific placement flourish
// e.g. a tree puffing some leaves out when yo uplace it
public abstract class PlacementAnimation : MonoBehaviour
{
    public abstract void Play();
}
