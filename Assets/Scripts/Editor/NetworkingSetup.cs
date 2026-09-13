#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor-only setup script. Run via the menu: Tools > OakHaven > Setup Networking.
/// Creates the NetworkPlayer prefab, configures NetworkManager in the scene,
/// builds the relay UI, and wires everything together.
/// 
/// Safe to run multiple times — it checks for existing objects first.
/// Delete this script after setup if desired.
/// </summary>
public static class NetworkingSetup
{
    [MenuItem("Tools/OakHaven/Setup Networking")]
    public static void SetupNetworking()
    {
        CreateNetworkPlayerPrefab();
        SetupSceneObjects();
        Debug.Log("[NetworkingSetup] ✅ Networking setup complete! Remember to:\n" +
                  "1. Link your project to Unity Gaming Services (Project Settings > Services)\n" +
                  "2. Enable Relay in the Unity Dashboard\n" +
                  "3. Save the scene (Ctrl+S)");
    }

    // ─────────────────── NetworkPlayer Prefab ───────────────────

    private static void CreateNetworkPlayerPrefab()
    {
        string prefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

        // Check if already exists
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existingPrefab != null)
        {
            Debug.Log("[NetworkingSetup] NetworkPlayer prefab already exists at: " + prefabPath);
            RegisterPrefabInNetworkPrefabsList(existingPrefab);
            return;
        }

        // Ensure directory exists
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        // Create a capsule as the visual representation
        GameObject playerGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerGO.name = "NetworkPlayer";

        // Add networking components
        playerGO.AddComponent<NetworkObject>();
        playerGO.AddComponent<NetworkTransform>();

        // Save as prefab
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(playerGO, prefabPath);
        Object.DestroyImmediate(playerGO); // Clean up scene instance

        Debug.Log("[NetworkingSetup] Created NetworkPlayer prefab at: " + prefabPath);

