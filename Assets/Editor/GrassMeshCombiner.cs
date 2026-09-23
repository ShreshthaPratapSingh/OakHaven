using UnityEngine;
using UnityEditor;

public class GrassMeshCombiner
{
    [MenuItem("Tools/Combine Selected Grass Mesh")]
    static void CombineSelected()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("Select the parent GameObject (e.g. 'Grass') first.");
            return;
        }

        MeshFilter[] meshFilters = selected.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length == 0)
        {
            Debug.LogError("No MeshFilters found in children.");
            return;
        }

        CombineInstance[] combine = new CombineInstance[meshFilters.Length];
        for (int i = 0; i < meshFilters.Length; i++)
        {
            combine[i].mesh = meshFilters[i].sharedMesh;
            // This bakes each child's LOCAL transform (relative to the
            // root) into the combined mesh's vertex data — this is the
            // step that preserves rotation.
            combine[i].transform = meshFilters[i].transform.localToWorldMatrix *
                                    selected.transform.worldToLocalMatrix;
        }

        Mesh combinedMesh = new Mesh();
        combinedMesh.CombineMeshes(combine, true, true);

        string path = "Assets/CombinedGrassMesh.asset";
        AssetDatabase.CreateAsset(combinedMesh, path);
        AssetDatabase.SaveAssets();

        Debug.Log("Combined mesh saved to " + path);
    }
}