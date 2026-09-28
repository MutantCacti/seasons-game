using System.Collections.Generic;
using UnityEngine;


// The endgame's surprise: Shift+G hands the player a few random items, flown in from the book like a
// puzzle's ingredients. Only while Endgame is unlocked, read from PuzzleManager at the moment of the
// press rather than kept, so a load gets it right and a reset to Break Ground turns it off again.
//
// The pool is a curated list rather than the whole registry: that holds fixtures (the Compendium),
// transitional items (sprouts, the hive tree) and reagents with nothing to react with, and the hidden
// flag can't sort them out since the seeds are hidden too.
public class EndgameGrantItems : MonoBehaviour
{
    [Tooltip("Shift+G only does anything while this is unlocked")]
    [SerializeField] private Puzzle endgame;

    [Tooltip("random picks per press, one of each; repeats stack")]
    [SerializeField] [Min(1)] private int itemCount = 5;

    [SerializeField] private List<GameItem> pool = new List<GameItem>();


    private InputManager input;


    public bool IsActive
    {
        get
        {
            PuzzleManager puzzles = PuzzleManager.Instance;
            return endgame != null && puzzles != null && puzzles.IsUnlocked(endgame);
        }
    }


    // MARK: lifecycle


    private void Awake()
    {
        if (endgame == null) Debug.LogError("EndgameGrantItems: no endgame puzzle assigned, so Shift+G never grants anything", this);
        if (pool == null || pool.Count == 0) Debug.LogError("EndgameGrantItems: the pool is empty, so Shift+G has nothing to grant", this);
    }


    private void OnEnable()
    {
        Subscribe();
    }


    // the InputManager lives in the persistent scene, so it is normally there by OnEnable; this catches
    // the odd order where it isn't
    private void Start()
    {
        Subscribe();

        if (input == null) Debug.LogError("EndgameGrantItems: no InputManager, so Shift+G is never heard", this);
    }


    private void OnDisable()
    {
        if (input != null) input.onGrantItems -= OnGrantItems;
        input = null;
    }


    private void Subscribe()
    {
        if (input != null || InputManager.Instance == null) return;

        input = InputManager.Instance;
        input.onGrantItems += OnGrantItems;
    }


    private void OnGrantItems()
    {
        if (InputManager.IsTypingText) return;
        if (!IsActive) return;

        GrantRandomItems();
    }


    // MARK: granting


    // itemCount random picks from the pool, grouped per item and granted in one go, so they fly in from
    // the book together. Not gated on Endgame: the key is, this is the thing it does
    [ContextMenu("Grant Random Items")]
    public void GrantRandomItems()
    {
        PuzzleManager puzzles = PuzzleManager.Instance;
        if (puzzles == null || pool == null || pool.Count == 0) return;

        Dictionary<GameItem, int> picked = new Dictionary<GameItem, int>();

        for (int i = 0; i < itemCount; i++)
        {
            GameItem item = pool[Random.Range(0, pool.Count)];
            if (item == null) continue;

            picked.TryGetValue(item, out int count);
            picked[item] = count + 1;
        }

        List<IngredientGrant> grants = new List<IngredientGrant>();

        foreach (KeyValuePair<GameItem, int> pick in picked)
        {
            grants.Add(new IngredientGrant(pick.Key, pick.Value));
        }

        EntityId book = GetComponentInParent<EntityId>();

        puzzles.GrantItems(book != null ? GrantOrigin.Entity(book) : GrantOrigin.None, grants);
    }


    // MARK: validation


    private void OnValidate()
    {
        if (pool == null) return;

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) Debug.LogWarning($"EndgameGrantItems: pool entry {i} is empty, so a pick of it grants nothing", this);
        }
    }
}
