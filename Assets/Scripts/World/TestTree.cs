using UnityEngine;


// literally just for the puzzle completion test
// delete this ASAP
public class TestTree : MonoBehaviour
{
    [SerializeField] private TerrainTile waterTile;
    [SerializeField] private Puzzle myPuzzle;


    private Vector3Int cellPosition;


    // A saved tree is instantiated inside EntityManager's Awake, which can happen before
    // PlacementController and TerrainManager have their own Awake, so the subscription waits until
    // here where both are up. The sweep at the end is what catches the tree's own placement: the drop
    // that spawned it raised ItemPlaced before this Start ran.
    public void Start()
    {
        if (waterTile == null)
        {
            Debug.LogError("TestTree: set waterTile on the Spring Tree prefab !!");
        }

        if (TerrainManager.Instance == null || PlacementController.Instance == null)
        {
            Debug.LogError("TestTree: no TerrainManager or PlacementController in the scene, so this " +
                "tree can never notice the water it needs", this);
            return;
        }

        // this tree never moves, so the cell is resolved once
        cellPosition = TerrainManager.Instance.WorldToCell(transform.position);

        PlacementController.Instance.ItemPlaced += TryCompletePuzzle;

        // a tree placed next to water that was already there, or a saved tree loaded next to it, never
        // gets an ItemPlaced of its own
        SweepCheckPuzzle();
    }


    public void OnDestroy()
    {
        if (PlacementController.Instance != null)
        {
            PlacementController.Instance.ItemPlaced -= TryCompletePuzzle;
        }
    }


    private void TryCompletePuzzle(GameItem item, Vector3Int cell, GameObject entity)
    {
        if (Vector3Int.Distance(cell, cellPosition) > Mathf.Sqrt(3)) return;

        SweepCheckPuzzle();
    }


    private void SweepCheckPuzzle()
    {
        if (myPuzzle == null) return;

        if (!PuzzleManager.Instance.IsActive(myPuzzle)) return;

        for (int x = -1; x < 2; x++)
        {
            for (int y = -1; y < 2; y++)
            {
                for (int z = -1; z < 2; z++)
                {
                    if (x == y && y == z && x == 0) continue;    // this is cellPosition
                    Vector3Int checkCell = new Vector3Int(cellPosition.x + x, cellPosition.y + y, cellPosition.z + z);
                    TerrainTile checkTile = TerrainManager.Instance.GetTile(checkCell);
                    if (checkTile != null && checkTile == waterTile)
                    {
                        CompletePuzzle();
                        return;
                    }
                }
            }
        }
    }


    private void CompletePuzzle()
    {
        Debug.Log("Well done! You completed my puzzle!");

        // looked up now rather than cached in Start: EntityManager only adds the component after
        // Instantiate returns, so this is the first point it is guaranteed to exist
        EntityId entityId = GetComponent<EntityId>();

        if (entityId == null)
        {
            Debug.LogWarning("TestTree: no EntityId on this tree, so the save cannot record which " +
                "entity completed the puzzle", this);
        }

        PuzzleManager.Instance.Complete(myPuzzle, entityId);
    }
}
