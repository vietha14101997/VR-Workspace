using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Editor tool to generate TextMeshPro Font Assets with full Vietnamese character support.
/// Ensures all diacritics and special characters are included to avoid missing glyphs.
/// </summary>
public class TMPFontAssetGenerator : EditorWindow
{
    private Font sourceFont;
    private string outputPath = "Assets/VR-Workspace/Fonts/TMP";
    private int atlasResolution = 2048;
    private int samplingPointSize = 90;
    private int atlasPadding = 5;

    private bool includeBasicLatin = true;
    private bool includeVietnamese = true;
    private bool includeNumbers = true;
    private bool includeSymbols = true;
    private bool includeExtendedLatin = false;
    private bool includeCJKCommon = false;

    private Vector2 scrollPosition;
    private string previewCharacters = "";

    // Vietnamese character sets
    private const string VIETNAMESE_LOWERCASE = "àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ";
    private const string VIETNAMESE_UPPERCASE = "ÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬÈÉẺẼẸÊỀẾỂỄỆÌÍỈĨỊÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢÙÚỦŨỤƯỪỨỬỮỰỲÝỶỸỴĐ";

    private const string BASIC_LATIN_LOWERCASE = "abcdefghijklmnopqrstuvwxyz";
    private const string BASIC_LATIN_UPPERCASE = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string NUMBERS = "0123456789";

    private const string SYMBOLS = "!@#$%^&*()_+-=[]{}|;':\",./<>?`~\\© ";

    private const string EXTENDED_LATIN = "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿŒœŠšŸŽž";

    // Common CJK punctuation and symbols
    private const string CJK_COMMON = "。、！？「」『』（）【】〈〉《》〔〕｛｝［］・：；，．";

    [MenuItem("Tools/TMP Font Generator (Vietnamese)")]
    public static void ShowWindow()
    {
        var window = GetWindow<TMPFontAssetGenerator>("TMP Font Generator");
        window.minSize = new Vector2(400, 500);
    }

