using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


// Pulses an outline around a compendium entry while it has unseen changes, and clears itself
// on hover. One material instance per highlight because each entry's outline needs its own
// _RectSize, so its pixel width is measured in that outline's own rect space.
public class UnseenHighlight : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField] private Image outline;
    [SerializeField] private Shader outlineShader;
    [SerializeField] private float maxWidth = 4.0f;
    [SerializeField] private float pulseSpeed = 3.0f;

    private Material material;
    private Action onSeen;


    public bool IsShowing { get { return outline != null && outline.enabled; } }


    private void Awake()
    {
        if (outline == null) Debug.LogError("UnseenHighlight: no outline Image, so unseen changes cannot pulse", this);
        if (outlineShader == null) Debug.LogError("UnseenHighlight: no outline shader, so the outline cannot be drawn", this);

        // the prefab ships with the outline enabled; make sure it never flashes before the first bind
        if (outline != null) outline.enabled = false;
    }


    // Replaces any previously stored callback. Entries are recycled across pages and tabs, so a
    // replaced callback is safer than a subscribable event that would pile up handlers.
    public void Show(Color colour, Action onSeen)
    {
        this.onSeen = onSeen;

        if (outline == null) return;

        EnsureMaterial();

        if (material != null)
        {
            material.SetColor("_OutlineColor", colour);
            SetRect();
        }

        outline.enabled = true;
    }


    public void Hide()
    {
        onSeen = null;

        if (outline != null) outline.enabled = false;
    }


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsShowing) return;

        Action callback = onSeen;

        Hide();

        if (callback != null) callback();
    }


    private void EnsureMaterial()
    {
        if (material != null || outlineShader == null) return;

        material = new Material(outlineShader);
        material.SetFloat("_MaxWidth", maxWidth);
        material.SetFloat("_PulseSpeed", pulseSpeed);

        outline.material = material;
    }


    private void SetRect()
    {
        if (material == null || outline == null) return;

        Rect rect = outline.rectTransform.rect;
        material.SetVector("_Rect", new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));
    }


    // The entry's size is driven by a layout group, so on the first bind it can still be zero
    // (the book binds before its canvas has laid out). Re-measure once the rect settles.
    private void OnRectTransformDimensionsChange()
    {
        if (material == null || outline == null || !IsShowing) return;

        SetRect();
    }


    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        material = null;
    }
}
