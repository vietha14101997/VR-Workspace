using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;

/// <summary>
/// Editor tool to generate TextMeshPro Font Assets with full Vietnamese character support.
/// Uses Unicode Range (Hex) format for comprehensive character coverage.
/// </summary>
public class TMPFontAssetGenerator : EditorWindow
{
    private Font sourceFont;
    private string outputPath = "Assets/VR-Workspace/Fonts/TMP";
    private int atlasResolution = 2048;
    private int samplingPointSize = 90;
    private int atlasPadding = 5;

    private Vector2 scrollPosition;
    private Vector2 previewScrollPosition;
    private string customUnicodeRanges = "";
    private string previewCharacters = "";
    private int totalCharCount = 0;

    // Unicode Range Presets (Hex format)
    private static class UnicodeRanges
    {
        // Basic Latin: Space to Tilde (printable ASCII)
        public const string BASIC_LATIN = "0020-007E";

        // Latin-1 Supplement: Extended Latin characters
        public const string LATIN_1_SUPPLEMENT = "00A0-00FF";

        // Latin Extended-A: European Latin
        public const string LATIN_EXTENDED_A = "0100-017F";

        // Latin Extended-B: African, Croatian, Romanian, etc.
        public const string LATIN_EXTENDED_B = "0180-024F";

        // Vietnamese: All Vietnamese diacritics
        public const string VIETNAMESE = "1EA0-1EFF";

        // Latin Extended Additional: Vietnamese + Welsh
        public const string LATIN_EXTENDED_ADDITIONAL = "1E00-1EFF";

        // General Punctuation
        public const string GENERAL_PUNCTUATION = "2000-206F";

        // Currency Symbols
        public const string CURRENCY_SYMBOLS = "20A0-20CF";

        // Letterlike Symbols
        public const string LETTERLIKE_SYMBOLS = "2100-214F";

        // Number Forms
        public const string NUMBER_FORMS = "2150-218F";

        // Arrows
        public const string ARROWS = "2190-21FF";

        // Mathematical Operators
        public const string MATH_OPERATORS = "2200-22FF";

        // Box Drawing
        public const string BOX_DRAWING = "2500-257F";

        // Geometric Shapes
        public const string GEOMETRIC_SHAPES = "25A0-25FF";

        // CJK Symbols and Punctuation
        public const string CJK_SYMBOLS = "3000-303F";

        // Halfwidth and Fullwidth Forms
        public const string HALFWIDTH_FULLWIDTH = "FF00-FFEF";

        // Private Use Area (custom icons)
        public const string PRIVATE_USE = "E000-F8FF";

        // Combining Diacritical Marks (for proper accent rendering)
        public const string COMBINING_DIACRITICS = "0300-036F";
    }

    // Preset configurations
    private enum PresetType
    {
        Custom,
        VietnameseFull,
        VietnameseMinimal,
        LatinComplete,
        AllLanguages
    }

    private PresetType selectedPreset = PresetType.VietnameseFull;

    private static readonly Dictionary<PresetType, string> PresetRanges = new Dictionary<PresetType, string>
    {
        {
            PresetType.VietnameseFull,
            $"{UnicodeRanges.BASIC_LATIN}\n" +
            $"{UnicodeRanges.LATIN_1_SUPPLEMENT}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_A}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_B}\n" +
            $"{UnicodeRanges.VIETNAMESE}\n" +
            $"{UnicodeRanges.COMBINING_DIACRITICS}\n" +
            $"{UnicodeRanges.GENERAL_PUNCTUATION}\n" +
            $"{UnicodeRanges.CURRENCY_SYMBOLS}"
        },
        {
            PresetType.VietnameseMinimal,
            $"{UnicodeRanges.BASIC_LATIN}\n" +
            $"{UnicodeRanges.VIETNAMESE}\n" +
            $"{UnicodeRanges.COMBINING_DIACRITICS}"
        },
        {
            PresetType.LatinComplete,
            $"{UnicodeRanges.BASIC_LATIN}\n" +
            $"{UnicodeRanges.LATIN_1_SUPPLEMENT}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_A}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_B}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_ADDITIONAL}\n" +
            $"{UnicodeRanges.COMBINING_DIACRITICS}\n" +
            $"{UnicodeRanges.GENERAL_PUNCTUATION}\n" +
            $"{UnicodeRanges.CURRENCY_SYMBOLS}\n" +
            $"{UnicodeRanges.LETTERLIKE_SYMBOLS}\n" +
            $"{UnicodeRanges.NUMBER_FORMS}"
        },
        {
            PresetType.AllLanguages,
            $"{UnicodeRanges.BASIC_LATIN}\n" +
            $"{UnicodeRanges.LATIN_1_SUPPLEMENT}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_A}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_B}\n" +
            $"{UnicodeRanges.LATIN_EXTENDED_ADDITIONAL}\n" +
            $"{UnicodeRanges.COMBINING_DIACRITICS}\n" +
            $"{UnicodeRanges.GENERAL_PUNCTUATION}\n" +
            $"{UnicodeRanges.CURRENCY_SYMBOLS}\n" +
            $"{UnicodeRanges.LETTERLIKE_SYMBOLS}\n" +
            $"{UnicodeRanges.NUMBER_FORMS}\n" +
            $"{UnicodeRanges.ARROWS}\n" +
            $"{UnicodeRanges.MATH_OPERATORS}\n" +
            $"{UnicodeRanges.BOX_DRAWING}\n" +
            $"{UnicodeRanges.GEOMETRIC_SHAPES}\n" +
            $"{UnicodeRanges.CJK_SYMBOLS}\n" +
            $"{UnicodeRanges.HALFWIDTH_FULLWIDTH}"
        }
    };

