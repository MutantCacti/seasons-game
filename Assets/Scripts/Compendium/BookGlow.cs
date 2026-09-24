using UnityEngine;
using UnityEngine.Rendering;


// Pulses one outline around the whole 3D compendium book while any tab has something unseen, coloured
// by the loudest thing waiting: a soft lock over a completion over a discovery. The outline itself is
// drawn by two Render Objects features on the renderer (see BookGlowOutline.shader), which only draw
// the BookGlow layer. So this moves the book's renderers onto that layer while it should glow and back
// again when it should not, and off costs nothing: nothing is on the layer to draw.
public class BookGlow : MonoBehaviour
{
    [SerializeField] private CompendiumUIManager compendium;

    // the book.fbx instance; every MeshRenderer under it joins the outline
    [SerializeField] private Transform bookModel;

    [SerializeField] private string glowLayerName = "BookGlow";

    [Header("Colours")]
    [SerializeField] private Color discoveryColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);   // #EDC43B, as the unlocked ping
    [SerializeField] private Color completionColour = new Color(141.0f / 255.0f, 191.0f / 255.0f, 90.0f / 255.0f, 1.0f);  // #8DBF5A, the complete ping
    [SerializeField] private Color softLockColour = new Color(210.0f / 255.0f, 87.0f / 255.0f, 63.0f / 255.0f, 1.0f);     // #D2573F, the fail ping

    [Header("Pulse")]
    // same default as UnseenHighlight, so the book and the page outlines pulse together
    [SerializeField] private float pulseSpeed = 8.0f;
    [SerializeField] private float minWidth = 1.5f;     // screen pixels; never zero, so it never vanishes
    [SerializeField] private float maxWidth = 6.0f;
    [SerializeField] [Range(0.0f, 1.0f)] private float minAlpha = 0.55f;
    [SerializeField] [Range(0.0f, 1.0f)] private float maxAlpha = 1.0f;


    private static readonly int ColourId = Shader.PropertyToID("_BookGlowColor");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_BookGlowPulseSpeed");
    private static readonly int WidthId = Shader.PropertyToID("_BookGlowWidth");
    private static readonly int AlphaId = Shader.PropertyToID("_BookGlowAlpha");

    private const int OUTLINE_UV_CHANNEL = 3;


    private MeshRenderer[] parts;
    private int[] restLayers;
    private int glowLayer = -1;

    // starts dirty, so the first LateUpdate settles whatever the tabs bound before this woke up
    private bool dirty = true;
    private bool glowing;
    private UnseenPriority priority = UnseenPriority.None;


    public UnseenPriority Priority { get { return priority; } }


    // MARK: lifecycle


    private void Awake()
    {
        if (compendium == null) compendium = GetComponent<CompendiumUIManager>();
        if (compendium == null) Debug.LogError("BookGlow: no CompendiumUIManager, so there are no tabs to glow for", this);

        if (bookModel == null)
        {
            Debug.LogError("BookGlow: no book model assigned, so every MeshRenderer under the compendium joins the outline", this);
            bookModel = transform;
        }

        glowLayer = LayerMask.NameToLayer(glowLayerName);
        if (glowLayer < 0) Debug.LogError($"BookGlow: no '{glowLayerName}' layer in Tags & Layers, so the book cannot glow", this);

        // inactive ones too: the loose sheets are switched off while the book is read
        parts = bookModel.GetComponentsInChildren<MeshRenderer>(true);
        restLayers = new int[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            restLayers[i] = parts[i].gameObject.layer;
            CheckOutlineData(parts[i]);
        }

        if (parts.Length == 0) Debug.LogError("BookGlow: no MeshRenderers under the book model, so there is nothing to outline", this);
    }


    private void Start()
    {
        CompendiumTabManager.UnseenChanged += MarkDirty;

        if (compendium != null) compendium.OpenChanged += MarkDirty;

        // Tabs prune and seed their unseen lists once puzzles load, which fires UnseenChanged anyway;
        // this is the belt to those braces. Sticky, so it answers at once if loading already finished.
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzlesLoaded += MarkDirty;
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.NewWorldConstructed += MarkDirty;

        dirty = true;
    }


    private void OnDestroy()
    {
        CompendiumTabManager.UnseenChanged -= MarkDirty;

        if (compendium != null) compendium.OpenChanged -= MarkDirty;
        if (PuzzleManager.Instance != null) PuzzleManager.Instance.OnPuzzlesLoaded -= MarkDirty;
        if (SaveDataManager.Instance != null) SaveDataManager.Instance.NewWorldConstructed -= MarkDirty;

        SetGlowing(false);
    }


    // One recompute per frame at most, however many unseen entries a load marks in a batch. Only a
    // bool check on frames where nothing changed.
    private void LateUpdate()
    {
        if (!dirty) return;

        dirty = false;
        Refresh();
    }


    private void MarkDirty()
    {
        dirty = true;
    }


    // MARK: glow


    private void Refresh()
    {
        priority = HighestUnseen();

        // hidden while the book is being read (or heading there), keyed off where the read is going
        bool open = compendium != null && compendium.IsOpen;
        bool shouldGlow = priority != UnseenPriority.None && !open && glowLayer >= 0;

        if (shouldGlow) ApplyGlobals();

        SetGlowing(shouldGlow);
    }


    private UnseenPriority HighestUnseen()
    {
        if (compendium == null) return UnseenPriority.None;

        UnseenPriority highest = UnseenPriority.None;
        int count = compendium.TabCount;

        for (int i = 0; i < count; i++)
        {
            CompendiumTabManager tab = compendium.GetTab(i);
            if (tab == null) continue;

            UnseenPriority tabPriority = tab.HighestUnseen();

            if (tabPriority == UnseenPriority.SoftLock) return tabPriority;
            if (tabPriority > highest) highest = tabPriority;
        }

        return highest;
    }


    // Globals rather than material properties, so the shared override material asset is never
    // written to (in the Editor that would dirty the .mat on disk)
    private void ApplyGlobals()
    {
        Shader.SetGlobalColor(ColourId, ColourOf(priority));
        Shader.SetGlobalFloat(PulseSpeedId, pulseSpeed);
        Shader.SetGlobalVector(WidthId, new Vector4(minWidth, maxWidth, 0.0f, 0.0f));
        Shader.SetGlobalVector(AlphaId, new Vector4(minAlpha, maxAlpha, 0.0f, 0.0f));
    }


    private Color ColourOf(UnseenPriority value)
    {
        switch (value)
        {
            case UnseenPriority.SoftLock: return softLockColour;
            case UnseenPriority.Completion: return completionColour;
            default: return discoveryColour;
        }
    }


    // only touches the hierarchy on the frame the state actually flips
    private void SetGlowing(bool value)
    {
        if (value == glowing || parts == null) return;

        glowing = value;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null) continue;

            parts[i].gameObject.layer = value ? glowLayer : restLayers[i];
        }
    }


    // The hull pass pushes along UV3, which only BookOutlinePostprocessor puts there. Without it the
    // part draws no outline at all, so say so rather than leave a silent gap.
    private void CheckOutlineData(MeshRenderer part)
    {
        MeshFilter filter = part.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;

        if (mesh == null || mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + OUTLINE_UV_CHANNEL)) return;

        Debug.LogError($"BookGlow: '{part.name}' has no outline data in UV{OUTLINE_UV_CHANNEL}, so it is left out of the " +
            "book's outline - reimport book.fbx (BookOutlinePostprocessor bakes it)", part);
    }
}
