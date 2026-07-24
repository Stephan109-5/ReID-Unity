using UnityEngine;
using UnityEditor;

public class BatchMeshColliderAdder : EditorWindow
{
    [MenuItem("Tools/Add Mesh Colliders to Children")]
    public static void AddCollidersToSelected()
    {
        // Get all selected GameObjects in the Hierarchy or Project view
        GameObject[] selectedObjects = Selection.gameObjects;

        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select at least one Prefab or GameObject.", "OK");
            return;
        }

        int addedCount = 0;

        foreach (GameObject parent in selectedObjects)
        {
            // Find all MeshFilters in children (including the parent itself)
            MeshFilter[] meshFilters = parent.GetComponentsInChildren<MeshFilter>(true);

            foreach (MeshFilter mf in meshFilters)
            {
                // Skip if there's no actual mesh assigned
                if (mf.sharedMesh == null) continue;

                // Check if it already has a MeshCollider
                MeshCollider mc = mf.GetComponent<MeshCollider>();

                if (mc == null)
                {
                    // Record the action so you can press Ctrl+Z to undo it!
                    Undo.AddComponent<MeshCollider>(mf.gameObject);
                    mc = mf.gameObject.GetComponent<MeshCollider>();
                    addedCount++;
                }

                // Automatically match the collider's mesh to the MeshFilter's mesh
                mc.sharedMesh = mf.sharedMesh;
            }

            // If it's a Prefab, mark it as dirty so Unity saves the changes
            if (PrefabUtility.IsPartOfPrefabAsset(parent))
            {
                EditorUtility.SetDirty(parent);
            }
        }

        EditorUtility.DisplayDialog("Success!", $"Added/Updated {addedCount} Mesh Colliders.", "OK");
    }
}