        // Register in the default network prefabs list
        RegisterPrefabInNetworkPrefabsList(prefab);
    }

    private static void RegisterPrefabInNetworkPrefabsList(GameObject prefab)
    {
        // Find the DefaultNetworkPrefabs asset
        string[] guids = AssetDatabase.FindAssets("t:NetworkPrefabsList");
        if (guids.Length == 0)
        {
            Debug.LogWarning("[NetworkingSetup] No NetworkPrefabsList found. " +
                             "Add the prefab manually via NetworkManager > Network Prefabs.");
            return;
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            NetworkPrefabsList prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (prefabsList == null) continue;

            // Check if already registered
            bool alreadyRegistered = false;
            foreach (var np in prefabsList.PrefabList)
            {
                if (np.Prefab == prefab)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                prefabsList.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(prefabsList);
                AssetDatabase.SaveAssets();
                Debug.Log("[NetworkingSetup] Registered NetworkPlayer in: " + path);
            }
            else
            {
                Debug.Log("[NetworkingSetup] NetworkPlayer already registered in: " + path);
            }
        }
    }

    // ─────────────────── Scene Objects ───────────────────

    private static void SetupSceneObjects()
    {
        SetupNetworkManager();
        SetupUI();
    }

    private static void SetupNetworkManager()
    {
        // Check if NetworkManager already exists in scene
        NetworkManager existingNM = Object.FindFirstObjectByType<NetworkManager>();
        if (existingNM != null)
        {
            Debug.Log("[NetworkingSetup] NetworkManager already exists in scene.");
            EnsureTransportWired(existingNM);
            EnsureRelayManager(existingNM.gameObject);
            return;
        }

        // Create NetworkManager GameObject
        GameObject nmGO = new GameObject("NetworkManager");

        // Add components
        NetworkManager nm = nmGO.AddComponent<NetworkManager>();
        UnityTransport transport = nmGO.AddComponent<UnityTransport>();

        // Use SerializedObject to wire everything — NetworkConfig is null
        // right after AddComponent, but the serialized fields exist.
        SerializedObject serializedNM = new SerializedObject(nm);

        // Wire transport: NetworkConfig.NetworkTransport
        var networkConfigProp = serializedNM.FindProperty("NetworkConfig");
        var transportProp = networkConfigProp.FindPropertyRelative("NetworkTransport");
        transportProp.objectReferenceValue = transport;

        // Wire player prefab: NetworkConfig.PlayerPrefab
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetworkPlayer.prefab");
        if (playerPrefab != null)
        {
            var playerPrefabProp = networkConfigProp.FindPropertyRelative("PlayerPrefab");
            playerPrefabProp.objectReferenceValue = playerPrefab;
            Debug.Log("[NetworkingSetup] Set PlayerPrefab on NetworkManager.");
        }
        else
        {
            Debug.LogWarning("[NetworkingSetup] NetworkPlayer prefab not found. Set PlayerPrefab manually.");
        }

        serializedNM.ApplyModifiedProperties();

        // Add RelayManager
        EnsureRelayManager(nmGO);

        Debug.Log("[NetworkingSetup] Created NetworkManager GameObject with UnityTransport.");
    }

    private static void EnsureTransportWired(NetworkManager nm)
    {
        var serializedNM = new SerializedObject(nm);
        var networkConfigProp = serializedNM.FindProperty("NetworkConfig");
        var transportProp = networkConfigProp.FindPropertyRelative("NetworkTransport");

        if (transportProp.objectReferenceValue == null)
        {
            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = nm.gameObject.AddComponent<UnityTransport>();
                Debug.Log("[NetworkingSetup] Added missing UnityTransport component.");
            }
            transportProp.objectReferenceValue = transport;
            serializedNM.ApplyModifiedProperties();
            Debug.Log("[NetworkingSetup] Wired UnityTransport to NetworkManager.");
        }
    }

    private static void EnsureRelayManager(GameObject target)
    {
        if (target.GetComponent<RelayManager>() == null)
        {
            target.AddComponent<RelayManager>();
            Debug.Log("[NetworkingSetup] Added RelayManager component.");
        }
    }

    private static void SetupUI()
    {
        // Check if UI canvas already exists
        RelayManager relayManager = Object.FindFirstObjectByType<RelayManager>();
        Canvas existingCanvas = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.gameObject.name == "NetworkUI")
            {
                existingCanvas = c;
                break;
            }
        }

        if (existingCanvas != null)
        {
            Debug.Log("[NetworkingSetup] NetworkUI canvas already exists.");
            return;
        }

        // Create Canvas
        GameObject canvasGO = new GameObject("NetworkUI");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // Render on top
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Configure CanvasScaler for consistent UI across resolutions
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ── Panel background ──
        GameObject panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRect = panelGO.AddComponent<RectTransform>();
        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.7f);
        // Position: bottom-left corner
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(0, 0);
        panelRect.pivot = new Vector2(0, 0);
        panelRect.anchoredPosition = new Vector2(20, 20);
        panelRect.sizeDelta = new Vector2(380, 260);

        // ── Status Text ──
        GameObject statusGO = new GameObject("StatusText");
        statusGO.transform.SetParent(panelGO.transform, false);
        TextMeshProUGUI statusText = statusGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "Initializing...";
        statusText.fontSize = 18;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Center;
        RectTransform statusRect = statusGO.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 1);
        statusRect.anchorMax = new Vector2(1, 1);
        statusRect.pivot = new Vector2(0.5f, 1);
        statusRect.anchoredPosition = new Vector2(0, -10);
        statusRect.sizeDelta = new Vector2(-20, 40);

        // ── Host Button ──
        GameObject hostBtnGO = CreateButton(panelGO.transform, "HostButton", "Host Game",
            new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(340, 45));

        // ── Join Code Input Field ──
        GameObject inputGO = CreateInputField(panelGO.transform, "JoinCodeInput", "Enter Join Code...",
            new Vector2(0.5f, 1f), new Vector2(0, -115), new Vector2(340, 45));

        // ── Join Button ──
        GameObject joinBtnGO = CreateButton(panelGO.transform, "JoinButton", "Join Game",
            new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(340, 45));

        // ── Wire up RelayManager ──
        if (relayManager != null)
        {
            SerializedObject so = new SerializedObject(relayManager);
            so.FindProperty("hostButton").objectReferenceValue = hostBtnGO.GetComponent<Button>();
            so.FindProperty("joinButton").objectReferenceValue = joinBtnGO.GetComponent<Button>();
            so.FindProperty("joinCodeInputField").objectReferenceValue = inputGO.GetComponent<TMP_InputField>();
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.ApplyModifiedProperties();
            Debug.Log("[NetworkingSetup] Wired UI references to RelayManager.");
        }
        else
        {
            Debug.LogWarning("[NetworkingSetup] RelayManager not found in scene. Wire UI references manually.");
        }

        // Ensure EventSystem exists
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            Debug.Log("[NetworkingSetup] Created EventSystem.");
        }

        Debug.Log("[NetworkingSetup] Created NetworkUI canvas with Host/Join buttons.");
    }

    // ─────────────────── UI Helpers ───────────────────

    private static GameObject CreateButton(Transform parent, string name, string label,
        Vector2 pivot, Vector2 position, Vector2 size)
    {
        GameObject btnGO = new GameObject(name);
        btnGO.transform.SetParent(parent, false);

        RectTransform rect = btnGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, pivot.y);
        rect.anchorMax = new Vector2(0.5f, pivot.y);
        rect.pivot = new Vector2(0.5f, pivot.y);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image img = btnGO.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.9f, 1f); // Blue button

        Button btn = btnGO.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.7f, 1f, 1f);
        colors.pressedColor = new Color(0.15f, 0.45f, 0.7f, 1f);
        btn.colors = colors;

        // Button label
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(btnGO.transform, false);
        TextMeshProUGUI text = labelGO.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;

        return btnGO;
    }

    private static GameObject CreateInputField(Transform parent, string name, string placeholder,
        Vector2 pivot, Vector2 position, Vector2 size)
    {
        GameObject inputGO = new GameObject(name);
        inputGO.transform.SetParent(parent, false);

        RectTransform rect = inputGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, pivot.y);
        rect.anchorMax = new Vector2(0.5f, pivot.y);
        rect.pivot = new Vector2(0.5f, pivot.y);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image img = inputGO.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 1f); // Dark input bg

        TMP_InputField inputField = inputGO.AddComponent<TMP_InputField>();

        // Text Area
        GameObject textAreaGO = new GameObject("Text Area");
        textAreaGO.transform.SetParent(inputGO.transform, false);
        RectTransform textAreaRect = textAreaGO.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 0);
        textAreaRect.offsetMax = new Vector2(-10, 0);
        textAreaGO.AddComponent<RectMask2D>();

        // Placeholder text
        GameObject placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(textAreaGO.transform, false);
        TextMeshProUGUI placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 18;
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform phRect = placeholderGO.GetComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.sizeDelta = Vector2.zero;

        // Input text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(textAreaGO.transform, false);
        TextMeshProUGUI inputText = textGO.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 18;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform txtRect = textGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.sizeDelta = Vector2.zero;

        // Wire input field references
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputText;
        inputField.placeholder = placeholderText;
        inputField.characterLimit = 6; // Relay codes are 6 chars

        return inputGO;
    }
}
#endif
