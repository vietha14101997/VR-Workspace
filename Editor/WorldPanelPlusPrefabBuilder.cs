#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class WorldPanelPlusPrefabBuilder
{
    [MenuItem("Tools/WorldPanel+/Create Prefab")]
    public static void CreatePrefab()
    {
        var go = new GameObject("WorldPanelPlus");
        var wpp = go.AddComponent<WorldPanelPlus>();
        wpp.Rebuild();

        var dir = "Assets/VR-Workspace/Prefabs";
        if (!AssetDatabase.IsValidFolder("Assets/VR-Workspace")) AssetDatabase.CreateFolder("Assets", "VR-Workspace");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/VR-Workspace", "Prefabs");

        var path = Path.Combine(dir, "WorldPanelPlus.prefab").Replace("\\", "/");
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(path);
        Debug.Log($"WorldPanelPlus prefab saved: {path}");
    }
}
#endif