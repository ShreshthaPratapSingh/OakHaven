#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-click setup for the character selection flow.
/// Run via Tools > OakHaven > Setup Character Selection.
///
/// Idempotent — safe to run multiple times.
/// </summary>
public static class CharacterSelectionSetup
{
    private const string PlayerPrefabSearch = "NetworkPlayer t:Prefab";
    private const string ButtonPrefabPath = "Assets/Prefabs/UI/CharacterButtonPrefab.prefab";

    [MenuItem("Tools/OakHaven/Setup Character Selection")]
    public static void Setup()
    {
        SetupPlayerPrefab();
        SetupSceneUI();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[CharacterSelectionSetup] Character selection setup complete.");
    }

    // ─────────────────── Player Prefab ───────────────────

    private static void SetupPlayerPrefab()
    {
        string[] guids = AssetDatabase.FindAssets(PlayerPrefabSearch);
        if (guids.Length == 0)
        {
            Debug.LogError("[CharacterSelectionSetup] No NetworkPlayer prefab found.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            PlayerCharacterAppearance appearance = contents.GetComponent<PlayerCharacterAppearance>();
            if (appearance == null)
            {
                appearance = contents.AddComponent<PlayerCharacterAppearance>();
                Debug.Log("[CharacterSelectionSetup] Added PlayerCharacterAppearance to NetworkPlayer.");
            }

            CharacterCatalog catalog = FindCharacterCatalog();
            Transform defaultVisual = FindDefaultVisualRoot(contents.transform);
            Animator defaultAnimator = defaultVisual != null ? defaultVisual.GetComponentInChildren<Animator>() : null;

            SerializedObject so = new SerializedObject(appearance);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.FindProperty("defaultVisualRoot").objectReferenceValue = defaultVisual;
            so.FindProperty("defaultAnimator").objectReferenceValue = defaultAnimator;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            Debug.Log("[CharacterSelectionSetup] Wired NetworkPlayer prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ─────────────────── Scene UI ───────────────────

    private static void SetupSceneUI()
    {
        Canvas canvas = FindNetworkUICanvas();
        if (canvas == null)
        {
            Debug.LogError("[CharacterSelectionSetup] No Canvas named 'NetworkUI' found in scene.");
            return;
        }

        Transform panel = canvas.transform.Find("Panel");
        if (panel == null)
        {
            Debug.LogError("[CharacterSelectionSetup] No 'Panel' child found under NetworkUI.");
            return;
        }

        // Create / reuse selection panel.
        Transform selectionPanel = canvas.transform.Find("CharacterSelectionPanel");
        if (selectionPanel == null)
        {
            selectionPanel = CreateSelectionPanel(canvas.transform);
            Debug.Log("[CharacterSelectionSetup] Created CharacterSelectionPanel.");
        }

        Transform cardsContainer = selectionPanel.Find("CardsContainer");
        if (cardsContainer == null)
        {
            cardsContainer = CreateCardsContainer(selectionPanel);
            Debug.Log("[CharacterSelectionSetup] Created CardsContainer.");
        }

        // Create / reuse button prefab.
        GameObject buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
        if (buttonPrefab == null)
        {
            buttonPrefab = CreateButtonPrefab();
        }

        // Add / reuse CharacterSelectionUI on the Canvas.
        CharacterSelectionUI ui = canvas.GetComponent<CharacterSelectionUI>();
        if (ui == null)
        {
            ui = canvas.gameObject.AddComponent<CharacterSelectionUI>();
            Debug.Log("[CharacterSelectionSetup] Added CharacterSelectionUI to Canvas.");
        }

        TMP_InputField joinInput = FindJoinCodeInput(panel);
        TMP_Text statusText = FindSelectionStatusText(selectionPanel);
        Button backButton = FindBackButton(selectionPanel);

        SerializedObject so = new SerializedObject(ui);
        so.FindProperty("mainMenuPanel").objectReferenceValue = panel.gameObject;
        so.FindProperty("characterSelectionPanel").objectReferenceValue = selectionPanel.gameObject;
        so.FindProperty("joinCodeInputField").objectReferenceValue = joinInput;
        so.FindProperty("characterButtonContainer").objectReferenceValue = cardsContainer;
        so.FindProperty("characterButtonPrefab").objectReferenceValue = buttonPrefab;
        so.FindProperty("selectionStatusText").objectReferenceValue = statusText;
        so.FindProperty("backButton").objectReferenceValue = backButton;
        so.FindProperty("catalog").objectReferenceValue = FindCharacterCatalog();
        so.FindProperty("selectionManager").objectReferenceValue = FindSelectionManager();
        so.FindProperty("relayManager").objectReferenceValue = FindRelayManager();
        so.ApplyModifiedPropertiesWithoutUndo();

        WireMainMenuButtons(panel, ui);
        WireSelectionManager(ui);

        Debug.Log("[CharacterSelectionSetup] Wired scene UI.");
    }

    // ─────────────────── Helpers ───────────────────

    private static Transform FindDefaultVisualRoot(Transform playerRoot)
    {
        // Prefer the transform referenced by PlayerController as playerModel.
        PlayerController controller = playerRoot.GetComponent<PlayerController>();
        if (controller != null)
        {
            SerializedObject so = new SerializedObject(controller);
            SerializedProperty prop = so.FindProperty("playerModel");
            Transform t = prop != null ? prop.objectReferenceValue as Transform : null;
            if (t != null) return t;
        }

        // Fallback: find the first child with an Animator (the Brute model).
        Animator anim = playerRoot.GetComponentInChildren<Animator>();
        if (anim != null) return anim.transform;

        return playerRoot;
    }

    private static Canvas FindNetworkUICanvas()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (Canvas c in canvases)
        {
            if (c.gameObject.name == "NetworkUI")
                return c;
        }
        return null;
    }

    private static CharacterCatalog FindCharacterCatalog()
    {
        string[] guids = AssetDatabase.FindAssets("t:CharacterCatalog");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[CharacterSelectionSetup] No CharacterCatalog asset found.");
            return null;
        }
        return AssetDatabase.LoadAssetAtPath<CharacterCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    private static CharacterSelectionManager FindSelectionManager()
    {
        GameObject go = GameObject.Find("CharacterSelectionManager");
        if (go != null) return go.GetComponent<CharacterSelectionManager>();
        return Object.FindFirstObjectByType<CharacterSelectionManager>();
    }

    private static RelayManager FindRelayManager()
    {
        return Object.FindFirstObjectByType<RelayManager>();
    }

    private static TMP_InputField FindJoinCodeInput(Transform panel)
    {
        Transform t = panel.Find("JoinCodeInput");
        if (t != null) return t.GetComponent<TMP_InputField>();
        return panel.GetComponentInChildren<TMP_InputField>();
    }

    private static TMP_Text FindSelectionStatusText(Transform selectionPanel)
    {
        Transform t = selectionPanel.Find("StatusText");
        if (t != null)
        {
            // Fix size/position if it was created by an older version of the tool.
            RectTransform rt = t.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1);
                rt.anchoredPosition = new Vector2(0, -40);
                rt.sizeDelta = new Vector2(1600, 60);
            }
            return t.GetComponent<TMP_Text>();
        }

        GameObject go = new GameObject("StatusText");
        go.transform.SetParent(selectionPanel, false);
        TMP_Text text = go.AddComponent<TextMeshProUGUI>();
        text.text = "Choose your character";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 24;
        text.color = Color.white;
        RectTransform newRt = go.GetComponent<RectTransform>();
        newRt.anchorMin = new Vector2(0.5f, 1f);
        newRt.anchorMax = new Vector2(0.5f, 1f);
        newRt.pivot = new Vector2(0.5f, 1);
        newRt.anchoredPosition = new Vector2(0, -40);
        newRt.sizeDelta = new Vector2(1600, 60);
        return text;
    }

    private static Button FindBackButton(Transform selectionPanel)
    {
        Transform t = selectionPanel.Find("BackButton");
        if (t != null)
        {
            // Fix size/position if it was created by an older version of the tool.
            RectTransform rt = t.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0, 40);
                rt.sizeDelta = new Vector2(120, 40);
            }
            return t.GetComponent<Button>();
        }

