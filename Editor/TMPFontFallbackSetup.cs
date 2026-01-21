using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Editor tool to setup TMP Font Fallback system for multi-language support.
/// Enables display of file names in all languages like the operating system.
///
/// Font Fallback: When a character is not found in the primary font,
/// TMP automatically searches fallback fonts in order.
/// </summary>
public class TMPFontFallbackSetup : EditorWindow
{
    private TMP_FontAsset primaryFont;
    private List<TMP_FontAsset> fallbackFonts = new List<TMP_FontAsset>();
    private Vector2 scrollPosition;

    // Recommended Noto Sans font variants for full Unicode coverage
    private static readonly string[] RecommendedFonts = new string[]
    {
        "NotoSans-Regular",           // Latin, Vietnamese, Cyrillic, Greek
        "NotoSansCJKsc-Regular",      // Chinese Simplified
        "NotoSansCJKtc-Regular",      // Chinese Traditional
        "NotoSansCJKjp-Regular",      // Japanese
        "NotoSansCJKkr-Regular",      // Korean
        "NotoSansArabic-Regular",     // Arabic
        "NotoSansThai-Regular",       // Thai
        "NotoSansHebrew-Regular",     // Hebrew
        "NotoSansDevanagari-Regular", // Hindi
        "NotoSansSymbols-Regular",    // Symbols & Emoji
    };

    // Unicode ranges for each language group
    private static readonly Dictionary<string, string> LanguageRanges = new Dictionary<string, string>
    {
        { "Latin + Vietnamese", "0020-007E\n00A0-00FF\n0100-024F\n1E00-1EFF\n0300-036F" },
        { "Cyrillic (Russian)", "0400-04FF\n0500-052F" },
        { "Greek", "0370-03FF\n1F00-1FFF" },
        { "Arabic", "0600-06FF\n0750-077F\nFB50-FDFF\nFE70-FEFF" },
        { "Hebrew", "0590-05FF\nFB00-FB4F" },
        { "Thai", "0E00-0E7F" },
        { "Devanagari (Hindi)", "0900-097F\nA8E0-A8FF" },
        { "CJK Common", "2E80-2EFF\n3000-303F\n31C0-31EF\nFE30-FE4F" },
        { "CJK Unified (Chinese)", "4E00-9FFF\n3400-4DBF" },
        { "Hiragana (Japanese)", "3040-309F" },
        { "Katakana (Japanese)", "30A0-30FF\n31F0-31FF" },
        { "Hangul (Korean)", "AC00-D7AF\n1100-11FF\n3130-318F" },
        { "Symbols & Punctuation", "2000-206F\n2070-209F\n20A0-20CF\n2100-214F\n2190-21FF\n2200-22FF\n25A0-25FF\n2600-26FF\n2700-27BF" },
    };

