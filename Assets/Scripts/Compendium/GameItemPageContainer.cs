using UnityEngine;


// The serialized slots of a Game Item page: one CompendiumEntry per row. This lives on the
// root of the GameItem Page Container prefab so GameItemTabManager can fill the entries
// without knowing the exact child hierarchy.
public class GameItemPageContainer : MonoBehaviour
{
    [SerializeField] private CompendiumEntry[] entries;

    public CompendiumEntry[] Entries { get { return entries; } }
}
