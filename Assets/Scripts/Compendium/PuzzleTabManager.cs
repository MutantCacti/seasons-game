using System.Collections.Generic;
using UnityEngine;


// The Puzzles tab: every puzzle in registry order with what state it is in. Completed puzzles pan
// the camera to whatever completed them, and soft locked ones pulse and offer a checkpoint reset.
public class PuzzleTabManager : CompendiumTabManager
{
    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);

    private const string ACTIVE_TEXT = "This puzzle is active";
    private const string COMPLETE_TEXT = "You've completed this puzzle";
    private const string FAIL_TEXT = "You've failed this puzzle";


    private enum PuzzleState
    {
        Hidden,
        Active,
        Completed,
        SoftLocked,
    }


    // What last happened to a puzzle the player has not looked at yet; the latest one wins.
    // Saved as its int, so these values are pinned: never reorder or renumber them.
    private enum UnseenKind
    {
        Unlocked = 0,
        Completed = 1,
        Failed = 2,
    }


    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;

    [Header("Status icons")]
    [SerializeField] private Sprite hiddenSprite;
    [SerializeField] private Sprite completeSprite;
    [SerializeField] private Sprite failSprite;

    [Header("Colours")]
    [SerializeField] private Color hiddenTint = new Color(181.0f / 255.0f, 172.0f / 255.0f, 158.0f / 255.0f, 1.0f);   // #B5AC9E
    [SerializeField] private Color activeTint = new Color(138.0f / 255.0f, 129.0f / 255.0f, 116.0f / 255.0f, 1.0f);   // #8A8174
    [SerializeField] private Color completeTint = new Color(141.0f / 255.0f, 191.0f / 255.0f, 90.0f / 255.0f, 1.0f);  // #8DBF5A, the complete ping
    [SerializeField] private Color failTint = new Color(210.0f / 255.0f, 87.0f / 255.0f, 63.0f / 255.0f, 1.0f);       // #D2573F, the fail ping
    [SerializeField] private Color unlockedColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f); // #EDC43B, as GameItemTabManager

    private Material silhouetteMaterial;
    private CameraController cameraController;
    private int rowsPerPage = -1;
    private bool warnedAboutMissingStatusRow;

    // Registry entries the book is built from, minus the hidden ones (see Puzzle.HideInCompendium).
    // Paging, binding and the page arrows all index this, never the registry itself, so a hidden
    // puzzle takes no page, no row and no glow
    private readonly List<Puzzle> visiblePuzzles = new List<Puzzle>();

    // unseen puzzles are saved through the base class, keyed by Puzzle.Id with an UnseenKind
    private readonly List<UnseenSave> savedUnseen = new List<UnseenSave>();
    private bool baselineEmpty;


    public override GameObject PagePrefab { get { return pagePrefab; } }


    public override int PageCount
    {
        get
        {
            int rows = RowsPerPage;
            return (Count + rows - 1) / rows;
        }
    }


    private void Awake()
    {
        if (pagePrefab == null) Debug.LogError("PuzzleTabManager: no page prefab, so the tab cannot render pages", this);
        if (silhouetteShader == null) Debug.LogError("PuzzleTabManager: no silhouette shader, so undiscovered puzzles render normally instead of greyed out", this);
        if (hiddenSprite == null || completeSprite == null || failSprite == null) Debug.LogError("PuzzleTabManager: a status sprite is missing, so some puzzle states show no status icon", this);

        // Captured now because EntityManager clears IsConstructingNewWorld later this same frame,
        // once it has finished seeding the world fixtures (this compendium included).
        baselineEmpty = SaveDataManager.Instance != null && SaveDataManager.Instance.IsConstructingNewWorld;
    }


    private void Start()
    {
        PuzzleManager manager = PuzzleManager.Instance;

        if (manager == null)
        {
            Debug.LogError("PuzzleTabManager: no PuzzleManager, so the tab has no puzzles and nothing is ever marked unseen", this);
            return;
        }

        RebuildVisiblePuzzles();

        manager.OnPuzzleUnlocked += OnPuzzleUnlocked;
        manager.OnPuzzleComplete += OnPuzzleComplete;
        manager.OnPuzzleSoftLocked += OnPuzzleSoftLocked;
        manager.OnPuzzleSoftLockCleared += OnPuzzleSoftLockCleared;

        // GameItemTabManager takes its baseline in Start, but this one waits for the puzzles to load:
        // the compendium is an entity, so this Start can run before PuzzleManager has restored
        // anything. Sticky, so it answers at once if loading already finished.
        manager.OnPuzzlesLoaded += OnPuzzlesLoaded;
    }


    private void OnDestroy()
    {
        PuzzleManager manager = PuzzleManager.Instance;

        if (manager != null)
        {
            manager.OnPuzzleUnlocked -= OnPuzzleUnlocked;
            manager.OnPuzzleComplete -= OnPuzzleComplete;
            manager.OnPuzzleSoftLocked -= OnPuzzleSoftLocked;
            manager.OnPuzzleSoftLockCleared -= OnPuzzleSoftLockCleared;
            manager.OnPuzzlesLoaded -= OnPuzzlesLoaded;
        }

        if (silhouetteMaterial != null) Destroy(silhouetteMaterial);
        silhouetteMaterial = null;
    }


    // MARK: registry


    // Once, in Start: the registry is authored data, so a hidden puzzle becoming visible mid-session
    // (an editor tweak during play) is not worth watching for
    private void RebuildVisiblePuzzles()
    {
        visiblePuzzles.Clear();

        PuzzleRegistry registry = Registry;
        if (registry == null) return;

        for (int i = 0; i < registry.Count; i++)
        {
            Puzzle puzzle = registry.Entries[i];
            if (puzzle == null) continue;
            if (puzzle.HideInCompendium) continue;

            visiblePuzzles.Add(puzzle);
        }
    }


    public int Count { get { return visiblePuzzles.Count; } }


    private PuzzleRegistry Registry
    {
        get { return PuzzleManager.Instance != null ? PuzzleManager.Instance.Registry : null; }
    }


    // False for a missing registry, an out of range index or a hole in the list
    private bool TryGetPuzzle(int index, out Puzzle puzzle)
    {
        puzzle = null;

        if (index < 0 || index >= visiblePuzzles.Count) return false;

        puzzle = visiblePuzzles[index];

        return puzzle != null;
    }


    // Read once from the page prefab, because the status row makes the rows taller and the prefab
    // is where it is decided whether two or three of them fit
    private int RowsPerPage
    {
        get
        {
            if (rowsPerPage > 0) return rowsPerPage;

            PuzzlePageContainer container = pagePrefab != null ? pagePrefab.GetComponent<PuzzlePageContainer>() : null;
            int rows = container != null && container.Entries != null ? container.Entries.Length : 0;

            if (rows <= 0)
            {
                Debug.LogError("PuzzleTabManager: the page prefab has no PuzzlePageContainer entries, so pages fall back to 1 row per page", this);
                rows = 1;
            }

            rowsPerPage = rows;
            return rowsPerPage;
        }
    }


    // MARK: requested page


    // One shot: the next open goes to this puzzle's page instead, e.g. the one whose fail ping was
    // clicked. Static because the ping asks before the book's canvas has ever been bound.
    private static string requestedPuzzleId;


    public static void RequestPuzzle(string puzzleId)
    {
        requestedPuzzleId = puzzleId;
    }


    // The book opens on the requested puzzle if it is still soft locked, else on the first soft locked
    // puzzle, in registry order
    public override bool TryGetRequestedPage(out int pageIndex)
    {
        pageIndex = 0;

        string requested = requestedPuzzleId;
        requestedPuzzleId = null;

        PuzzleManager manager = PuzzleManager.Instance;
        if (manager == null || !manager.IsLoaded) return false;

        int count = Count;

        if (!string.IsNullOrEmpty(requested) && manager.Registry != null && manager.Registry.TryGetById(requested, out Puzzle wanted)
            && manager.IsSoftLocked(wanted))
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryGetPuzzle(i, out Puzzle puzzle) || puzzle != wanted) continue;

                pageIndex = i / RowsPerPage;
                return true;
            }

            // The requested puzzle is soft locked but hidden, so it has no page to jump to. The book
            // still opens on this tab, at the spread it was left on, rather than turning to an
            // unrelated puzzle the player did not ask about
            if (wanted.HideInCompendium)
            {
                pageIndex = RememberedPage;
                return true;
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!TryGetPuzzle(i, out Puzzle puzzle)) continue;
            if (!manager.IsSoftLocked(puzzle)) continue;

            pageIndex = i / RowsPerPage;
            return true;
        }

        return false;
    }


    // MARK: binding


    public override void BindPage(GameObject page, int pageIndex)
    {
        PuzzlePageContainer container = page != null ? page.GetComponent<PuzzlePageContainer>() : null;
        if (container == null) return;

        CompendiumEntry[] entries = container.Entries;
        if (entries == null) return;

        int count = Count;
        int first = pageIndex * RowsPerPage;
        bool hasContent = pageIndex >= 0 && first < count;

        for (int i = 0; i < entries.Length; i++)
        {
            CompendiumEntry entry = entries[i];
            if (entry == null) continue;

            Puzzle puzzle = null;
            if (hasContent) TryGetPuzzle(first + i, out puzzle);

            BindEntry(entry, puzzle);
            entry.gameObject.SetActive(puzzle != null);
        }
    }


    private void BindEntry(CompendiumEntry entry, Puzzle puzzle)
    {
        PuzzleStatusRow row = GetStatusRow(entry);
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();

        if (puzzle == null)
        {
            entry.Clear();
            if (row != null) row.Clear();
            if (highlight != null) highlight.Hide();
            return;
        }

        PuzzleState state = StateOf(puzzle);

        if (state == PuzzleState.Hidden)
        {
            entry.SetImage(puzzle.Icon);
            entry.SetIconMaterial(SilhouetteMaterial);
            entry.SetNameText("Undiscovered");
            entry.SetDescriptionText(string.Empty);
        }
        else
        {
            entry.SetImage(puzzle.Icon);
            entry.SetIconMaterial(null);
            entry.SetNameText(puzzle.Name);
            entry.SetDescriptionText(puzzle.Description);
        }

        if (row != null) BindStatus(row, puzzle, state);

        BindHighlight(highlight, puzzle, state);
    }


    // SetStatus goes first because it stops any pulse, and SetPulsing goes last so it is the one
    // that decides whether this row pulses
    private void BindStatus(PuzzleStatusRow row, Puzzle puzzle, PuzzleState state)
    {
        switch (state)
        {
            case PuzzleState.Hidden:
                row.SetStatus(hiddenSprite, hiddenTint);
                row.SetText(string.Empty);
                row.SetInteractable(false);
                row.SetOnClick(null);
                break;

            case PuzzleState.Active:
                row.SetStatus(completeSprite, activeTint);
                row.SetText(ACTIVE_TEXT);
                row.SetInteractable(false);
                row.SetOnClick(null);
                break;

            case PuzzleState.Completed:
                row.SetStatus(completeSprite, completeTint);
                row.SetText(COMPLETE_TEXT);

                // nothing to go to once the entity that did it has left the world
                row.SetInteractable(PuzzleManager.Instance.TryGetCreditTarget(puzzle, out EntityId _, out Vector3 _));
                row.SetOnClick(() => GoTo(puzzle));
                break;

            case PuzzleState.SoftLocked:
                row.SetStatus(failSprite, failTint);
                row.SetText(FAIL_TEXT);
                row.SetInteractable(true);
                row.SetOnClick(() => ResetPuzzle(puzzle));
                break;
        }

        row.SetPulsing(state == PuzzleState.SoftLocked);
    }


    private void BindHighlight(UnseenHighlight highlight, Puzzle puzzle, PuzzleState state)
    {
        if (highlight == null) return;

        if (state == PuzzleState.SoftLocked)
        {
            ShowFailGlow(highlight, puzzle);
            return;
        }

        if (state != PuzzleState.Hidden && TryGetUnseen(puzzle.Id, out int kind))
        {
            highlight.Show(ColourOf((UnseenKind)kind), () => ClearUnseen(puzzle.Id));
            return;
        }

        highlight.Hide();
    }


    // A soft locked puzzle glows for as long as it stays stuck, whether or not it has been seen.
    // Hovering still marks it seen, but it hides the highlight before calling back, so the callback
    // puts it straight back up.
    private void ShowFailGlow(UnseenHighlight highlight, Puzzle puzzle)
    {
        highlight.Show(failTint, () =>
        {
            ClearUnseen(puzzle.Id);

            if (highlight != null && PuzzleManager.Instance != null && PuzzleManager.Instance.IsSoftLocked(puzzle))
            {
                ShowFailGlow(highlight, puzzle);
            }
        });
    }


    private PuzzleStatusRow GetStatusRow(CompendiumEntry entry)
    {
        PuzzleStatusRow row = entry.GetComponent<PuzzleStatusRow>();

        if (row == null && !warnedAboutMissingStatusRow)
        {
            warnedAboutMissingStatusRow = true;
            Debug.LogError("PuzzleTabManager: a puzzle entry has no PuzzleStatusRow, so it shows no status and cannot be acted on", entry);
        }

        return row;
    }


    private static PuzzleState StateOf(Puzzle puzzle)
    {
        PuzzleManager manager = PuzzleManager.Instance;

        if (manager == null || !manager.IsUnlocked(puzzle)) return PuzzleState.Hidden;
        if (manager.IsCompleted(puzzle)) return PuzzleState.Completed;
        if (manager.IsSoftLocked(puzzle)) return PuzzleState.SoftLocked;

        return PuzzleState.Active;
    }


    private Color ColourOf(UnseenKind kind)
    {
        switch (kind)
        {
            case UnseenKind.Completed: return completeTint;
            case UnseenKind.Failed: return failTint;
            default: return unlockedColour;
        }
    }


    // MARK: unseen


    // A soft lock outranks a completion, which outranks anything merely new. A stuck puzzle stays red
    // for as long as it is stuck, seen or not, so it's read from the live state and not the unseen list.
    // Over the whole registry, hidden puzzles included: they have no row to show red on, but the book
    // and the tab still point at the failures the player cannot fix from anywhere else
    public override UnseenPriority HighestUnseen()
    {
        if (AnySoftLocked()) return UnseenPriority.SoftLock;

        UnseenPriority highest = UnseenPriority.None;
        int count = UnseenCount;

        for (int i = 0; i < count; i++)
        {
            if (!TryGetUnseenAt(i, out int kind)) continue;

            UnseenPriority priority = PriorityOf((UnseenKind)kind);

            if (priority == UnseenPriority.SoftLock) return priority;
            if (priority > highest) highest = priority;
        }

        return highest;
    }


    // Same ranking, over just the puzzles laid out on those pages
    public override UnseenPriority HighestUnseenInPages(int firstPage, int lastPage)
    {
        int rows = RowsPerPage;
        int first = Mathf.Max(firstPage, 0) * rows;
        int end = Mathf.Min((lastPage + 1) * rows, Count);

        if (AnySoftLocked(first, end)) return UnseenPriority.SoftLock;

        UnseenPriority highest = UnseenPriority.None;

        for (int i = first; i < end; i++)
        {
            if (!TryGetPuzzle(i, out Puzzle puzzle)) continue;
            if (!TryGetUnseen(puzzle.Id, out int kind)) continue;

            UnseenPriority priority = PriorityOf((UnseenKind)kind);

            if (priority == UnseenPriority.SoftLock) return priority;
            if (priority > highest) highest = priority;
        }

        return highest;
    }


    // Over the whole registry, so a hidden puzzle's soft lock still fires the tab and book red. The
    // page range version stays on the visible list: an arrow must not point at a page that isn't there
    private bool AnySoftLocked()
    {
        PuzzleManager manager = PuzzleManager.Instance;
        PuzzleRegistry registry = Registry;

        if (manager == null || registry == null) return false;

        for (int i = 0; i < registry.Count; i++)
        {
            if (manager.IsSoftLocked(registry.Entries[i])) return true;
        }

        return false;
    }


    private bool AnySoftLocked(int first, int end)
    {
        PuzzleManager manager = PuzzleManager.Instance;
        if (manager == null) return false;

        for (int i = first; i < end; i++)
        {
            if (TryGetPuzzle(i, out Puzzle puzzle) && manager.IsSoftLocked(puzzle)) return true;
        }

        return false;
    }


    private static UnseenPriority PriorityOf(UnseenKind kind)
    {
        switch (kind)
        {
            case UnseenKind.Failed: return UnseenPriority.SoftLock;
            case UnseenKind.Completed: return UnseenPriority.Completion;
            default: return UnseenPriority.Discovery;
        }
    }


    private void OnPuzzleUnlocked(Puzzle puzzle)
    {
        MarkPuzzleUnseen(puzzle, UnseenKind.Unlocked);
    }


    private void OnPuzzleComplete(Puzzle puzzle)
    {
        MarkPuzzleUnseen(puzzle, UnseenKind.Completed);
    }


    // Also raised again about half a second after every load for a puzzle that is still stuck,
    // so a reload brings its fail highlight back. That is intended.
    private void OnPuzzleSoftLocked(Puzzle puzzle)
    {
        // A hidden puzzle has no row to put a fail glow on, but it can still be stuck: the glows rank
        // off live state, so they re-rank even though nothing unseen is added
        if (puzzle != null && puzzle.HideInCompendium)
        {
            RaiseUnseenChanged();
            return;
        }

        MarkPuzzleUnseen(puzzle, UnseenKind.Failed);

        // the glows rank soft locks by live state, so they re-rank even when the unseen list didn't change
        RaiseUnseenChanged();
    }


    // No longer stuck: nothing new to mark, but the row still shows the fail state it was bound with,
    // so the pages are re-bound. The fail glow follows IsSoftLocked by itself, and any Failed unseen
    // flag is left in the save rather than cleared, because the pending-completion flush prunes it and
    // a completion the player never saw is the one thing this tab must not swallow
    private void OnPuzzleSoftLockCleared(Puzzle puzzle)
    {
        RaiseChanged();
        RaiseUnseenChanged();
    }


    private void MarkPuzzleUnseen(Puzzle puzzle, UnseenKind kind)
    {
        if (puzzle == null) return;

        // a hidden puzzle has no page for an unseen flag to point at, so it is never marked
        if (puzzle.HideInCompendium) return;

        MarkUnseen(puzzle.Id, (int)kind);
        RaiseChanged();
    }


    // A new world starts from an empty baseline, so every puzzle the player begins with is unseen.
    // A loaded world keeps what it saved, minus anything the restored puzzles now contradict.
    private void OnPuzzlesLoaded()
    {
        PuzzleManager manager = PuzzleManager.Instance;
        if (manager == null) return;

        if (baselineEmpty) MarkStartingPuzzlesUnseen(manager);
        else PruneSavedUnseen(manager);
    }


    private void MarkStartingPuzzlesUnseen(PuzzleManager manager)
    {
        bool changed = false;
        int count = Count;

        for (int i = 0; i < count; i++)
        {
            if (!TryGetPuzzle(i, out Puzzle puzzle)) continue;
            if (!manager.IsUnlocked(puzzle)) continue;

            // an event this frame may already have marked it with something more recent
            if (TryGetUnseen(puzzle.Id, out int _)) continue;

            MarkUnseen(puzzle.Id, (int)UnseenKind.Unlocked, false);
            changed = true;
        }

        // one save for the whole baseline rather than one per puzzle
        if (!changed) return;

        SaveCompendium();
        RaiseChanged();
    }


    // The save is global, so a checkpoint reset rolls the puzzles back but not this. Anything the
    // restored state contradicts goes; a Failed on a still-active puzzle stays, because the soft lock
    // re-announce half a second after load confirms it (or it is already stale and harmless). A hidden
    // puzzle's flags go too: an unseen flag on a page the book no longer shows only glows for nothing
    private void PruneSavedUnseen(PuzzleManager manager)
    {
        PuzzleRegistry registry = manager.Registry;
        if (registry == null) return;

        CopyUnseen(savedUnseen);

        bool pruned = false;

        for (int i = 0; i < savedUnseen.Count; i++)
        {
            string id = savedUnseen[i].id;
            UnseenKind kind = (UnseenKind)savedUnseen[i].kind;

            if (!registry.TryGetById(id, out Puzzle puzzle))
            {
                Debug.LogWarning($"PuzzleTabManager: the save holds unseen puzzle '{id}', but no puzzle in " +
                    $"'{registry.name}' has that id - dropping it", this);

                ClearUnseen(id, false);
                pruned = true;
                continue;
            }

            if (puzzle.HideInCompendium)
            {
                ClearUnseen(id, false);
                pruned = true;
                continue;
            }

            bool contradicted = !manager.IsUnlocked(puzzle)
                || (kind == UnseenKind.Completed && !manager.IsCompleted(puzzle))
                || (kind == UnseenKind.Failed && manager.IsCompleted(puzzle));

            if (!contradicted) continue;

            ClearUnseen(id, false);
            pruned = true;
        }

        savedUnseen.Clear();

        if (!pruned) return;

        SaveCompendium();
        RaiseChanged();
    }


    // MARK: actions


    // Closes the book first so the pan is not hidden behind it
    private void GoTo(Puzzle puzzle)
    {
        // checked again here, the entity can be removed while the book is open
        if (PuzzleManager.Instance == null) return;
        // an entity, or the cell it was credited to
        if (!PuzzleManager.Instance.TryGetCreditTarget(puzzle, out EntityId entity, out Vector3 point)) return;

        CameraController view = ViewCamera;

        if (view == null)
        {
            Debug.LogWarning("PuzzleTabManager: no CameraController on the main camera, so the view cannot go to the puzzle", this);
            return;
        }

        if (CompendiumUIManager.Instance != null) CompendiumUIManager.Instance.RequestClose();

        // the entity's own marker when its prefab has one, so the camera goes to the marked point
        if (entity != null) view.PanToTransform(PingTarget.Resolve(entity));
        else view.PanToPoint(point);
    }


    // Asks before resetting. A missing modal logs and falls back to the old straight-through reset,
    // so a broken or missing prefab can never take the reset button away entirely. The modal names the
    // puzzle it goes back to, which can be an earlier one than the puzzle that failed
    private void ResetPuzzle(Puzzle puzzle)
    {
        Puzzle target = ResolveResetTarget(puzzle);

        ConfirmationModal modal = ConfirmationModal.Instance;

        if (modal == null)
        {
            Debug.LogWarning("PuzzleTabManager: no ConfirmationModal, so the reset is not confirmed", this);
            ConfirmReset(target);
            return;
        }

        modal.Show(() => ConfirmReset(target), null, "_PUZZLE_", target.Name);
    }


    // The puzzle's reset target, or the puzzle itself when the target has no checkpoint (never
    // unlocked in this world, say), so the player is never left without a reset
    private Puzzle ResolveResetTarget(Puzzle puzzle)
    {
        Puzzle target = puzzle.ResetTarget;
        if (target == puzzle) return puzzle;

        if (CheckpointManager.Instance != null && CheckpointManager.Instance.HasCheckpoint(target)) return target;

        Debug.LogWarning($"PuzzleTabManager: '{puzzle.name}' resets to '{target.name}', which has no checkpoint, " +
            $"so it resets to its own instead", puzzle);

        return puzzle;
    }


    // The scene reloads on success, so there is nothing else to tidy up here
    private void ConfirmReset(Puzzle puzzle)
    {
        if (CheckpointManager.Instance != null && CheckpointManager.Instance.ResetTo(puzzle)) return;

        Debug.LogWarning($"PuzzleTabManager: could not reset '{puzzle.name}' to its checkpoint", this);
    }


    private CameraController ViewCamera
    {
        get
        {
            if (cameraController == null && Camera.main != null) cameraController = Camera.main.GetComponent<CameraController>();

            return cameraController;
        }
    }


    private Material SilhouetteMaterial
    {
        get
        {
            if (silhouetteMaterial == null && silhouetteShader != null)
            {
                silhouetteMaterial = new Material(silhouetteShader);
                silhouetteMaterial.SetColor("_SilhouetteColor", SILHOUETTE_COLOR);
            }

            return silhouetteMaterial;
        }
    }
}
