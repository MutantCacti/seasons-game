using System.Collections.Generic;
using UnityEngine;


// The Puzzles tab: every puzzle in registry order with what state it is in. Completed puzzles pan
// the camera to whatever completed them, and soft locked ones pulse and offer a checkpoint reset.
public class PuzzleTabManager : CompendiumTabManager
{
    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);

    private const string ACTIVE_TEXT = "This puzzle is active";
    private const string COMPLETE_TEXT = "You've completed this puzzle";
    private const string FAIL_TEXT = "You've failed this puzzle, reset to checkpoint?";


    private enum PuzzleState
    {
        Hidden,
        Active,
        Completed,
        SoftLocked,
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

    private Material silhouetteMaterial;
    private CameraController cameraController;
    private int rowsPerPage = -1;
    private bool warnedAboutMissingStatusRow;


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
    }


    private void OnDestroy()
    {
        if (silhouetteMaterial != null) Destroy(silhouetteMaterial);
        silhouetteMaterial = null;
    }


    // MARK: registry


    public int Count
    {
        get
        {
            PuzzleRegistry registry = Registry;
            return registry != null ? registry.Count : 0;
        }
    }


    private PuzzleRegistry Registry
    {
        get { return PuzzleManager.Instance != null ? PuzzleManager.Instance.Registry : null; }
    }


    // False for a missing registry, an out of range index or a hole in the list
    private bool TryGetPuzzle(int index, out Puzzle puzzle)
    {
        puzzle = null;

        PuzzleRegistry registry = Registry;
        if (registry == null) return false;
        if (index < 0 || index >= registry.Count) return false;

        puzzle = registry.Puzzles[index];

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


    // The book opens on the first soft locked puzzle, in registry order
    public override bool TryGetRequestedPage(out int pageIndex)
    {
        pageIndex = 0;

        PuzzleManager manager = PuzzleManager.Instance;
        if (manager == null || !manager.IsLoaded) return false;

        int count = Count;

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
                row.SetInteractable(PuzzleManager.Instance.TryGetCompletedBy(puzzle, out EntityId _));
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

        highlight.Hide();
    }


    // A soft locked puzzle glows for as long as it stays stuck. Hovering hides the highlight before
    // calling back, so the callback just puts it straight back up.
    private void ShowFailGlow(UnseenHighlight highlight, Puzzle puzzle)
    {
        highlight.Show(failTint, () =>
        {
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


    // MARK: actions


    // Closes the book first so the pan is not hidden behind it
    private void GoTo(Puzzle puzzle)
    {
        // checked again here, the entity can be removed while the book is open
        if (PuzzleManager.Instance == null) return;
        if (!PuzzleManager.Instance.TryGetCompletedBy(puzzle, out EntityId entity) || entity == null) return;

        CameraController view = ViewCamera;

        if (view == null)
        {
            Debug.LogWarning("PuzzleTabManager: no CameraController on the main camera, so the view cannot go to the puzzle", this);
            return;
        }

        if (CompendiumUIManager.Instance != null) CompendiumUIManager.Instance.RequestClose();

        view.PanToTransform(entity.transform);
    }


    // The scene reloads on success, so there is nothing else to tidy up here
    private void ResetPuzzle(Puzzle puzzle)
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
