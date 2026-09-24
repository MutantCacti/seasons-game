using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


// Bakes an outline direction into UV3 of every mesh in the compendium book, for BookGlowOutline's hull
// pass. The book is hard-edged boxes (split normals, so a normal-extruded hull tears at every corner)
// plus zero-thickness page quads (whose normals would push the hull straight off the page, not around it).
// So each vertex stores which side of its mesh's bounds centre it sits on, per axis: pushing along that
// grows a box by the same amount on every face with no gaps, and grows a flat quad in its own plane.
public class BookOutlinePostprocessor : AssetPostprocessor
{
    // Scoped to the one model by path, so no other FBX pays for this or gets an extra channel. Moving
    // or renaming book.fbx silently turns the bake off; BookGlow logs an error when UV3 is missing.
    private const string BOOK_PATH_SUFFIX = "Art/book.fbx";

    private const int OUTLINE_UV_CHANNEL = 3;

    // relative to the mesh's largest extent, so a flat quad's thickness axis counts as zero
    private const float DEADZONE = 1e-4f;


    // bump whenever the bake changes, so Unity reimports the book with the new data
    public override uint GetVersion()
    {
        return 1;
    }


    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.EndsWith(BOOK_PATH_SUFFIX, System.StringComparison.OrdinalIgnoreCase)) return;

        HashSet<Mesh> baked = new HashSet<Mesh>();

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Bake(filter.sharedMesh, baked);
        }

        foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Bake(skinned.sharedMesh, baked);
        }
    }


    private static void Bake(Mesh mesh, HashSet<Mesh> baked)
    {
        if (mesh == null || !baked.Add(mesh)) return;

        Bounds bounds = mesh.bounds;
        Vector3 centre = bounds.center;
        Vector3 extents = bounds.extents;
        float deadzone = DEADZONE * Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));

        Vector3[] vertices = mesh.vertices;
        List<Vector3> directions = new List<Vector3>(vertices.Length);

        // Split copies of a corner share its position, so they get the same direction and the hull
        // stays closed there
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 offset = vertices[i] - centre;

            directions.Add(new Vector3(
                SignOutside(offset.x, deadzone),
                SignOutside(offset.y, deadzone),
                SignOutside(offset.z, deadzone)));
        }

        mesh.SetUVs(OUTLINE_UV_CHANNEL, directions);
    }


    private static float SignOutside(float value, float deadzone)
    {
        if (value > deadzone) return 1.0f;
        if (value < -deadzone) return -1.0f;
        return 0.0f;
    }
}
