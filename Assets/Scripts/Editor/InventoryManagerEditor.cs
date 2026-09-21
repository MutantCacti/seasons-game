using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


// MARK: ! AI disclaimer
// All code in this file was written by DeepSeek-Flash


[CustomEditor(typeof(InventoryManager))]
public class InventoryManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        InventoryManager manager = (InventoryManager)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Gather Slots From Children"))
        {
            GatherSlots(manager);
        }

        if (GUILayout.Button("Validate"))
        {
            Validate(manager);
        }
    }


    // MARK: actions


    private static void GatherSlots(InventoryManager manager)
    {
        // include inactive: an empty slot hides itself
        ItemInventorySlot[] found = manager.GetComponentsInChildren<ItemInventorySlot>(true);

        Undo.RecordObject(manager, "Gather Inventory Slots");

        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty list = serialized.FindProperty("itemInventorySlots");

        list.arraySize = found.Length;
        for (int i = 0; i < found.Length; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        serialized.ApplyModifiedProperties();

        Debug.Log($"InventoryManager: gathered {found.Length} slots from children, in hierarchy order", manager);
    }


    private static void Validate(InventoryManager manager)
    {
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty list = serialized.FindProperty("itemInventorySlots");

        GameItemRegistry registry =
            serialized.FindProperty("registry").objectReferenceValue as GameItemRegistry;

        HashSet<ItemInventorySlot> seen = new HashSet<ItemInventorySlot>();
        int problems = 0;
        int usable = 0;

        for (int i = 0; i < list.arraySize; i++)
        {
            ItemInventorySlot slot = list.GetArrayElementAtIndex(i).objectReferenceValue as ItemInventorySlot;

            if (slot == null)
            {
                Debug.LogError($"InventoryManager: slot entry {i} is empty", manager);
                problems++;
                continue;
            }

            if (!seen.Add(slot))
            {
                Debug.LogError($"InventoryManager: slot entry {i} repeats '{slot.name}'", manager);
                problems++;
                continue;
            }

            usable++;
        }

        if (usable == 0)
        {
            Debug.LogError("InventoryManager: there are no slots, so nothing can ever be held", manager);
            problems++;
        }

        if (registry == null)
        {
            Debug.LogError("InventoryManager: no GameItemRegistry assigned, so saved items can never be resolved", manager);
            problems++;
        }
        else if (registry.Count > usable)
        {
            // Not an error: the player is not expected to hold every item at once. It is the point
            // at which a full inventory starts logging the no-free-slot error, so it is worth knowing.
            Debug.LogWarning($"InventoryManager: {usable} slots but {registry.Count} items in '{registry.name}', so a full inventory will run out of slots", manager);
        }

        if (problems == 0)
        {
            Debug.Log($"InventoryManager: {usable} slots, all distinct and assigned", manager);
        }
        else
        {
            Debug.LogError($"InventoryManager: {problems} problem(s) found", manager);
        }
    }
}