    [MenuItem("Tools/TMP Font Fallback Setup")]
    public static void ShowWindow()
    {
        var window = GetWindow<TMPFontFallbackSetup>("Font Fallback Setup");
        window.minSize = new Vector2(500, 600);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("TMP Font Fallback Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Thiết lập Font Fallback để hiển thị đa ngôn ngữ như hệ điều hành.\n" +
            "Khi ký tự không có trong font chính, TMP sẽ tự động tìm trong các font phụ.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Primary font
        EditorGUILayout.LabelField("Primary Font (Font chính)", EditorStyles.boldLabel);
        primaryFont = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP Font Asset", primaryFont, typeof(TMP_FontAsset), false);

        EditorGUILayout.Space(10);

        // Fallback fonts list
        EditorGUILayout.LabelField($"Fallback Fonts ({fallbackFonts.Count})", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

        for (int i = 0; i < fallbackFonts.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(25));
            fallbackFonts[i] = (TMP_FontAsset)EditorGUILayout.ObjectField(fallbackFonts[i], typeof(TMP_FontAsset), false);

            if (GUILayout.Button("▲", GUILayout.Width(25)) && i > 0)
            {
                var temp = fallbackFonts[i];
                fallbackFonts[i] = fallbackFonts[i - 1];
                fallbackFonts[i - 1] = temp;
            }

            if (GUILayout.Button("▼", GUILayout.Width(25)) && i < fallbackFonts.Count - 1)
            {
                var temp = fallbackFonts[i];
                fallbackFonts[i] = fallbackFonts[i + 1];
                fallbackFonts[i + 1] = temp;
            }

            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                fallbackFonts.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        // Add fallback button
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ Add Fallback Font"))
        {
            fallbackFonts.Add(null);
        }

        if (GUILayout.Button("Auto-Find Fonts"))
        {
            AutoFindFallbackFonts();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Apply button
        EditorGUI.BeginDisabledGroup(primaryFont == null);

        if (GUILayout.Button("Apply Fallback Chain", GUILayout.Height(35)))
        {
            ApplyFallbackChain();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(15);

        // Language coverage reference
        DrawLanguageCoverageSection();

        EditorGUILayout.Space(10);

        // Download fonts section
        DrawDownloadSection();
    }

    private void DrawLanguageCoverageSection()
    {
        EditorGUILayout.LabelField("Language Unicode Ranges", EditorStyles.boldLabel);

        if (GUILayout.Button("Show Language Ranges Reference"))
        {
            string info = "=== Unicode Ranges by Language ===\n\n";

            foreach (var kvp in LanguageRanges)
            {
                info += $"{kvp.Key}:\n{kvp.Value}\n\n";
            }

            EditorUtility.DisplayDialog("Language Unicode Ranges", info, "OK");
        }
    }

    private void DrawDownloadSection()
    {
        EditorGUILayout.LabelField("Download Noto Fonts", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Để hỗ trợ đa ngôn ngữ, tải Google Noto Fonts:\n" +
            "• NotoSans - Latin, Vietnamese, Cyrillic\n" +
            "• NotoSansCJK - Chinese, Japanese, Korean\n" +
            "• NotoSansArabic, NotoSansThai, etc.",
            MessageType.Info);

        if (GUILayout.Button("Open Google Noto Fonts Website"))
        {
            Application.OpenURL("https://fonts.google.com/noto");
        }

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Quick Setup: Latin + CJK (Recommended)"))
        {
            ShowQuickSetupGuide();
        }
    }

    private void AutoFindFallbackFonts()
    {
        fallbackFonts.Clear();

        // Find all TMP Font Assets in project
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

            if (font != null && font != primaryFont)
            {
                // Prioritize Noto fonts
                string fontName = font.name.ToLower();
                if (fontName.Contains("noto") || fontName.Contains("cjk") ||
                    fontName.Contains("arabic") || fontName.Contains("thai") ||
                    fontName.Contains("fallback"))
                {
                    fallbackFonts.Add(font);
                }
            }
        }

        // Sort by name for consistent ordering
        fallbackFonts.Sort((a, b) => a.name.CompareTo(b.name));

        Debug.Log($"Found {fallbackFonts.Count} potential fallback fonts.");
    }

    private void ApplyFallbackChain()
    {
        if (primaryFont == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a primary font.", "OK");
            return;
        }

        // Clear existing fallbacks
        primaryFont.fallbackFontAssetTable.Clear();

        // Add new fallbacks
        int addedCount = 0;
        foreach (var fallback in fallbackFonts)
        {
            if (fallback != null && fallback != primaryFont)
            {
                primaryFont.fallbackFontAssetTable.Add(fallback);
                addedCount++;
            }
        }

        // Mark as dirty and save
        EditorUtility.SetDirty(primaryFont);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Success",
            $"Applied {addedCount} fallback fonts to '{primaryFont.name}'.\n\n" +
            "Font fallback chain:\n" +
            $"1. {primaryFont.name} (Primary)\n" +
            string.Join("\n", GetFallbackChainList()),
            "OK");
    }

    private List<string> GetFallbackChainList()
    {
        var list = new List<string>();
        int index = 2;
        foreach (var fallback in fallbackFonts)
        {
            if (fallback != null)
            {
                list.Add($"{index}. {fallback.name}");
                index++;
            }
        }
        return list;
    }

    private void ShowQuickSetupGuide()
    {
        string guide =
            "=== Quick Setup Guide ===\n\n" +
            "1. TẢI FONTS:\n" +
            "   • NotoSans-Regular.ttf (Latin + Vietnamese)\n" +
            "   • NotoSansCJKsc-Regular.otf (Chinese + Japanese + Korean)\n" +
            "   Từ: https://fonts.google.com/noto\n\n" +
            "2. IMPORT VÀO UNITY:\n" +
            "   • Kéo thả .ttf/.otf vào Assets/Fonts/\n\n" +
            "3. TẠO TMP FONT ASSETS:\n" +
            "   • Tools > TMP Font Generator\n" +
            "   • Chọn NotoSans → Preset: VietnameseFull\n" +
            "   • Chọn NotoSansCJK → Preset: Custom với ranges:\n" +
            "     4E00-9FFF (CJK Unified)\n" +
            "     3040-309F (Hiragana)\n" +
            "     30A0-30FF (Katakana)\n" +
            "     AC00-D7AF (Hangul)\n\n" +
            "4. THIẾT LẬP FALLBACK:\n" +
            "   • Primary: NotoSans_TMP\n" +
            "   • Fallback 1: NotoSansCJK_TMP\n" +
            "   • Apply Fallback Chain\n\n" +
            "5. SỬ DỤNG:\n" +
            "   • Gán NotoSans_TMP cho TextMeshPro\n" +
            "   • Tự động fallback khi gặp ký tự CJK";

        EditorUtility.DisplayDialog("Quick Setup Guide", guide, "OK");
    }

    /// <summary>
    /// Programmatically setup fallback chain.
    /// </summary>
    public static void SetupFallbackChain(TMP_FontAsset primary, params TMP_FontAsset[] fallbacks)
    {
        if (primary == null) return;

        primary.fallbackFontAssetTable.Clear();

        foreach (var fallback in fallbacks)
        {
            if (fallback != null && fallback != primary)
            {
                primary.fallbackFontAssetTable.Add(fallback);
            }
        }

        EditorUtility.SetDirty(primary);
        AssetDatabase.SaveAssets();
    }
}
