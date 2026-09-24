using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// Pulses an outline around a compendium entry while it has unseen changes, and clears itself
// on hover. One material instance per highlight because each entry's outline needs its own
// _RectSize, so its width is measured in that outline's own rect units.
public class UnseenHighlight : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField] private Image outline;
    [SerializeField] private Shader outlineShader;
    [SerializeField] private float maxWidth = 6.0f;
    [SerializeField] private float pulseSpeed = 8.0f;
    [SerializeField] private float glowWidth = 14.0f;
    [SerializeField] private float glowIntensity = 0.8f;

    private Material material;
    private Action onSeen;

    // The wanted state, kept apart from outline.enabled: the book binds its pages while its canvas
    // is still inactive, so Show can run before this entry's Awake, and Awake must not undo it
    private bool showing;


    public bool IsShowing { get { return showing; } }


    private void Awake()
    {
        if (outline == null) Debug.LogError("UnseenHighlight: no outline Image, so unseen changes cannot pulse", this);
        if (outlineShader == null) Debug.LogError("UnseenHighlight: no outline shader, so the outline cannot be drawn", this);

        PrepareOutline();

        // the prefab ships with the outline enabled, so settle it to whatever was last asked for
        if (outline != null) outline.enabled = showing;
    }


    // Replaces any previously stored callback. Entries are recycled across pages and tabs, so a
    // replaced callback is safer than a subscribable event that would pile up handlers.
    public void Show(Color colour, Action onSeen)
    {
        this.onSeen = onSeen;
        showing = true;

        if (outline == null) return;

        PrepareOutline();
        EnsureMaterial();

        if (material != null)
        {
            material.SetColor("_OutlineColor", colour);
            SetRectSize();
        }

        outline.enabled = true;
    }


    public void Hide()
    {
        onSeen = null;
        showing = false;

        if (outline != null) outline.enabled = false;
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!showing) return;

        Action callback = onSeen;

        Hide();

        if (callback != null) callback();
    }


    // The shader reads the edge distance from the UVs, which only run 0..1 across the rect for a
    // Simple Image with no sprite. A sliced sprite warps them, so this is enforced rather than
    // left to the prefab.
    private void PrepareOutline()
    {
        if (outline == null) return;

        outline.sprite = null;
        outline.type = Image.Type.Simple;
        outline.raycastTarget = false;
    }


    private void EnsureMaterial()
    {
        if (material != null || outlineShader == null) return;

        material = new Material(outlineShader);
        material.SetFloat("_MaxWidth", maxWidth);
        material.SetFloat("_PulseSpeed", pulseSpeed);
        material.SetFloat("_GlowWidth", glowWidth);
        material.SetFloat("_GlowIntensity", glowIntensity);

        outline.material = material;
    }


    private void SetRectSize()
    {
        if (material == null || outline == null) return;

        Rect rect = outline.rectTransform.rect;
        material.SetVector("_RectSize", new Vector4(rect.width, rect.height, 0.0f, 0.0f));
    }


    // The entry's size is driven by a layout group, so on the first bind it can still be zero
    // (the book binds before its canvas has laid out). Re-measure once the rect settles.
    private void OnRectTransformDimensionsChange()
    {
        if (material == null || outline == null || !showing) return;

        SetRectSize();
    }


    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        material = null;
    }
}