        GameObject go = CreateButton(selectionPanel, "BackButton", "Back", new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(120, 40));
        return go.GetComponent<Button>();
    }

    private static Transform CreateSelectionPanel(Transform canvas)
    {
        GameObject panel = new GameObject("CharacterSelectionPanel");
        panel.transform.SetParent(canvas, false);

        RectTransform rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.85f);

        panel.SetActive(false);
        return panel.transform;
    }

    private static Transform CreateCardsContainer(Transform selectionPanel)
    {
        GameObject container = new GameObject("CardsContainer");
        container.transform.SetParent(selectionPanel, false);

        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(1600, 400);

        GridLayoutGroup grid = container.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(250, 350);
        grid.spacing = new Vector2(16, 16);
        grid.padding = new RectOffset(0, 0, 0, 0);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;

        return container.transform;
    }

    private static GameObject CreateButtonPrefab()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        GameObject root = new GameObject("CharacterButtonPrefab");
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(250, 350);

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.25f, 0.3f, 1f);

        Button btn = root.AddComponent<Button>();

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(root.transform, false);
        RectTransform iconRt = iconObj.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0, 0.13f);
        iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(16, 16);
        iconRt.offsetMax = new Vector2(-16, -16);
        Image icon = iconObj.AddComponent<Image>();
        icon.color = Color.white;
        icon.preserveAspect = false;

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(root.transform, false);
        RectTransform labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = new Vector2(1, 0.13f);
        labelRt.offsetMin = new Vector2(4, 4);
        labelRt.offsetMax = new Vector2(-4, -4);
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = "Character";
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 18;
        label.color = Color.white;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ButtonPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateButton(Transform parent, string name, string labelText, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = pivot;
        rt.anchorMax = pivot;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.9f, 1f);

        Button btn = go.AddComponent<Button>();

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(go.transform, false);
        RectTransform labelRt = labelObj.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
        TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = labelText;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 20;
        label.color = Color.white;

        return go;
    }

    private static void WireMainMenuButtons(Transform panel, CharacterSelectionUI ui)
    {
        Button hostButton = FindButton(panel, "HostButton");
        if (hostButton != null)
        {
            AddPersistentListener(hostButton, ui, "OnHostClicked");
            Debug.Log("[CharacterSelectionSetup] Wired HostButton to CharacterSelectionUI.OnHostClicked.");
        }

        Button joinButton = FindButton(panel, "JoinButton");
        if (joinButton != null)
        {
            AddPersistentListener(joinButton, ui, "OnJoinClicked");
            Debug.Log("[CharacterSelectionSetup] Wired JoinButton to CharacterSelectionUI.OnJoinClicked.");
        }
    }

    private static void AddPersistentListener(Button button, Object target, string methodName)
    {
        SerializedObject so = new SerializedObject(button);
        SerializedProperty onClick = so.FindProperty("m_OnClick");
        SerializedProperty calls = onClick.FindPropertyRelative("m_PersistentCalls.m_Calls");

        // Replace any existing listeners with the single new one.
        calls.ClearArray();
        calls.arraySize = 1;

        SerializedProperty call = calls.GetArrayElementAtIndex(0);
        call.FindPropertyRelative("m_Target").objectReferenceValue = target;
        call.FindPropertyRelative("m_MethodName").stringValue = methodName;
        call.FindPropertyRelative("m_Mode").intValue = 1; // Void
        call.FindPropertyRelative("m_CallState").intValue = 2; // RuntimeOnly

        SerializedProperty args = call.FindPropertyRelative("m_Arguments");
        args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = null;
        args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = "UnityEngine.Object, UnityEngine";
        args.FindPropertyRelative("m_IntArgument").intValue = 0;
        args.FindPropertyRelative("m_FloatArgument").floatValue = 0;
        args.FindPropertyRelative("m_StringArgument").stringValue = "";
        args.FindPropertyRelative("m_BoolArgument").intValue = 0;

        so.ApplyModifiedProperties();
    }

    private static Button FindButton(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t.GetComponent<Button>();
        return null;
    }

    private static void WireSelectionManager(CharacterSelectionUI ui)
    {
        CharacterSelectionManager manager = FindSelectionManager();
        if (manager == null)
        {
            Debug.LogWarning("[CharacterSelectionSetup] CharacterSelectionManager not found in scene.");
            return;
        }

        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("selectionUI").objectReferenceValue = ui;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[CharacterSelectionSetup] Wired CharacterSelectionManager.selectionUI.");
    }
}
#endif
