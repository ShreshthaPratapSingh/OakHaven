#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only setup script. Run via the menu: Tools > OakHaven > Setup Equipment.
/// Wires up the held-item (equipment) system on the existing NetworkPlayer prefab:
///
/// 1. Finds the NetworkPlayer prefab anywhere in the project (AssetDatabase
///    search — no hardcoded path, so Prefab folder reorganizations can't break it).
/// 2. Opens it with PrefabUtility.LoadPrefabContents (never hand-edit prefab YAML).
/// 3. Finds the right-hand bone on the ACTIVE character model, creates a
///    "HandSocket" empty child under it.
/// 4. Adds the PlayerEquipment component (if missing) and wires handSocket
///    via SerializedObject (same pattern as NetworkingSetup).
/// 5. Assigns the stone_axe prefab to ItemDef_StoneAxe.equippedPrefab
///    (looked up by itemId, never by filename).
///
/// Safe to run multiple times — everything is idempotent. Re-run after
/// Tools > OakHaven > Rebuild Player Prefab.
/// </summary>
public static class EquipmentSetup
{
    private const string SocketName = "HandSocket";
    private const string AxeItemId = "stone_axe";

    [MenuItem("Tools/OakHaven/Setup Equipment")]
    public static void SetupEquipment()
    {
        GameObject playerPrefab = FindPlayerPrefab();
        if (playerPrefab == null) return;

        if (!SetupPlayerPrefab(playerPrefab)) return;
        AssignAxePrefabToItemDefinition();
        ValidateItemDatabaseLoader();

        Debug.Log("[EquipmentSetup] ✅ Equipment setup complete! Next steps:\n" +
                  "1. Open the NetworkPlayer prefab and tune the HandSocket rotation (under the\n" +
                  "   right hand) until the axe sits correctly in the fist — usually a ±90° flip.\n" +
                  "2. Save the scene (Ctrl+S).\n" +
                  "3. Play → craft the stone axe at the workbench → press Q to equip/unequip.");
    }

    // ─────────────────── Player Prefab ───────────────────

