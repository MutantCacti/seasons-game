using UnityEngine;


// literally just for the puzzle completion test
// delete this ASAP
public class TestTree : MonoBehaviour
{
    [SerializeField] private TerrainTile waterTile;
    [SerializeField] private Puzzle myPuzzle;


    private Vector3Int cellPosition;


    public void Awake()
    {
        if (waterTile == null)
        {
            Debug.LogError("TestTree: set waterTile on the Spring Tree prefab !!");
        }

        cellPosition = TerrainManager.Instance.WorldToCell(transform.position);

        PlacementController.Instance.ItemPlaced += TryCompletePuzzle;
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
        PuzzleManager.Instance.Complete(myPuzzle);
    }
}
