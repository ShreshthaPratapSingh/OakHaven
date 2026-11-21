#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// Editor-only tool that creates the networked WorldItemDrop prefab.
/// Run via Tools > OakHaven > Create World Item Drop Prefab.
///
/// The generated prefab contains:
/// - NetworkObject + NetworkTransform for server-authoritative sync
/// - Rigidbody + BoxCollider physics body and SphereCollider trigger for interaction
/// - WorldItemDrop component
/// - A cube visual and two world-space TextMesh labels
///
/// After creation the prefab is automatically registered in the default
/// NetworkPrefabsList so it can be spawned at runtime.
/// </summary>
public static class DropPrefabSetup
{
    private const string PrefabPath = "Assets/Prefabs/Environment/WorldItemDrop.prefab";

    [MenuItem("Tools/OakHaven/Create World Item Drop Prefab")]
    public static void CreateWorldItemDropPrefab()
    {
        // Ensure the directory exists
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Environment"))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Environment");
        }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            Debug.LogWarning($"[DropPrefabSetup] Replacing existing WorldItemDrop prefab at {PrefabPath}.");
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        GameObject root = BuildDropPrefab();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            NetworkingSetup.RegisterPrefabInNetworkPrefabsList(prefab);
            AssignDropPrefabToPlayer(prefab);
            Selection.activeObject = prefab;
            Debug.Log($"[DropPrefabSetup] Created and registered WorldItemDrop prefab at {PrefabPath}.");
        }
        else
        {
            Debug.LogError("[DropPrefabSetup] Failed to save WorldItemDrop prefab.");
        }
    }

    private static void AssignDropPrefabToPlayer(GameObject dropPrefab)
    {
        GameObject playerPrefab = NetworkingSetup.FindExistingPlayerPrefab();
        if (playerPrefab == null)
        {
            Debug.LogWarning("[DropPrefabSetup] No NetworkPlayer prefab found — cannot auto-assign drop prefab. " +
                             "Assign the WorldItemDrop prefab manually to the Inventory and ResourceInventory components.");
            return;
        }

        bool changed = false;

        Inventory inv = playerPrefab.GetComponent<Inventory>();
        if (inv != null)
        {
            SerializedObject so = new SerializedObject(inv);
            so.FindProperty("defaultDropPrefab").objectReferenceValue = dropPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        ResourceInventory res = playerPrefab.GetComponent<ResourceInventory>();
        if (res != null)
        {
            SerializedObject so = new SerializedObject(res);
            so.FindProperty("defaultDropPrefab").objectReferenceValue = dropPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(playerPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[DropPrefabSetup] Assigned WorldItemDrop prefab to NetworkPlayer Inventory and ResourceInventory.");
        }
    }

    private static GameObject BuildDropPrefab()
    {
        // ── Root ──
        GameObject root = new GameObject("WorldItemDrop");

        NetworkObject netObj = root.AddComponent<NetworkObject>();
        NetworkTransform netTransform = root.AddComponent<NetworkTransform>();
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.5f;
        trigger.center = new Vector3(0f, 0.3f, 0f);

        // Small physics collider so the drop lands on the ground instead of falling through.
        BoxCollider physicsCol = root.AddComponent<BoxCollider>();
        physicsCol.size = Vector3.one * 0.3f;
        physicsCol.center = new Vector3(0f, 0.15f, 0f);

        WorldItemDrop drop = root.AddComponent<WorldItemDrop>();

        // ── Visual cube ──
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        visual.transform.localScale = Vector3.one * 0.3f;

        Collider visualCol = visual.GetComponent<Collider>();
        if (visualCol != null) Object.DestroyImmediate(visualCol);

        Renderer visualRenderer = visual.GetComponent<Renderer>();

        // ── Item name label ──
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(root.transform, false);
        labelGO.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        labelGO.transform.localScale = Vector3.one * 0.04f;
        TextMesh labelText = labelGO.AddComponent<TextMesh>();
        labelText.text = "Item";
        labelText.fontSize = 24;
        labelText.anchor = TextAnchor.MiddleCenter;
        labelText.alignment = TextAlignment.Center;
        labelText.color = Color.white;

        // ── Quantity label ──
        GameObject quantityGO = new GameObject("Quantity");
        quantityGO.transform.SetParent(root.transform, false);
        quantityGO.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        quantityGO.transform.localScale = Vector3.one * 0.04f;
        TextMesh quantityText = quantityGO.AddComponent<TextMesh>();
        quantityText.text = "x1";
        quantityText.fontSize = 24;
        quantityText.anchor = TextAnchor.MiddleCenter;
        quantityText.alignment = TextAlignment.Center;
        quantityText.color = Color.white;

        // ── Wire WorldItemDrop references via SerializedObject ──
        SerializedObject soDrop = new SerializedObject(drop);
        soDrop.FindProperty("visualRenderer").objectReferenceValue = visualRenderer;
        soDrop.FindProperty("labelTextMesh").objectReferenceValue = labelText;
        soDrop.FindProperty("quantityTextMesh").objectReferenceValue = quantityText;
        soDrop.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }
}
#endif
