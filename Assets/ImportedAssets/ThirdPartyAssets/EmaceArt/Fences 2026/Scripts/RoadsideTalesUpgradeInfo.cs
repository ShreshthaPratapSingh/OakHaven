using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class RoadsideTalesUpgradeInfo : MonoBehaviour
{
    public string assetUrl = "https://assetstore.unity.com/packages/tools/level-design/roadside-tales-pro-modular-fence-narrative-pack-spline-tool-350100";

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (Application.isPlaying) return;

        EditorApplication.delayCall += () =>
        {
            if (this != null && gameObject != null)
            {
                Selection.activeGameObject = gameObject;
            }
        };
    }
#endif
}