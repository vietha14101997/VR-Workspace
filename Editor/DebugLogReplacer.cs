using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// Editor tool to replace Debug.Log calls with AppLog calls.
/// AppLog methods are stripped from builds using [Conditional] attribute.
/// </summary>
public class DebugLogReplacer : EditorWindow
{
    private string targetFolder = "Assets/VR-Workspace/Scripts";
    private bool replaceLog = true;
    private bool replaceLogWarning = true;
    private bool replaceLogFormat = true;
    private bool replaceLogWarningFormat = true;
    private bool skipLogError = true; // Errors should remain visible
    private bool addUsingStatement = true;
    private bool dryRun = true;
    private Vector2 scrollPosition;
    private List<FileChangeInfo> pendingChanges = new List<FileChangeInfo>();

    private class FileChangeInfo
    {
        public string FilePath;
        public int LogCount;
        public int LogWarningCount;
        public int LogFormatCount;
        public int LogWarningFormatCount;
        public bool NeedsUsing;
        public string Preview;
    }

    [MenuItem("Tools/Debug Log Replacer")]
    public static void ShowWindow()
    {
        GetWindow<DebugLogReplacer>("Log Replacer");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Replace Debug.Log → AppLog", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "AppLog methods use [Conditional(\"UNITY_EDITOR\")] attribute.\n" +
            "They are completely stripped from Android/Quest builds = zero overhead.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Folder selection
        EditorGUILayout.BeginHorizontal();
        targetFolder = EditorGUILayout.TextField("Target Folder", targetFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
            if (!string.IsNullOrEmpty(selected))
            {
                if (selected.StartsWith(Application.dataPath))
                {
                    targetFolder = "Assets" + selected.Substring(Application.dataPath.Length);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Options
        EditorGUILayout.LabelField("Replace Options:", EditorStyles.boldLabel);
        replaceLog = EditorGUILayout.Toggle("Debug.Log → AppLog.Log", replaceLog);
        replaceLogWarning = EditorGUILayout.Toggle("Debug.LogWarning → AppLog.LogWarning", replaceLogWarning);
        replaceLogFormat = EditorGUILayout.Toggle("Debug.LogFormat → AppLog.LogFormat", replaceLogFormat);
        replaceLogWarningFormat = EditorGUILayout.Toggle("Debug.LogWarningFormat → AppLog.LogWarningFormat", replaceLogWarningFormat);

        EditorGUILayout.Space(5);
        skipLogError = EditorGUILayout.Toggle("Keep Debug.LogError (recommended)", skipLogError);
        addUsingStatement = EditorGUILayout.Toggle("Add 'using VRWorkspace.Core'", addUsingStatement);

        EditorGUILayout.Space(10);

        // Actions
        EditorGUILayout.BeginHorizontal();

        dryRun = EditorGUILayout.Toggle("Preview Only", dryRun);

        if (GUILayout.Button("Scan Files", GUILayout.Height(30)))
        {
            ScanFiles();
        }

        EditorGUI.BeginDisabledGroup(dryRun || pendingChanges.Count == 0);
        if (GUILayout.Button("Apply Changes", GUILayout.Height(30)))
        {
            ApplyChanges();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Results
        if (pendingChanges.Count > 0)
        {
            int totalLogs = 0, totalWarnings = 0, totalFormats = 0, totalWarningFormats = 0;

            foreach (var change in pendingChanges)
            {
                totalLogs += change.LogCount;
                totalWarnings += change.LogWarningCount;
                totalFormats += change.LogFormatCount;
                totalWarningFormats += change.LogWarningFormatCount;
            }

            EditorGUILayout.LabelField($"Found {pendingChanges.Count} files to modify:");
            EditorGUILayout.LabelField($"  • Debug.Log: {totalLogs}");
            EditorGUILayout.LabelField($"  • Debug.LogWarning: {totalWarnings}");
            EditorGUILayout.LabelField($"  • Debug.LogFormat: {totalFormats}");
            EditorGUILayout.LabelField($"  • Debug.LogWarningFormat: {totalWarningFormats}");
            EditorGUILayout.LabelField($"  Total replacements: {totalLogs + totalWarnings + totalFormats + totalWarningFormats}");

            EditorGUILayout.Space(5);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            foreach (var change in pendingChanges)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(change.FilePath, EditorStyles.boldLabel);

                string summary = "";
                if (change.LogCount > 0) summary += $"Log:{change.LogCount} ";
                if (change.LogWarningCount > 0) summary += $"Warn:{change.LogWarningCount} ";
                if (change.LogFormatCount > 0) summary += $"LogFmt:{change.LogFormatCount} ";
                if (change.LogWarningFormatCount > 0) summary += $"WarnFmt:{change.LogWarningFormatCount} ";
                if (change.NeedsUsing) summary += "[+using]";

                EditorGUILayout.LabelField(summary);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void ScanFiles()
    {
        pendingChanges.Clear();

        if (!Directory.Exists(targetFolder))
        {
            Debug.LogError($"Folder not found: {targetFolder}");
            return;
        }

        string[] csFiles = Directory.GetFiles(targetFolder, "*.cs", SearchOption.AllDirectories);

        foreach (string filePath in csFiles)
        {
            // Skip Editor folder if needed
            if (filePath.Contains("\\Editor\\") || filePath.Contains("/Editor/"))
            {
                // Optionally skip editor scripts
                // continue;
            }

            // Skip this script itself
            if (filePath.EndsWith("DebugLogReplacer.cs") || filePath.EndsWith("AlwaysIncludedShadersManager.cs"))
                continue;

            string content = File.ReadAllText(filePath);
            var change = AnalyzeFile(filePath, content);

            if (change != null)
            {
                pendingChanges.Add(change);
            }
        }

        Debug.Log($"Scanned {csFiles.Length} files. Found {pendingChanges.Count} files with Debug.Log calls.");
    }

    private FileChangeInfo AnalyzeFile(string filePath, string content)
    {
        var info = new FileChangeInfo
        {
            FilePath = filePath,
            LogCount = 0,
            LogWarningCount = 0,
            LogFormatCount = 0,
            LogWarningFormatCount = 0
        };

        // Count occurrences (excluding LogError and LogException)
        if (replaceLog)
        {
            // Match Debug.Log but not Debug.LogWarning, Debug.LogError, Debug.LogException, Debug.LogFormat
            info.LogCount = Regex.Matches(content, @"Debug\.Log\s*\((?!Warning|Error|Exception|Format)").Count;
        }

        if (replaceLogWarning)
        {
            // Match Debug.LogWarning but not Debug.LogWarningFormat
            info.LogWarningCount = Regex.Matches(content, @"Debug\.LogWarning\s*\((?!Format)").Count;
        }

        if (replaceLogFormat)
        {
            info.LogFormatCount = Regex.Matches(content, @"Debug\.LogFormat\s*\(").Count;
        }

        if (replaceLogWarningFormat)
        {
            info.LogWarningFormatCount = Regex.Matches(content, @"Debug\.LogWarningFormat\s*\(").Count;
        }

        int total = info.LogCount + info.LogWarningCount + info.LogFormatCount + info.LogWarningFormatCount;

        if (total == 0)
            return null;

        // Check if using statement needed
        info.NeedsUsing = !content.Contains("using VRWorkspace.Core;") && addUsingStatement;

        return info;
    }

    private void ApplyChanges()
    {
        int filesModified = 0;
        int totalReplacements = 0;

        foreach (var change in pendingChanges)
        {
            string content = File.ReadAllText(change.FilePath);
            string originalContent = content;

            // Add using statement if needed
            if (change.NeedsUsing && addUsingStatement)
            {
                // Find the last using statement and add after it
                var usingMatch = Regex.Match(content, @"(using\s+[\w\.]+;\s*\n)(?!using)", RegexOptions.RightToLeft);
                if (usingMatch.Success)
                {
                    int insertPos = usingMatch.Index + usingMatch.Length;
                    content = content.Insert(insertPos, "using VRWorkspace.Core;\n");
                }
                else
                {
                    // No using statements found, add at the beginning
                    content = "using VRWorkspace.Core;\n" + content;
                }
            }

            // Replace Debug.Log (but not Debug.LogWarning, Debug.LogError, etc.)
            if (replaceLog)
            {
                content = Regex.Replace(content, @"Debug\.Log\s*\((?!Warning|Error|Exception|Format)", "AppLog.Log(");
                totalReplacements += change.LogCount;
            }

            // Replace Debug.LogWarning (but not Debug.LogWarningFormat)
            if (replaceLogWarning)
            {
                content = Regex.Replace(content, @"Debug\.LogWarning\s*\((?!Format)", "AppLog.LogWarning(");
                totalReplacements += change.LogWarningCount;
            }

            // Replace Debug.LogFormat
            if (replaceLogFormat)
            {
                content = Regex.Replace(content, @"Debug\.LogFormat\s*\(", "AppLog.LogFormat(");
                totalReplacements += change.LogFormatCount;
            }

            // Replace Debug.LogWarningFormat
            if (replaceLogWarningFormat)
            {
                content = Regex.Replace(content, @"Debug\.LogWarningFormat\s*\(", "AppLog.LogWarningFormat(");
                totalReplacements += change.LogWarningFormatCount;
            }

            if (content != originalContent)
            {
                File.WriteAllText(change.FilePath, content);
                filesModified++;
            }
        }

        AssetDatabase.Refresh();

        Debug.Log($"Modified {filesModified} files with {totalReplacements} replacements.");
        pendingChanges.Clear();
    }
}
