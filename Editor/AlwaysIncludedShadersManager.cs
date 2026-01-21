using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class AlwaysIncludedShadersManager : EditorWindow
{
    private List<Shader> shadersToAdd = new List<Shader>();
    private Vector2 scrollPosition;
    private bool showCurrentShaders = true;

    [MenuItem("Tools/Always Included Shaders Manager")]
    public static void ShowWindow()
    {
        GetWindow<AlwaysIncludedShadersManager>("Shaders Manager");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Always Included Shaders Manager", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Buttons row
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Remove Missing Shaders", GUILayout.Height(30)))
        {
            RemoveMissingShaders();
        }

        if (GUILayout.Button("Add All Project Shaders", GUILayout.Height(30)))
        {
            AddAllProjectShaders();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Drag and drop area
        EditorGUILayout.LabelField("Drag Shaders Here to Add:", EditorStyles.boldLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop Shaders Here");

        HandleDragAndDrop(dropArea);

        EditorGUILayout.Space(10);

        // Shaders to add list
        if (shadersToAdd.Count > 0)
        {
            EditorGUILayout.LabelField($"Shaders to Add ({shadersToAdd.Count}):", EditorStyles.boldLabel);

            for (int i = shadersToAdd.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(shadersToAdd[i], typeof(Shader), false);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    shadersToAdd.RemoveAt(i);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Selected Shaders", GUILayout.Height(25)))
            {
                AddShaders(shadersToAdd.ToArray());
                shadersToAdd.Clear();
            }

            if (GUILayout.Button("Clear List", GUILayout.Height(25)))
            {
                shadersToAdd.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(10);

        // Current shaders display
        showCurrentShaders = EditorGUILayout.Foldout(showCurrentShaders, "Current Always Included Shaders", true);

        if (showCurrentShaders)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

            var currentShaders = GetCurrentAlwaysIncludedShaders();
            int validCount = 0;
            int missingCount = 0;

            for (int i = 0; i < currentShaders.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();

                if (currentShaders[i] != null)
                {
                    validCount++;
                    EditorGUILayout.LabelField($"{i}: {currentShaders[i].name}");

                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        RemoveShaderAtIndex(i);
                    }
                }
                else
                {
                    missingCount++;
                    EditorGUILayout.LabelField($"{i}: [MISSING]", new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } });
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField($"Total: {currentShaders.Length} | Valid: {validCount} | Missing: {missingCount}");
        }
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;

        if (!dropArea.Contains(evt.mousePosition))
            return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        Shader shader = obj as Shader;
                        if (shader != null && !shadersToAdd.Contains(shader))
                        {
                            if (!IsShaderAlreadyIncluded(shader))
                            {
                                shadersToAdd.Add(shader);
                            }
                            else
                            {
                                Debug.Log($"Shader '{shader.name}' is already in Always Included Shaders.");
                            }
                        }
                    }
                }
                evt.Use();
                break;
        }
    }

    private static Shader[] GetCurrentAlwaysIncludedShaders()
    {
        var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>(
            "ProjectSettings/GraphicsSettings.asset");

        SerializedObject serializedObject = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);

        SerializedProperty arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        Shader[] shaders = new Shader[arrayProp.arraySize];
        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            shaders[i] = arrayProp.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
        }

        return shaders;
    }

    private static bool IsShaderAlreadyIncluded(Shader shader)
    {
        var currentShaders = GetCurrentAlwaysIncludedShaders();
        return currentShaders.Any(s => s != null && s == shader);
    }

    [MenuItem("Tools/Shaders/Remove Missing Shaders")]
    public static void RemoveMissingShaders()
    {
        SerializedObject serializedObject = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);

        SerializedProperty arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        int removedCount = 0;

        for (int i = arrayProp.arraySize - 1; i >= 0; i--)
        {
            if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
            {
                arrayProp.DeleteArrayElementAtIndex(i);
                removedCount++;
            }
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log($"Removed {removedCount} missing shader(s) from Always Included Shaders.");
    }

    public static void AddShaders(params Shader[] shaders)
    {
        if (shaders == null || shaders.Length == 0)
            return;

        SerializedObject serializedObject = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);

        SerializedProperty arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        int addedCount = 0;

        foreach (var shader in shaders)
        {
            if (shader == null)
                continue;

            if (IsShaderAlreadyIncluded(shader))
            {
                Debug.Log($"Shader '{shader.name}' is already included. Skipping.");
                continue;
            }

            int newIndex = arrayProp.arraySize;
            arrayProp.InsertArrayElementAtIndex(newIndex);
            arrayProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
            addedCount++;
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log($"Added {addedCount} shader(s) to Always Included Shaders.");
    }

    public static void AddShadersByName(params string[] shaderNames)
    {
        List<Shader> shaders = new List<Shader>();

        foreach (var name in shaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader != null)
            {
                shaders.Add(shader);
            }
            else
            {
                Debug.LogWarning($"Shader not found: {name}");
            }
        }

        AddShaders(shaders.ToArray());
    }

    private void RemoveShaderAtIndex(int index)
    {
        SerializedObject serializedObject = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);

        SerializedProperty arrayProp = serializedObject.FindProperty("m_AlwaysIncludedShaders");

        if (index >= 0 && index < arrayProp.arraySize)
        {
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"Removed shader at index {index}.");
        }
    }

    [MenuItem("Tools/Shaders/Add All Project Shaders")]
    public static void AddAllProjectShaders()
    {
        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
        List<Shader> projectShaders = new List<Shader>();

        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Skip Unity built-in packages if needed
            if (path.StartsWith("Packages/") && !path.Contains("com.unity"))
                continue;

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null)
            {
                projectShaders.Add(shader);
            }
        }

        AddShaders(projectShaders.ToArray());
    }

    [MenuItem("Tools/Shaders/Add Shaders From Folder")]
    public static void AddShadersFromSelectedFolder()
    {
        string folderPath = EditorUtility.OpenFolderPanel("Select Shader Folder", "Assets", "");

        if (string.IsNullOrEmpty(folderPath))
            return;

        // Convert to relative path
        if (folderPath.StartsWith(Application.dataPath))
        {
            folderPath = "Assets" + folderPath.Substring(Application.dataPath.Length);
        }

        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader", new[] { folderPath });
        List<Shader> shaders = new List<Shader>();

        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null)
            {
                shaders.Add(shader);
            }
        }

        AddShaders(shaders.ToArray());
    }

    // Auto-add specific shaders on project load (optional)
    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        // Uncomment below to auto-clean missing shaders on project load
        // EditorApplication.delayCall += RemoveMissingShaders;
    }
}