    /// <summary>Finds the NetworkPlayer prefab via AssetDatabase search (no hardcoded path).</summary>
    private static GameObject FindPlayerPrefab()
    {
        string[] guids = AssetDatabase.FindAssets("NetworkPlayer t:Prefab");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[EquipmentSetup] No NetworkPlayer prefab found in the project! " +
                             "Run Tools > OakHaven > Setup Networking first.");
            return null;
        }

        if (guids.Length > 1)
        {
            Debug.LogWarning("[EquipmentSetup] Multiple NetworkPlayer prefabs found — using the first:\n" +
                             AssetDatabase.GUIDToAssetPath(guids[0]) +
                             "\nIf that's the wrong one, delete/rename the others.");
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Debug.Log("[EquipmentSetup] Found NetworkPlayer prefab at: " + path);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// Creates the hand socket under the right-hand bone and adds/wires
    /// PlayerEquipment on the prefab. Returns false if the hand bone could not
    /// be found (aborts WITHOUT saving so nothing is half-wired).
    /// </summary>
    private static bool SetupPlayerPrefab(GameObject playerPrefab)
    {
        string path = AssetDatabase.GetAssetPath(playerPrefab);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform hand = FindHandBone(contents.transform);
            if (hand == null)
            {
                LogHandBoneCandidates(contents.transform);
                return false;
            }
            Debug.Log("[EquipmentSetup] Using hand bone: " + GetHierarchyPath(hand));

            // ── HandSocket (idempotent) ──
            Transform socket = hand.Find(SocketName);
            if (socket == null)
            {
                GameObject socketGo = new GameObject(SocketName);
                socketGo.transform.SetParent(hand, false);
                socketGo.transform.localPosition = Vector3.zero;
                socketGo.transform.localRotation = Quaternion.identity;
                socketGo.transform.localScale = Vector3.one;
                socketGo.layer = hand.gameObject.layer;   // match the model's layer
                socket = socketGo.transform;
                Debug.Log("[EquipmentSetup] Created " + SocketName + " under the right hand.");
            }
            else
            {
                Debug.Log("[EquipmentSetup] " + SocketName + " already exists — reusing it.");
            }

            // ── PlayerEquipment component (idempotent) ──
            PlayerEquipment equipment = contents.GetComponent<PlayerEquipment>();
            if (equipment == null)
            {
                equipment = contents.AddComponent<PlayerEquipment>();
                Debug.Log("[EquipmentSetup] Added PlayerEquipment to the prefab root.");
            }
            else
            {
                Debug.Log("[EquipmentSetup] PlayerEquipment already present — reusing it.");
            }

            // ── Wire handSocket via SerializedObject (NetworkingSetup pattern) ──
            SerializedObject so = new SerializedObject(equipment);
            SerializedProperty socketProp = so.FindProperty("handSocket");
            if (socketProp == null)
            {
                Debug.LogWarning("[EquipmentSetup] 'handSocket' property not found on PlayerEquipment " +
                                 "(was the script renamed?). Socket created but NOT wired!");
                return false;
            }

            socketProp.objectReferenceValue = socket;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[EquipmentSetup] Wired handSocket reference.");

            // SaveAsPrefabAsset overwrites in place; the socket under the nested
            // character model is stored as a proper added-object override.
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log("[EquipmentSetup] Saved player prefab: " + path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// Finds the right-hand bone. Matches common naming (RightHand,
    /// mixamorig:RightHand, Right_Hand, hand_r) — prefers EXACT matches on
    /// ACTIVE objects (the visible model; e.g. the disabled Y Bot source model
    /// has a RightHand too and must not win).
    /// </summary>
    private static Transform FindHandBone(Transform root)
    {
        Transform exactActive = null;
        Transform exactAny = null;
        Transform partialActive = null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string clean = CleanBoneName(t.name);
            bool isRightHand = clean == "righthand";
            bool containsRightHand = clean.Contains("righthand");
            if (!isRightHand && !containsRightHand) continue;

            bool active = t.gameObject.activeInHierarchy;
            if (isRightHand)
            {
                if (active && exactActive == null) exactActive = t;
                if (exactAny == null) exactAny = t;
            }
            else if (containsRightHand)
            {
                // Shortest name wins for partials (the hand itself, not RightHandIndex1)
                if (active && (partialActive == null || t.name.Length < partialActive.name.Length))
                    partialActive = t;
            }
        }

        if (exactActive != null) return exactActive;
        if (exactAny != null) return exactAny;
        return partialActive;
    }

    /// <summary>Normalizes a bone name for matching (strips mixamo prefixes, underscores, case).</summary>
    private static string CleanBoneName(string name)
    {
        return name.Replace("mixamorig:", "").Replace("_", "").Replace(" ", "").Trim().ToLowerInvariant();
    }

    /// <summary>Diagnostic dump when no hand bone is found — never fail silently.</summary>
    private static void LogHandBoneCandidates(Transform root)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.ToLowerInvariant().Contains("hand"))
                sb.AppendLine("  - " + t.name + (t.gameObject.activeInHierarchy ? "" : "  (inactive)"));
        }

        Debug.LogWarning("[EquipmentSetup] No right-hand bone found in the player prefab — nothing was saved.\n" +
                         "Bones containing 'hand':\n" +
                         (sb.Length > 0 ? sb.ToString() : "  (none — is the character model missing from the prefab?)"));
    }

    /// <summary>Bone path relative to the prefab root, for readable logs.</summary>
    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ─────────────────── Item Definition ───────────────────

    /// <summary>
    /// Assigns the stone_axe prefab to the ItemDefinition's equippedPrefab field.
    /// The ItemDefinition is looked up by its itemId (the real identity),
    /// never by asset filename.
    /// </summary>
    private static void AssignAxePrefabToItemDefinition()
    {
        ItemDefinition axeDef = null;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
        {
            ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def != null && def.itemId == AxeItemId)
            {
                axeDef = def;
                break;
            }
        }

        if (axeDef == null)
        {
            Debug.LogWarning("[EquipmentSetup] No ItemDefinition with itemId '" + AxeItemId +
                             "' found — assign the axe prefab to its 'equippedPrefab' field manually.");
            return;
        }

        GameObject axePrefab = null;
        foreach (string guid in AssetDatabase.FindAssets(AxeItemId + " t:Prefab"))
        {
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null)
            {
                axePrefab = p;
                break;
            }
        }

        if (axePrefab == null)
        {
            Debug.LogWarning("[EquipmentSetup] No '" + AxeItemId + "' prefab found (expected under Assets/Prefabs/Items). " +
                             "Create it from the FBX and assign it to the ItemDefinition manually.");
            return;
        }

        SerializedObject so = new SerializedObject(axeDef);
        SerializedProperty prop = so.FindProperty("equippedPrefab");
        if (prop == null)
        {
            Debug.LogWarning("[EquipmentSetup] 'equippedPrefab' property not found on ItemDefinition " +
                             "(was the field renamed?). Assign manually.");
            return;
        }

        if (prop.objectReferenceValue == axePrefab)
        {
            Debug.Log("[EquipmentSetup] ItemDef '" + axeDef.name + "' already has the axe prefab assigned.");
            return;
        }

        prop.objectReferenceValue = axePrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(axeDef);
        AssetDatabase.SaveAssets();
        Debug.Log("[EquipmentSetup] Assigned '" + axePrefab.name + "' to " + axeDef.name + ".equippedPrefab.");
    }

    // ─────────────────── Scene Validation ───────────────────

    /// <summary>Reminds about ItemDatabaseLoader (required in any inventory/crafting scene).</summary>
    private static void ValidateItemDatabaseLoader()
    {
        if (Object.FindFirstObjectByType<ItemDatabaseLoader>() == null)
        {
            Debug.LogWarning("[EquipmentSetup] No ItemDatabaseLoader found in the open scene! " +
                             "Equip validation and visuals will fail at runtime without it.");
        }
        else
        {
            Debug.Log("[EquipmentSetup] ItemDatabaseLoader present in scene. ✔");
        }
    }
}
#endif
