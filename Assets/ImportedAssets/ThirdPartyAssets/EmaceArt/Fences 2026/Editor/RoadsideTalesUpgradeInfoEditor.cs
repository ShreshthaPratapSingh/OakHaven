using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(RoadsideTalesUpgradeInfo))]
public class RoadsideTalesUpgradeInfoEditor : Editor
{
    private Texture2D coverImage;
    private GUIStyle headlineStyle;
    private GUIStyle bodyTextStyle;
    private GUIStyle thankYouStyle;
    
    private const string TargetUrl = "https://assetstore.unity.com/packages/tools/level-design/roadside-tales-pro-modular-fence-narrative-pack-spline-tool-350100";
    private const string VideoUrl = "https://www.youtube.com/watch?v=0zwgU23z5tE";

    private void OnEnable()
    {
        string pathPNG = "Assets/EmaceArt/Fences 2026/Scripts/Cover.png";
        string pathJPG = "Assets/EmaceArt/Fences 2026/Scripts/Cover.jpg";

        coverImage = AssetDatabase.LoadAssetAtPath<Texture2D>(pathPNG);
        if (coverImage == null) coverImage = AssetDatabase.LoadAssetAtPath<Texture2D>(pathJPG);
    }

    public override void OnInspectorGUI()
    {
        RoadsideTalesUpgradeInfo info = (RoadsideTalesUpgradeInfo)target;

        if (headlineStyle == null)
        {
            headlineStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true, richText = true, margin = new RectOffset(2, 2, 0, 0) };
            bodyTextStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true, margin = new RectOffset(2, 2, 0, 0) };
            thankYouStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, richText = true, margin = new RectOffset(5, 5, 5, 5), alignment = TextAnchor.MiddleCenter };
        }

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("<b>Thank you for choosing EmaceArt as your publisher!</b>", thankYouStyle);
        GUILayout.Label("You are exploring:\n<b>Roadside Tales Free - Modular Fence Narrative Pack</b>", thankYouStyle);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        if (coverImage != null)
        {
            float imageAspect = (float)coverImage.width / coverImage.height;
            float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
            
            float drawWidth = Mathf.Min(coverImage.width, availableWidth);
            float drawHeight = (drawWidth / imageAspect) + 80f;

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Rect imageRect = GUILayoutUtility.GetRect(drawWidth, drawHeight, GUILayout.Width(drawWidth), GUILayout.Height(drawHeight));
            GUI.DrawTexture(imageRect, coverImage, ScaleMode.StretchToFill);

            if (GUI.Button(imageRect, GUIContent.none, GUIStyle.none))
            {
                Application.OpenURL(TargetUrl);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            EditorGUILayout.Space(2);
        }

        GUILayout.Label("<b>Roadside Tales PRO – Modular Fence & Spline Tool</b>", headlineStyle);
        EditorGUILayout.Space(2);

        GUILayout.Label("You are currently using Roadside Tales Free. It is a solid foundation, but only a fraction of the full storytelling potential.", bodyTextStyle);
        EditorGUILayout.Space(2);

        GUILayout.Label("<b>Roadside Tales PRO</b> features a massive collection of 179 diverse fence models across multiple categories (Rural, Ruins, Palisades). These barriers do more than block paths: they guide players, hint at hidden histories, and shape exploration.", bodyTextStyle);
        EditorGUILayout.Space(2);

        GUILayout.Label("Includes <b>EasyFenceTool</b> — a custom Unity editor brush for placing fence paths directly in the scene. Snap to terrain, use a weighted randomization system, control posts independently, and bake the result to real scene objects in one click.", bodyTextStyle);
        EditorGUILayout.Space(5);

        GUIStyle mainButtonStyle = new GUIStyle(GUI.skin.button) 
        { 
            fontStyle = FontStyle.Bold, 
            fontSize = 11,
            wordWrap = true,
            padding = new RectOffset(5, 5, 5, 5)
        };

        if (GUILayout.Button("Get Roadside Tales PRO - Modular Fence Pack", mainButtonStyle, GUILayout.Height(35)))
        {
            Application.OpenURL(TargetUrl);
        }

        EditorGUILayout.Space(2);

        if (GUILayout.Button("Watch Video Trailer", mainButtonStyle, GUILayout.Height(30)))
        {
            Application.OpenURL(VideoUrl);
        }

        EditorGUILayout.Space(15);

        GUIStyle deleteButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };
        deleteButtonStyle.normal.textColor = new Color(0.9f, 0.3f, 0.3f);
        
        if (GUILayout.Button("Delete Promo Object & Save Scene", deleteButtonStyle))
        {
            GameObject objToDestroy = info.gameObject;
            Undo.DestroyObjectImmediate(objToDestroy);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            return; 
        }
    }
}