    [MenuItem("Tools/TMP Font Generator (Vietnamese)")]
    public static void ShowWindow()
    {
        var window = GetWindow<TMPFontAssetGenerator>("TMP Font Generator");
        window.minSize = new Vector2(450, 600);
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(customUnicodeRanges))
        {
            customUnicodeRanges = PresetRanges[PresetType.VietnameseFull];
        }
        UpdatePreview();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("TMP Font Asset Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tạo Font Asset cho TextMeshPro sử dụng Unicode Range (Hex).\n" +
            "Đảm bảo đầy đủ ký tự tiếng Việt và các ngôn ngữ khác.",
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

        // Preset selection
        EditorGUILayout.LabelField("Unicode Range Preset", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        selectedPreset = (PresetType)EditorGUILayout.EnumPopup("Preset", selectedPreset);

        if (EditorGUI.EndChangeCheck() && selectedPreset != PresetType.Custom)
        {
            customUnicodeRanges = PresetRanges[selectedPreset];
            UpdatePreview();
        }

        EditorGUILayout.Space(5);

        // Unicode ranges input
        EditorGUILayout.LabelField($"Unicode Ranges (Hex) - {totalCharCount} characters", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(120));
        customUnicodeRanges = EditorGUILayout.TextArea(customUnicodeRanges, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck())
        {
            selectedPreset = PresetType.Custom;
            UpdatePreview();
        }

        // Quick add buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+Vietnamese", EditorStyles.miniButton))
        {
            AddRange(UnicodeRanges.VIETNAMESE);
        }
        if (GUILayout.Button("+Latin Ext", EditorStyles.miniButton))
        {
            AddRange(UnicodeRanges.LATIN_EXTENDED_A + "\n" + UnicodeRanges.LATIN_EXTENDED_B);
        }
        if (GUILayout.Button("+Symbols", EditorStyles.miniButton))
        {
            AddRange(UnicodeRanges.GENERAL_PUNCTUATION + "\n" + UnicodeRanges.CURRENCY_SYMBOLS);
        }
        if (GUILayout.Button("+CJK", EditorStyles.miniButton))
        {
            AddRange(UnicodeRanges.CJK_SYMBOLS + "\n" + UnicodeRanges.HALFWIDTH_FULLWIDTH);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Unicode ranges reference
        if (GUILayout.Button("Show Unicode Ranges Reference", EditorStyles.linkLabel))
        {
            ShowUnicodeRangesReference();
        }

        EditorGUILayout.Space(10);

        // Preview
        EditorGUILayout.LabelField("Character Preview (Sample)", EditorStyles.boldLabel);

        previewScrollPosition = EditorGUILayout.BeginScrollView(previewScrollPosition, GUILayout.Height(60));
        EditorGUILayout.SelectableLabel(previewCharacters, EditorStyles.textArea, GUILayout.ExpandHeight(true));
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
    }

    private void AddRange(string range)
    {
        if (!customUnicodeRanges.Contains(range))
        {
            customUnicodeRanges = customUnicodeRanges.TrimEnd() + "\n" + range;
            selectedPreset = PresetType.Custom;
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        var chars = ParseUnicodeRanges(customUnicodeRanges);
        totalCharCount = chars.Count;

        // Build preview string (sample of characters)
        var preview = new StringBuilder();
        int count = 0;
        foreach (uint unicode in chars)
        {
            if (count >= 200) // Limit preview
            {
                preview.Append("...");
                break;
            }

            char c = (char)unicode;
            if (!char.IsControl(c))
            {
                preview.Append(c);
                count++;
            }
        }
        previewCharacters = preview.ToString();
    }

    private HashSet<uint> ParseUnicodeRanges(string rangesText)
    {
        var result = new HashSet<uint>();

        if (string.IsNullOrEmpty(rangesText))
            return result;

        // Split by newlines, commas, spaces
        string[] lines = rangesText.Split(new[] { '\n', '\r', ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Check if it's a range (XXXX-YYYY) or single value (XXXX)
            if (trimmed.Contains("-"))
            {
                string[] parts = trimmed.Split('-');
                if (parts.Length == 2)
                {
                    if (TryParseHex(parts[0], out uint start) && TryParseHex(parts[1], out uint end))
                    {
                        for (uint i = start; i <= end && i <= 0xFFFF; i++)
                        {
                            result.Add(i);
                        }
                    }
                }
            }
            else
            {
                if (TryParseHex(trimmed, out uint value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    private bool TryParseHex(string hex, out uint value)
    {
        hex = hex.Trim().TrimStart('0', 'x', 'X', 'U', '+');
        return uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value);
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

        var unicodes = ParseUnicodeRanges(customUnicodeRanges);
        if (unicodes.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No valid Unicode ranges specified.", "OK");
            return;
        }

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

            // Delete existing asset
            AssetDatabase.DeleteAsset(outputFilePath);
        }

        try
        {
            EditorUtility.DisplayProgressBar("Generating Font Asset", "Creating TMP Font Asset...", 0.3f);

            // Create the font asset
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

            // Add characters
            EditorUtility.DisplayProgressBar("Generating Font Asset", $"Adding {unicodes.Count} characters...", 0.6f);

            uint[] unicodeArray = new uint[unicodes.Count];
            unicodes.CopyTo(unicodeArray);

            fontAsset.TryAddCharacters(unicodeArray, out uint[] missingUnicodes);

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
            int addedCount = unicodes.Count - (missingUnicodes?.Length ?? 0);
            int missingCount = missingUnicodes?.Length ?? 0;

            string message = $"Font Asset created successfully!\n\n" +
                           $"Total Unicode ranges: {unicodes.Count}\n" +
                           $"Characters added: {addedCount}\n" +
                           $"Missing in font: {missingCount}";

            if (missingCount > 0 && missingUnicodes != null && missingCount <= 50)
            {
                message += "\n\nMissing characters:\n";
                foreach (uint unicode in missingUnicodes)
                {
                    char c = (char)unicode;
                    message += $"U+{unicode:X4} ";
                    if (!char.IsControl(c))
                        message += $"'{c}' ";
                }
            }
            else if (missingCount > 50)
            {
                message += $"\n\n(Too many missing characters to display)";
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

    private void ShowUnicodeRangesReference()
    {
        string reference =
            "=== Unicode Ranges Reference ===\n\n" +
            "BASIC CHARACTERS:\n" +
            "  0020-007E  Basic Latin (ASCII printable)\n" +
            "  00A0-00FF  Latin-1 Supplement\n\n" +
            "LATIN EXTENDED:\n" +
            "  0100-017F  Latin Extended-A\n" +
            "  0180-024F  Latin Extended-B\n" +
            "  1E00-1EFF  Latin Extended Additional\n\n" +
            "VIETNAMESE:\n" +
            "  1EA0-1EFF  Vietnamese characters\n" +
            "  0300-036F  Combining Diacritical Marks\n\n" +
            "SYMBOLS:\n" +
            "  2000-206F  General Punctuation\n" +
            "  20A0-20CF  Currency Symbols (₫, €, £, ¥)\n" +
            "  2100-214F  Letterlike Symbols\n" +
            "  2150-218F  Number Forms (fractions)\n\n" +
            "TECHNICAL:\n" +
            "  2190-21FF  Arrows\n" +
            "  2200-22FF  Mathematical Operators\n" +
            "  2500-257F  Box Drawing\n" +
            "  25A0-25FF  Geometric Shapes\n\n" +
            "CJK:\n" +
            "  3000-303F  CJK Symbols and Punctuation\n" +
            "  FF00-FFEF  Halfwidth and Fullwidth Forms\n\n" +
            "ICONS:\n" +
            "  E000-F8FF  Private Use Area (custom icons)";

        EditorUtility.DisplayDialog("Unicode Ranges Reference", reference, "OK");
    }

    /// <summary>
    /// Get Vietnamese Unicode ranges string.
    /// </summary>
    public static string GetVietnameseUnicodeRanges()
    {
        return PresetRanges[PresetType.VietnameseFull];
    }
}
