using System;
using System.Collections.Generic;
using UnityEngine;


// The entities a brand new world starts with. The world itself is authored in the tilemaps and the save
// only stores what differs from them, so a fixture is simply the first difference a new world has: it is
// spawned through EntityManager once, when the world is created, and is an ordinary saved entity from
// then on.
//
// Deliberately not a flag on GameItem: an item that is never a fixture should not have to say so.
[CreateAssetMenu(
    fileName="WorldFixtures",
    menuName="Items/World Fixtures"
)]
public class WorldFixtures : ScriptableObject
{
    [Serializable]
    public struct Fixture
    {
        public GameItem item;

        // The column the fixture stands in, not a full cell. Occupancy is fixed in the tilemaps, so the
        // level is resolved when the world is created as the topmost occupied level of this column,
        // which also means a fixture follows an elevation being inserted underneath it.
        public Vector2Int column;
    }


    [SerializeField] private List<Fixture> fixtures = new List<Fixture>();


    public IReadOnlyList<Fixture> Entries { get { return fixtures; } }
    public int Count { get { return fixtures != null ? fixtures.Count : 0; } }
}
