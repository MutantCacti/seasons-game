using System.Collections.Generic;
using UnityEngine;


// The set of options the game knows about, in the order they should be applied. The manager reads
// defaults from here and never writes back to it.
[CreateAssetMenu(menuName = "Global Options/Global Options Database")]
public class GlobalOptionsDatabase : ScriptableObject
{
    [SerializeField] private List<GlobalOption> allGlobalOptions = new List<GlobalOption>();


    public List<GlobalOption> AllGlobalOptions { get { return allGlobalOptions; } }
}