    private void OnEnable()
    {
        UpdatePreview();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("TMP Font Asset Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tạo Font Asset cho TextMeshPro với đầy đủ ký tự tiếng Việt.\n" +
            "Đảm bảo không bị lỗi dấu khi hiển thị văn bản.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Source font selection
        EditorGUILayout.LabelField("Source Font", EditorStyles.boldLabel);
        sourceFont = (Font)EditorGUILayout.ObjectField("Font File (.ttf/.otf)", sourceFont, typeof(Font), false);

        EditorGUILayout.Space(5);

        // Output settings
        EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        outputPath = EditorGUILayout.TextField("Output Folder", outputPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(selected) && selected.StartsWith(Application.dataPath))
            {
                outputPath = "Assets" + selected.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Atlas settings
        EditorGUILayout.LabelField("Atlas Settings", EditorStyles.boldLabel);
        atlasResolution = EditorGUILayout.IntPopup("Atlas Resolution", atlasResolution,
            new string[] { "512", "1024", "2048", "4096", "8192" },
            new int[] { 512, 1024, 2048, 4096, 8192 });
        samplingPointSize = EditorGUILayout.IntSlider("Sampling Point Size", samplingPointSize, 10, 200);
        atlasPadding = EditorGUILayout.IntSlider("Atlas Padding", atlasPadding, 1, 10);

        EditorGUILayout.Space(10);

        // Character sets
        EditorGUILayout.LabelField("Character Sets", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        includeBasicLatin = EditorGUILayout.Toggle("Basic Latin (A-Z, a-z)", includeBasicLatin);
        includeVietnamese = EditorGUILayout.Toggle("Vietnamese (Full diacritics)", includeVietnamese);
        includeNumbers = EditorGUILayout.Toggle("Numbers (0-9)", includeNumbers);
        includeSymbols = EditorGUILayout.Toggle("Symbols (!@#$%...)", includeSymbols);
        includeExtendedLatin = EditorGUILayout.Toggle("Extended Latin (Accents)", includeExtendedLatin);
        includeCJKCommon = EditorGUILayout.Toggle("CJK Punctuation", includeCJKCommon);

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreview();
        }

        EditorGUILayout.Space(10);

        // Preview
        EditorGUILayout.LabelField($"Character Preview ({previewCharacters.Length} characters)", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100));
        EditorGUILayout.TextArea(previewCharacters, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);

        // Generate button
        EditorGUI.BeginDisabledGroup(sourceFont == null);

        if (GUILayout.Button("Generate TMP Font Asset", GUILayout.Height(40)))
        {
            GenerateFontAsset();
        }

        EditorGUI.EndDisabledGroup();

        if (sourceFont == null)
        {
            EditorGUILayout.HelpBox("Please select a source font file (.ttf or .otf)", MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // Quick actions
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Select All"))
        {
            includeBasicLatin = includeVietnamese = includeNumbers = includeSymbols = includeExtendedLatin = includeCJKCommon = true;
            UpdatePreview();
        }

        if (GUILayout.Button("Vietnamese Only"))
        {
            includeBasicLatin = includeVietnamese = includeNumbers = includeSymbols = true;
            includeExtendedLatin = includeCJKCommon = false;
            UpdatePreview();
        }

        if (GUILayout.Button("Minimal"))
        {
            includeBasicLatin = includeVietnamese = includeNumbers = true;
            includeSymbols = includeExtendedLatin = includeCJKCommon = false;
            UpdatePreview();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void UpdatePreview()
    {
        previewCharacters = BuildCharacterSet();
    }

    private string BuildCharacterSet()
    {
        var chars = new HashSet<char>();

        if (includeBasicLatin)
        {
            AddChars(chars, BASIC_LATIN_LOWERCASE);
            AddChars(chars, BASIC_LATIN_UPPERCASE);
        }

        if (includeVietnamese)
        {
            AddChars(chars, VIETNAMESE_LOWERCASE);
            AddChars(chars, VIETNAMESE_UPPERCASE);
        }

        if (includeNumbers)
        {
            AddChars(chars, NUMBERS);
        }

        if (includeSymbols)
        {
            AddChars(chars, SYMBOLS);
        }

        if (includeExtendedLatin)
        {
            AddChars(chars, EXTENDED_LATIN);
        }

        if (includeCJKCommon)
        {
            AddChars(chars, CJK_COMMON);
        }

        // Sort and build string
        var sortedChars = new List<char>(chars);
        sortedChars.Sort();

        return new string(sortedChars.ToArray());
    }

    private void AddChars(HashSet<char> set, string chars)
    {
        foreach (char c in chars)
        {
            set.Add(c);
        }
    }

    private void GenerateFontAsset()
    {
        if (sourceFont == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a source font.", "OK");
            return;
        }

        // Ensure output directory exists
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
            AssetDatabase.Refresh();
        }

        string characterSet = BuildCharacterSet();
        string fontName = sourceFont.name;
        string outputFilePath = $"{outputPath}/{fontName}_TMP.asset";

        // Check if asset already exists
        if (File.Exists(outputFilePath))
        {
            if (!EditorUtility.DisplayDialog("Overwrite?",
                $"Font asset '{fontName}_TMP.asset' already exists.\nOverwrite?",
                "Yes", "No"))
            {
                return;
            }
        }

        try
        {
            EditorUtility.DisplayProgressBar("Generating Font Asset", "Creating TMP Font Asset...", 0.5f);

            // Create the font asset using TMP's built-in method
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                samplingPointSize,
                atlasPadding,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasResolution,
                atlasResolution,
                AtlasPopulationMode.Dynamic
            );

            if (fontAsset == null)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", "Failed to create font asset.", "OK");
                return;
            }

            // Try to add all characters
            EditorUtility.DisplayProgressBar("Generating Font Asset", "Adding characters...", 0.7f);

            uint[] unicodes = new uint[characterSet.Length];
            for (int i = 0; i < characterSet.Length; i++)
            {
                unicodes[i] = characterSet[i];
            }

            fontAsset.TryAddCharacters(unicodes, out uint[] missingUnicodes);

            // Save the asset
            EditorUtility.DisplayProgressBar("Generating Font Asset", "Saving asset...", 0.9f);

            AssetDatabase.CreateAsset(fontAsset, outputFilePath);

            // Save atlas texture
            if (fontAsset.atlasTexture != null)
            {
                fontAsset.atlasTexture.name = $"{fontName}_Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            }

            // Save material
            if (fontAsset.material != null)
            {
                fontAsset.material.name = $"{fontName}_Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();

            // Report results
            int addedCount = characterSet.Length - (missingUnicodes?.Length ?? 0);
            int missingCount = missingUnicodes?.Length ?? 0;

            string message = $"Font Asset created successfully!\n\n" +
                           $"Characters added: {addedCount}\n" +
                           $"Missing characters: {missingCount}";

            if (missingCount > 0 && missingUnicodes != null)
            {
                message += "\n\nMissing characters:\n";
                for (int i = 0; i < Mathf.Min(missingCount, 20); i++)
                {
                    message += $"'{(char)missingUnicodes[i]}' (U+{missingUnicodes[i]:X4}) ";
                }
                if (missingCount > 20)
                {
                    message += $"\n... and {missingCount - 20} more";
                }
            }

            EditorUtility.DisplayDialog("Success", message, "OK");

            // Ping the created asset
            Selection.activeObject = fontAsset;
            EditorGUIUtility.PingObject(fontAsset);
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Error", $"Failed to create font asset:\n{e.Message}", "OK");
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// Get the full Vietnamese character set for use in other scripts.
    /// </summary>
    public static string GetVietnameseCharacterSet()
    {
        return BASIC_LATIN_LOWERCASE + BASIC_LATIN_UPPERCASE +
               VIETNAMESE_LOWERCASE + VIETNAMESE_UPPERCASE +
               NUMBERS + SYMBOLS;
    }

    /// <summary>
    /// Get Unicode ranges for Vietnamese characters.
    /// Useful for TMP Font Asset settings.
    /// </summary>
    public static string GetVietnameseUnicodeRanges()
    {
        // Basic Latin + Vietnamese Unicode ranges
        return "0020-007E,00C0-00FF,0100-017F,1EA0-1EFF";
    }
}
