using UnityEditor;
using UnityEngine;

/// <summary>
/// Fills one terrain detail prototype only where a selected Terrain Layer has
/// at least the chosen alphamap weight. The selected detail prototype is
/// replaced across the whole Terrain; Undo restores its previous placement.
/// </summary>
public sealed class TerrainDetailLayerPainter : EditorWindow
{
    private const int DetailRowsPerChunk = 64;

    private Terrain terrain;
    private int terrainLayerIndex;
    private int detailPrototypeIndex;
    private float minimumLayerWeight = 0.5f;
    private int densityPerCell = 1;

    [MenuItem("Tools/OakHaven/Fill Detail by Terrain Layer")]
    private static void OpenWindow()
    {
        TerrainDetailLayerPainter window = GetWindow<TerrainDetailLayerPainter>("Detail by Layer");
        window.minSize = new Vector2(360f, 250f);
        window.Show();
    }

    private void OnEnable()
    {
        if (terrain == null && Selection.activeGameObject != null)
            terrain = Selection.activeGameObject.GetComponent<Terrain>();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Distribute Terrain Detail", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Fills the selected detail prototype in cells where the selected Terrain Layer's alphamap weight meets the threshold. " +
            "The selected prototype is replaced across this Terrain; other detail prototypes are unchanged. The operation supports Undo.",
            MessageType.Info);

        terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
        TerrainData data = terrain != null ? terrain.terrainData : null;
        if (data == null)
        {
            EditorGUILayout.HelpBox("Assign a Terrain with Terrain Layers and detail prototypes.", MessageType.Warning);
            return;
        }

        TerrainLayer[] layers = data.terrainLayers;
        DetailPrototype[] details = data.detailPrototypes;
        if (layers == null || layers.Length == 0)
        {
            EditorGUILayout.HelpBox("This Terrain has no Terrain Layers.", MessageType.Warning);
            return;
        }

        if (details == null || details.Length == 0)
        {
            EditorGUILayout.HelpBox("This Terrain has no detail prototypes. Add one in Paint Terrain > Paint Details first.", MessageType.Warning);
            return;
        }

        string[] layerNames = new string[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            layerNames[i] = layers[i] != null ? layers[i].name : "Missing Terrain Layer " + i;

        string[] detailNames = new string[details.Length];
        for (int i = 0; i < details.Length; i++)
            detailNames[i] = GetDetailPrototypeName(details[i], i);

        terrainLayerIndex = Mathf.Clamp(terrainLayerIndex, 0, layers.Length - 1);
        detailPrototypeIndex = Mathf.Clamp(detailPrototypeIndex, 0, details.Length - 1);

        terrainLayerIndex = EditorGUILayout.Popup("Terrain Layer", terrainLayerIndex, layerNames);
        detailPrototypeIndex = EditorGUILayout.Popup("Detail Prototype", detailPrototypeIndex, detailNames);
        minimumLayerWeight = EditorGUILayout.Slider("Minimum Layer Weight", minimumLayerWeight, 0.01f, 1f);
        densityPerCell = EditorGUILayout.IntSlider("Density per Detail Cell", densityPerCell, 1, 16);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(layers[terrainLayerIndex] == null))
        {
            if (GUILayout.Button("Fill Selected Detail on Layer", GUILayout.Height(32f)))
                FillSelectedDetail(terrain, terrainLayerIndex, detailPrototypeIndex,
                    minimumLayerWeight, densityPerCell);
        }
    }

    private static string GetDetailPrototypeName(DetailPrototype detail, int index)
    {
        if (detail != null && detail.prototype != null)
            return detail.prototype.name;

        if (detail != null && detail.prototypeTexture != null)
            return detail.prototypeTexture.name;

        return "Detail " + index;
    }

    private static void FillSelectedDetail(
        Terrain targetTerrain,
        int selectedTerrainLayer,
        int selectedDetailPrototype,
        float threshold,
        int density)
    {
        TerrainData data = targetTerrain != null ? targetTerrain.terrainData : null;
        if (data == null || data.detailResolution <= 0 || data.alphamapResolution <= 0)
            return;

        int detailResolution = data.detailResolution;
        int alphaResolution = data.alphamapResolution;
        int[,] newDetailLayer = new int[detailResolution, detailResolution];
        int[] sampledAlphaX = new int[detailResolution];

        for (int x = 0; x < detailResolution; x++)
        {
            float u = (x + 0.5f) / detailResolution;
            sampledAlphaX[x] = Mathf.Clamp(
                Mathf.RoundToInt(u * (alphaResolution - 1)), 0, alphaResolution - 1);
        }

        try
        {
            for (int detailY = 0; detailY < detailResolution; detailY += DetailRowsPerChunk)
            {
                int rowCount = Mathf.Min(DetailRowsPerChunk, detailResolution - detailY);
                float firstV = (detailY + 0.5f) / detailResolution;
                float lastV = (detailY + rowCount - 0.5f) / detailResolution;
                int alphaYStart = Mathf.Clamp(
                    Mathf.RoundToInt(firstV * (alphaResolution - 1)), 0, alphaResolution - 1);
                int alphaYEnd = Mathf.Clamp(
                    Mathf.RoundToInt(lastV * (alphaResolution - 1)), alphaYStart, alphaResolution - 1);
                int alphaRowCount = alphaYEnd - alphaYStart + 1;

                float progress = (float)detailY / detailResolution;
                if (EditorUtility.DisplayCancelableProgressBar(
                    "Filling Terrain Detail", "Sampling Terrain Layer weights...", progress))
                    return;

                float[,,] alphaBand = data.GetAlphamaps(0, alphaYStart, alphaResolution, alphaRowCount);

                for (int localY = 0; localY < rowCount; localY++)
                {
                    int y = detailY + localY;
                    float v = (y + 0.5f) / detailResolution;
                    int alphaY = Mathf.Clamp(
                        Mathf.RoundToInt(v * (alphaResolution - 1)), alphaYStart, alphaYEnd) - alphaYStart;

                    for (int x = 0; x < detailResolution; x++)
                    {
                        float layerWeight = alphaBand[alphaY, sampledAlphaX[x], selectedTerrainLayer];
                        if (layerWeight >= threshold)
                            newDetailLayer[y, x] = density;
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.RegisterCompleteObjectUndo(data, "Fill Terrain Detail by Terrain Layer");
        data.SetDetailLayer(0, 0, selectedDetailPrototype, newDetailLayer);
        EditorUtility.SetDirty(data);
        targetTerrain.Flush();
        SceneView.RepaintAll();

        Debug.Log("Filled detail prototype " + selectedDetailPrototype + " where Terrain Layer " +
                  selectedTerrainLayer + " has weight >= " + threshold.ToString("0.00") + ". Undo is available.");
    }
}
