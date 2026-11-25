using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor tool that scans the active scene for ResourceNode prefab instances and
/// reports configuration problems that cause inconsistent gathering.
/// </summary>
public static class ResourceNodeValidator
{
    [MenuItem("Tools/OakHaven/Validate Resource Nodes")]
    private static void Validate()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[ResourceNodeValidator] No active scene.");
            return;
        }

        var allItemIds = LoadAllItemIds();
        var nodes = Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);

        if (nodes.Length == 0)
        {
            Debug.Log("[ResourceNodeValidator] No ResourceNode components found in scene.");
            return;
        }

        int errorCount = 0;
        int warningCount = 0;

        // Group by resourceType to detect inconsistent tuning.
        var byType = new Dictionary<string, List<ResourceNode>>();
        foreach (ResourceNode node in nodes)
        {
            string type = node.ResourceType;
            if (!byType.TryGetValue(type, out var list))
            {
                list = new List<ResourceNode>();
                byType[type] = list;
            }
            list.Add(node);
        }

        foreach (ResourceNode node in nodes)
        {
            string path = GetScenePath(node.transform);

            if (string.IsNullOrWhiteSpace(node.ResourceType))
            {
                Debug.LogError($"[ResourceNodeValidator] {path}: resourceType is empty!", node);
                errorCount++;
                continue;
            }

            if (!allItemIds.Contains(node.ResourceType))
            {
                Debug.LogError($"[ResourceNodeValidator] {path}: resourceType '{node.ResourceType}' " +
                               $"does not match any ItemDefinition.itemId.", node);
                errorCount++;
            }

            Collider col = node.GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogError($"[ResourceNodeValidator] {path}: ResourceNode is missing a Collider on its root. " +
                               $"Add a BoxCollider (trigger) for consistent gathering.", node);
                errorCount++;
            }
            else
            {
                if (!col.isTrigger)
                {
                    Debug.LogWarning($"[ResourceNodeValidator] {path}: root Collider is not a trigger. " +
                                     $"This can block player movement and line-of-sight checks.", node);
                    warningCount++;
                }

                if (col is BoxCollider box)
                {
                    Vector3 worldSize = Vector3.Scale(box.size, node.transform.lossyScale);
                    float minDim = Mathf.Min(worldSize.x, worldSize.y, worldSize.z);
                    float maxDim = Mathf.Max(worldSize.x, worldSize.y, worldSize.z);
                    if (minDim <= 0.01f || maxDim <= 0.01f)
                    {
                        Debug.LogError($"[ResourceNodeValidator] {path}: root BoxCollider has a zero or near-zero size. " +
                                       $"Size={box.size}, WorldSize={worldSize}", node);
                        errorCount++;
                    }
                }
            }

            // Warn about child colliders that might confuse raycasts.
            Collider[] childColliders = node.GetComponentsInChildren<Collider>().Where(c => c.gameObject != node.gameObject).ToArray();
            if (childColliders.Length > 0)
            {
                Debug.LogWarning($"[ResourceNodeValidator] {path}: ResourceNode has {childColliders.Length} child Collider(s). " +
                                 $"Only the root collider is used for server validation; child colliders may interfere with client aiming.", node);
                warningCount++;
            }

            if (node.MaxHealth <= 0)
            {
                Debug.LogError($"[ResourceNodeValidator] {path}: maxHealth must be > 0.", node);
                errorCount++;
            }
        }

        // Report tuning inconsistencies within each resource type.
        foreach (var kvp in byType)
        {
            var list = kvp.Value;
            if (list.Count <= 1) continue;

            var healthValues = list.Select(n => n.MaxHealth).Distinct().ToList();
            var cooldownValues = list.Select(n => GetHitCooldown(n)).Distinct().ToList();

            if (healthValues.Count > 1)
            {
                Debug.LogWarning($"[ResourceNodeValidator] Resource type '{kvp.Key}' has inconsistent maxHealth: " +
                                 $"{string.Join(", ", healthValues)}. Same resource type should have the same health.", list[0]);
                warningCount++;
            }

            if (cooldownValues.Count > 1)
            {
                Debug.LogWarning($"[ResourceNodeValidator] Resource type '{kvp.Key}' has inconsistent hitCooldown: " +
                                 $"{string.Join(", ", cooldownValues)}. Same resource type should have the same cooldown.", list[0]);
                warningCount++;
            }
        }

        Debug.Log($"[ResourceNodeValidator] Scan complete: {nodes.Length} node(s), {errorCount} error(s), {warningCount} warning(s).");
    }

    private static HashSet<string> LoadAllItemIds()
    {
        var ids = new HashSet<string>();
        string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (def != null && !string.IsNullOrWhiteSpace(def.itemId))
                ids.Add(def.itemId);
        }
        return ids;
    }

    private static string GetScenePath(Transform t)
    {
        if (t.parent == null) return t.name;
        return GetScenePath(t.parent) + "/" + t.name;
    }

    private static float GetHitCooldown(ResourceNode node)
    {
        // Access private field via reflection for validation purposes.
        var field = typeof(ResourceNode).GetField("hitCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) return (float)field.GetValue(node);
        return -1f;
    }
}
