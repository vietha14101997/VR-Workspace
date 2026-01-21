using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;

/// <summary>
/// Automatically builds TMP Font Assets with optimal fallback chains
/// and Font Weights setup for font families.
/// </summary>
public class TMPFontAutoBuilder : EditorWindow
{
    private string sourceFontFolder = "Assets/VR-Workspace/Fonts/NotoSans";
    private string outputFolder = "Assets/VR-Workspace/Resources/TMP";
    private int atlasResolution = 2048;
    private int fallbackAtlasResolution = 1024;  // Smaller for fallback
    private int samplingPointSize = 90;
    private int atlasPadding = 5;

    private List<FontAnalysis> analyzedFonts = new List<FontAnalysis>();
    private Vector2 scrollPosition;
    private bool analyzed = false;
    private int primaryFontIndex = -1;
    private bool setupFontWeights = true;
    private bool lightweightMode = true;  // Default to lightweight
    private bool deleteUnusedFonts = false;  // Option to delete unused source fonts after build

    // Font weight detection
    private enum FontWeight
    {
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Regular = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        Heavy = 800,
        Black = 900
    }

    private class FontAnalysis
    {
        public Font Font;
        public string Name;
        public string BaseName; // Family name without weight/italic
        public FontWeight Weight = FontWeight.Regular;
        public bool IsItalic = false;
        public Dictionary<string, float> LanguageCoverage = new Dictionary<string, float>();
        public bool Selected = true;
        public bool IsPrimary = false;
        public string TMPAssetPath;

        public float GetScore()
        {
            float score = 0;
            if (LanguageCoverage.TryGetValue("Latin", out float latin)) score += latin * 2;
            if (LanguageCoverage.TryGetValue("VN", out float viet)) score += viet * 3;
            if (LanguageCoverage.TryGetValue("JP", out float jp)) score += jp;
            if (LanguageCoverage.TryGetValue("KR", out float kr)) score += kr;
            if (LanguageCoverage.TryGetValue("CN", out float cn)) score += cn;
            return score;
        }
    }

    private static readonly Dictionary<string, string> LanguageTestChars = new Dictionary<string, string>
    {
        { "Latin", " !#$%&()*+-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_abcdefghijklmnopqrstuvwxyz{|}~" },
        { "VN", "ÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚÝàáâãèéêìíòóôõùúýĂăĐđĨĩŨũƠơƯưẠạẢảẤấẦầẨẩẪẫẬậẮắẰằẲẳẴẵẶặẸẹẺẻẼẽẾếỀềỂểỄễỆệỈỉỊịỌọỎỏỐốỒồỔổỖỗỘộỚớỜờỞởỠỡỢợỤụỦủỨứỪừỬửỮữỰựỲỳỴỵỶỷỸỹ" },
        { "JP", "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン" },
        { "KR", "가각간갈감갑강개객갱갸걀거걱건걸검겁게겨격견결겸겹경계고곡곤골곰곱공과곽관광괘괴굉교구국군굴굼굽궁권궐궤귀규균귤그극근글금급긍기긴길김깁깅" },
        { "CN", "的一是不了在人有我他这个们中来上大为和国地到以说时要就出会可也你对生能而子那得于着下自之年过发后作里用道行所然家种事成方多经么去法学如都同现当没动面起看定天分还进好小部其些主样理心她本前开但因只从想实日军者意无力它与长把机十民" },
        { "TW", "的一是不了在人有我他這個們中來上大為和國地到以說時要就出會可也你對生能而子那得於著下自之年過發後作裡用道行所然家種事成方多經麼去法學如都同現當沒動面起看定天分還進好小部其些主樣理心她本前開但因只從想實日軍者意無力它與長把機十民" },
        { "Cyrillic", "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщъыьэюя" },
        { "Greek", "ΆΈΉΊΌΎΏΐΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩαβγδεζηθικλμνξοπρστυφχψω" },
        { "Thai", "กขฃคฅฆงจฉชซฌญฎฏฐฑฒณดตถทธนบปผฝพฟภมยรลวศษสหฬอฮ" },
        { "Arabic", "ءآأؤإئابةتثجحخدذرزسشصضطظعغفقكلمنهوىي" },
        { "Hebrew", "אבגדהוזחטיכלמנסעפצקרשת" },
        { "Currency", "₠₡₢₣₤₥₦₧₨₩₪₫€₭₮₯₰₱₲₳₴₵₸₹₺₻₼₽₾₿$¥£" },
    };

    // Weight detection patterns
    private static readonly Dictionary<string, FontWeight> WeightPatterns = new Dictionary<string, FontWeight>
    {
        { "thin", FontWeight.Thin },
        { "hairline", FontWeight.Thin },
        { "extralight", FontWeight.ExtraLight },
        { "extra-light", FontWeight.ExtraLight },
        { "ultralight", FontWeight.ExtraLight },
        { "light", FontWeight.Light },
        { "regular", FontWeight.Regular },
        { "normal", FontWeight.Regular },
        { "reg", FontWeight.Regular },
        { "medium", FontWeight.Medium },
        { "med", FontWeight.Medium },
        { "semibold", FontWeight.SemiBold },
        { "semi-bold", FontWeight.SemiBold },
        { "demibold", FontWeight.SemiBold },
        { "bold", FontWeight.Bold },
        { "bld", FontWeight.Bold },
        { "heavy", FontWeight.Heavy },
        { "extrabold", FontWeight.Heavy },
        { "extra-bold", FontWeight.Heavy },
        { "black", FontWeight.Black },
        { "blk", FontWeight.Black },
        { "ultra", FontWeight.Black },
    };

    [MenuItem("Tools/TMP Font Auto Builder")]
    public static void ShowWindow()
    {
        var window = GetWindow<TMPFontAutoBuilder>("TMP Auto Builder");
        window.minSize = new Vector2(900, 550);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("TMP Font Auto Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tự động tạo TMP Font Assets với Fallback Chain và Font Weights.\n" +
            "1. Analyze  2. Chọn Primary  3. Build",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Folders
        EditorGUILayout.BeginHorizontal();
        sourceFontFolder = EditorGUILayout.TextField("Source Fonts", sourceFontFolder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string sel = EditorUtility.OpenFolderPanel("Select Font Folder", "Assets", "");
            if (!string.IsNullOrEmpty(sel) && sel.StartsWith(Application.dataPath))
            {
                sourceFontFolder = "Assets" + sel.Substring(Application.dataPath.Length);
                analyzed = false;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Output TMP", outputFolder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string sel = EditorUtility.OpenFolderPanel("Select Output", "Assets", "");
            if (!string.IsNullOrEmpty(sel) && sel.StartsWith(Application.dataPath))
            {
                outputFolder = "Assets" + sel.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // Settings
        atlasResolution = EditorGUILayout.IntPopup("Primary Atlas", atlasResolution,
            new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
        fallbackAtlasResolution = EditorGUILayout.IntPopup("Fallback Atlas", fallbackAtlasResolution,
            new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
        samplingPointSize = EditorGUILayout.IntSlider("Sampling Size", samplingPointSize, 30, 150);
        setupFontWeights = EditorGUILayout.Toggle("Setup Font Weights", setupFontWeights);

        EditorGUILayout.Space(5);

        // Lightweight mode section
        EditorGUILayout.BeginVertical("box");
        lightweightMode = EditorGUILayout.Toggle("Lightweight Mode", lightweightMode);
        if (lightweightMode)
        {
            EditorGUILayout.HelpBox(
                "Tự động chọn bộ font tối thiểu để hỗ trợ tất cả ngôn ngữ.\n" +
                "Fallback sử dụng atlas nhỏ hơn để giảm dung lượng.",
                MessageType.Info);
        }
        EditorGUILayout.EndVertical();

        // Delete unused fonts option
        EditorGUILayout.BeginVertical("box");
        deleteUnusedFonts = EditorGUILayout.Toggle("Delete Unused Fonts", deleteUnusedFonts);
        if (deleteUnusedFonts)
        {
            EditorGUILayout.HelpBox(
                "⚠ Sau khi build, xóa các file font (.ttf/.otf) không được chọn.\n" +
                "Giảm dung lượng project nhưng KHÔNG THỂ HOÀN TÁC!",
                MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Analyze Fonts", GUILayout.Height(30)))
        {
            AnalyzeFonts();
        }

        EditorGUILayout.Space(10);

        // Results
        if (analyzed && analyzedFonts.Count > 0)
        {
            EditorGUILayout.LabelField($"Found {analyzedFonts.Count} Fonts", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(280));

            for (int i = 0; i < analyzedFonts.Count; i++)
            {
                DrawFontRow(analyzedFonts[i], i);
            }

            EditorGUILayout.EndScrollView();

            // Recommendation
            DrawRecommendation();

            EditorGUILayout.Space(5);

            // Auto Select and Size Estimation
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Select Optimal", GUILayout.Height(25)))
            {
                AutoSelectOptimalFonts();
            }
            if (GUILayout.Button("Deselect All", GUILayout.Height(25), GUILayout.Width(100)))
            {
                foreach (var f in analyzedFonts) f.Selected = false;
            }
            if (GUILayout.Button("Select All", GUILayout.Height(25), GUILayout.Width(100)))
            {
                foreach (var f in analyzedFonts) f.Selected = true;
            }
            EditorGUILayout.EndHorizontal();

            // Estimated size
            DrawEstimatedSize();

            EditorGUILayout.Space(10);

            // Build buttons
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = primaryFontIndex >= 0;
            if (GUILayout.Button("Build with Fallback + Weights", GUILayout.Height(30)))
            {
                BuildWithFallbackAndWeights();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Build Selected Only", GUILayout.Height(30)))
            {
                BuildSelected();
            }

            EditorGUILayout.EndHorizontal();

            if (primaryFontIndex < 0)
            {
                EditorGUILayout.HelpBox("Select a Primary font (radio button) to enable full build", MessageType.Warning);
            }
        }
    }

    private void DrawFontRow(FontAnalysis font, int index)
    {
        EditorGUILayout.BeginHorizontal("box");

        // Checkbox
        font.Selected = EditorGUILayout.Toggle(font.Selected, GUILayout.Width(18));

        // Radio for primary
        bool wasPrimary = primaryFontIndex == index;
        bool isPrimary = EditorGUILayout.Toggle(wasPrimary, EditorStyles.radioButton, GUILayout.Width(18));
        if (isPrimary && !wasPrimary)
        {
            primaryFontIndex = index;
            for (int j = 0; j < analyzedFonts.Count; j++)
                analyzedFonts[j].IsPrimary = (j == index);
        }

        // Name with weight info
        string weightStr = font.Weight.ToString();
        string italicStr = font.IsItalic ? " Italic" : "";
        string label = font.IsPrimary ? $"★ {font.Name}" : font.Name;
        EditorGUILayout.LabelField(label, font.IsPrimary ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(160));

        // Weight badge
        var weightStyle = new GUIStyle(EditorStyles.miniLabel);
        weightStyle.normal.textColor = GetWeightColor(font.Weight);
        EditorGUILayout.LabelField($"[{(int)font.Weight}{italicStr}]", weightStyle, GUILayout.Width(70));

        // Coverage - show all languages
        DrawCoverage("Lat", font.LanguageCoverage.GetValueOrDefault("Latin", 0));
        DrawCoverage("VN", font.LanguageCoverage.GetValueOrDefault("VN", 0));
        DrawCoverage("JP", font.LanguageCoverage.GetValueOrDefault("JP", 0));
        DrawCoverage("KR", font.LanguageCoverage.GetValueOrDefault("KR", 0));
        DrawCoverage("CN", font.LanguageCoverage.GetValueOrDefault("CN", 0));
        DrawCoverage("TW", font.LanguageCoverage.GetValueOrDefault("TW", 0));
        DrawCoverage("Cyr", font.LanguageCoverage.GetValueOrDefault("Cyrillic", 0));
        DrawCoverage("Grk", font.LanguageCoverage.GetValueOrDefault("Greek", 0));
        DrawCoverage("Thai", font.LanguageCoverage.GetValueOrDefault("Thai", 0));
        DrawCoverage("Ar", font.LanguageCoverage.GetValueOrDefault("Arabic", 0));
        DrawCoverage("Heb", font.LanguageCoverage.GetValueOrDefault("Hebrew", 0));

        EditorGUILayout.EndHorizontal();
    }

    private Color GetWeightColor(FontWeight weight)
    {
        switch (weight)
        {
            case FontWeight.Thin:
            case FontWeight.ExtraLight:
            case FontWeight.Light:
                return new Color(0.6f, 0.8f, 1f);
            case FontWeight.Regular:
                return Color.white;
            case FontWeight.Medium:
                return new Color(1f, 0.9f, 0.6f);
            case FontWeight.SemiBold:
            case FontWeight.Bold:
                return new Color(1f, 0.7f, 0.4f);
            case FontWeight.Heavy:
            case FontWeight.Black:
                return new Color(1f, 0.5f, 0.3f);
            default:
                return Color.white;
        }
    }

    private void DrawCoverage(string label, float coverage)
    {
        string icon = coverage >= 95 ? "✓" : coverage >= 50 ? "◐" : coverage > 0 ? "◔" : "✗";
        Color c = coverage >= 95 ? Color.green : coverage >= 50 ? Color.yellow : coverage > 0 ? new Color(1f, 0.5f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = c }, fontSize = 10 };
        EditorGUILayout.LabelField($"{icon}{label}", style, GUILayout.Width(32));
    }

    private void DrawRecommendation()
    {
        var best = analyzedFonts
            .Where(f => f.LanguageCoverage.GetValueOrDefault("VN", 0) >= 90 && f.Weight == FontWeight.Regular && !f.IsItalic)
            .OrderByDescending(f => f.GetScore())
            .FirstOrDefault();

        EditorGUILayout.Space(5);

        if (best != null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Recommended Primary: {best.Name}", EditorStyles.boldLabel);
            if (GUILayout.Button("Set", GUILayout.Width(50)))
            {
                primaryFontIndex = analyzedFonts.IndexOf(best);
                for (int j = 0; j < analyzedFonts.Count; j++)
                    analyzedFonts[j].IsPrimary = (j == primaryFontIndex);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void AnalyzeFonts()
    {
        analyzedFonts.Clear();
        primaryFontIndex = -1;

        string[] guids = AssetDatabase.FindAssets("t:Font", new[] { sourceFontFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(path);

            if (font != null)
            {
                var analysis = new FontAnalysis { Font = font, Name = font.name };

                // Detect weight and italic
                DetectWeightAndStyle(analysis);

                // Analyze language coverage using accurate glyph check
                foreach (var kvp in LanguageTestChars)
                {
                    int supported = CountSupportedChars(font, kvp.Value);
                    analysis.LanguageCoverage[kvp.Key] = (supported * 100f) / kvp.Value.Length;
                }

                analyzedFonts.Add(analysis);
            }
        }

        // Sort: Regular first, then by weight, non-italic before italic
        analyzedFonts = analyzedFonts
            .OrderBy(f => f.Weight == FontWeight.Regular ? 0 : 1)
            .ThenBy(f => (int)f.Weight)
            .ThenBy(f => f.IsItalic ? 1 : 0)
            .ToList();

        analyzed = true;
        Debug.Log($"Analyzed {analyzedFonts.Count} fonts.");
    }

    /// <summary>
    /// Accurately count supported characters using TMP FontEngine.
    /// Unity's Font.HasCharacter() and GetCharacterInfo() are unreliable
    /// because they may use OS fallback fonts.
    /// FontEngine reads directly from the font file for accurate results.
    /// </summary>
    private int CountSupportedChars(Font font, string testChars)
    {
        // Initialize FontEngine if not already initialized
        UnityEngine.TextCore.LowLevel.FontEngine.InitializeFontEngine();

        // Load font using FontEngine - use Font object overload for reliable path handling
        var loadResult = UnityEngine.TextCore.LowLevel.FontEngine.LoadFontFace(font);
        if (loadResult != UnityEngine.TextCore.LowLevel.FontEngineError.Success)
        {
            Debug.LogWarning($"Failed to load font face for {font.name}: {loadResult}");
            return 0;
        }

        int count = 0;
        foreach (char c in testChars)
        {
            // TryGetGlyphIndex returns false if character is not in font
            if (UnityEngine.TextCore.LowLevel.FontEngine.TryGetGlyphIndex((uint)c, out uint glyphIndex) && glyphIndex != 0)
            {
                count++;
            }
        }

        return count;
    }

    private void DetectWeightAndStyle(FontAnalysis analysis)
    {
        string nameLower = analysis.Name.ToLower();

        // Detect italic
        analysis.IsItalic = nameLower.Contains("italic") || nameLower.Contains("_it") ||
                           nameLower.Contains("-it") || nameLower.EndsWith("it") ||
                           nameLower.Contains("oblique");

        // Remove italic suffix for weight detection
        string nameForWeight = Regex.Replace(nameLower, @"(italic|_it|-it|oblique)", "", RegexOptions.IgnoreCase);

        // Detect weight
        analysis.Weight = FontWeight.Regular; // Default

        foreach (var pattern in WeightPatterns.OrderByDescending(p => p.Key.Length))
        {
            if (nameForWeight.Contains(pattern.Key))
            {
                analysis.Weight = pattern.Value;
                break;
            }
        }

        // Extract base name (family name without weight/italic)
        string baseName = analysis.Name;
        foreach (var pattern in WeightPatterns.Keys.OrderByDescending(k => k.Length))
        {
            baseName = Regex.Replace(baseName, $"[_-]?{pattern}[_-]?", "", RegexOptions.IgnoreCase);
        }
        baseName = Regex.Replace(baseName, @"[_-]?(italic|it|oblique)[_-]?", "", RegexOptions.IgnoreCase);
        baseName = Regex.Replace(baseName, @"[_-]+$", ""); // Remove trailing separators
        analysis.BaseName = baseName;
    }

    private void BuildWithFallbackAndWeights()
    {
        if (primaryFontIndex < 0 || primaryFontIndex >= analyzedFonts.Count)
        {
            EditorUtility.DisplayDialog("Error", "Select a Primary font first.", "OK");
            return;
        }

        EnsureOutputFolder();

        var primary = analyzedFonts[primaryFontIndex];
        var allSelected = analyzedFonts.Where(f => f.Selected).ToList();
        var fallbacks = allSelected.Where(f => f != primary).ToList();

        try
        {
            // Build all selected fonts
            float progress = 0;
            float step = 0.8f / allSelected.Count;

            foreach (var font in allSelected)
            {
                bool isPrimary = (font == primary);
                EditorUtility.DisplayProgressBar("Building", $"Creating {font.Name}... ({(isPrimary ? atlasResolution : fallbackAtlasResolution)}px)", progress);
                string suffix = isPrimary ? "_TMP_Primary" : "_TMP";
                font.TMPAssetPath = CreateTMPAsset(font.Font, suffix, isPrimary);
                progress += step;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Setup primary font
            EditorUtility.DisplayProgressBar("Building", "Setting up Primary font...", 0.9f);

            var primaryAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(primary.TMPAssetPath);
            if (primaryAsset != null)
            {
                // Setup fallback chain
                SetupFallbackChain(primaryAsset, fallbacks);

                // Setup font weights
                if (setupFontWeights)
                {
                    SetupFontWeightsForAsset(primaryAsset, allSelected);
                }

                EditorUtility.SetDirty(primaryAsset);
                AssetDatabase.SaveAssets();

                Selection.activeObject = primaryAsset;
                EditorGUIUtility.PingObject(primaryAsset);
            }

            EditorUtility.ClearProgressBar();

            // Report
            string msg = $"Created {allSelected.Count} TMP Font Assets\n\n" +
                        $"★ Primary: {primary.Name} ({atlasResolution}px)\n" +
                        $"   Fallbacks: {fallbacks.Count} ({fallbackAtlasResolution}px each)\n";

            if (setupFontWeights)
            {
                var weightsSetup = allSelected.Where(f => f.BaseName == primary.BaseName).ToList();
                msg += $"   Font Weights: {weightsSetup.Count} variants\n";
            }

            // Estimate final size
            float primaryMB = EstimateAtlasSizeMB(atlasResolution);
            float fallbackMB = EstimateAtlasSizeMB(fallbackAtlasResolution);
            float totalMB = primaryMB + (fallbacks.Count * fallbackMB);
            msg += $"\n   Estimated Size: ~{totalMB:F1}MB";

            msg += $"\nOutput: {outputFolder}";

            // Delete unused fonts if enabled
            if (deleteUnusedFonts)
            {
                int deletedCount = DeleteUnusedSourceFonts(allSelected);
                if (deletedCount > 0)
                {
                    msg += $"\n\n🗑 Đã xóa {deletedCount} font không sử dụng";
                }
            }

            EditorUtility.DisplayDialog("Success", msg, "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Error", e.Message, "OK");
        }
    }

    private void SetupFallbackChain(TMP_FontAsset primaryAsset, List<FontAnalysis> fallbacks)
    {
        if (primaryAsset.fallbackFontAssetTable == null)
            primaryAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
        else
            primaryAsset.fallbackFontAssetTable.Clear();

        foreach (var fb in fallbacks)
        {
            if (!string.IsNullOrEmpty(fb.TMPAssetPath))
            {
                var fbAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fb.TMPAssetPath);
                if (fbAsset != null)
                    primaryAsset.fallbackFontAssetTable.Add(fbAsset);
            }
        }
    }

    private void SetupFontWeightsForAsset(TMP_FontAsset primaryAsset, List<FontAnalysis> allFonts)
    {
        var primary = analyzedFonts[primaryFontIndex];

        // Find fonts in the same family
        var familyFonts = allFonts.Where(f => f.BaseName == primary.BaseName).ToList();

        if (familyFonts.Count <= 1)
        {
            Debug.Log("No font weight variants found for the same family.");
            return;
        }

        // Access font weight table via SerializedObject
        var so = new SerializedObject(primaryAsset);
        var fontWeightsProp = so.FindProperty("m_FontWeightTable");

        if (fontWeightsProp == null || !fontWeightsProp.isArray)
        {
            Debug.LogWarning("Could not find m_FontWeightTable property.");
            return;
        }

        // Font weights array has 10 elements (index 0-9 for weights 100-900, index 0 unused or for 100)
        // Each element has regularTypeface and italicTypeface
        foreach (var font in familyFonts)
        {
            if (string.IsNullOrEmpty(font.TMPAssetPath)) continue;

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(font.TMPAssetPath);
            if (fontAsset == null) continue;

            int weightIndex = GetWeightIndex(font.Weight);

            if (weightIndex >= 0 && weightIndex < fontWeightsProp.arraySize)
            {
                var weightElement = fontWeightsProp.GetArrayElementAtIndex(weightIndex);
                var regularProp = weightElement.FindPropertyRelative("regularTypeface");
                var italicProp = weightElement.FindPropertyRelative("italicTypeface");

                if (font.IsItalic)
                {
                    if (italicProp != null)
                        italicProp.objectReferenceValue = fontAsset;
                }
                else
                {
                    if (regularProp != null)
                        regularProp.objectReferenceValue = fontAsset;
                }

                Debug.Log($"Assigned {font.Name} to weight {(int)font.Weight} {(font.IsItalic ? "Italic" : "Regular")}");
            }
        }

        so.ApplyModifiedProperties();
    }

    private int GetWeightIndex(FontWeight weight)
    {
        // TMP font weight table: index 0 = 100 (Thin), index 4 = 400 (Regular), index 7 = 700 (Bold), etc.
        return ((int)weight / 100) - 1;
    }

    private void BuildSelected()
    {
        var selected = analyzedFonts.Where(f => f.Selected).ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No fonts selected.", "OK");
            return;
        }

        EnsureOutputFolder();

        int created = 0;
        float progress = 0;
        float step = 1f / selected.Count;

        // If primary is set, use different atlas sizes
        var primary = primaryFontIndex >= 0 ? analyzedFonts[primaryFontIndex] : null;

        foreach (var font in selected)
        {
            bool isPrimary = (primary != null && font == primary);
            int resolution = isPrimary ? atlasResolution : (primary != null ? fallbackAtlasResolution : atlasResolution);
            EditorUtility.DisplayProgressBar("Building", $"Creating {font.Name}... ({resolution}px)", progress);
            string path = CreateTMPAsset(font.Font, "_TMP", isPrimary);
            if (!string.IsNullOrEmpty(path)) created++;
            progress += step;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.ClearProgressBar();

        string msg = $"Created {created} TMP assets in:\n{outputFolder}";

        // Delete unused fonts if enabled
        if (deleteUnusedFonts)
        {
            int deletedCount = DeleteUnusedSourceFonts(selected);
            if (deletedCount > 0)
            {
                msg += $"\n\n🗑 Đã xóa {deletedCount} font không sử dụng";
            }
        }

        EditorUtility.DisplayDialog("Success", msg, "OK");
    }

    private void EnsureOutputFolder()
    {
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();
        }
    }

    private string CreateTMPAsset(Font sourceFont, string suffix, bool isPrimary = true)
    {
        try
        {
            // Use different atlas resolution for primary vs fallback
            int resolution = isPrimary ? atlasResolution : fallbackAtlasResolution;

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont, samplingPointSize, atlasPadding,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                resolution, resolution,
                AtlasPopulationMode.Dynamic
            );

            if (fontAsset == null) return null;

            // Dynamic mode: Characters will be added on-demand at runtime
            // No need to pre-populate atlas - this avoids long build times for large character sets (Arabic, CJK)

            // Create subfolder using source folder name
            string sourceFolderName = Path.GetFileName(sourceFontFolder);
            string fontFolder = $"{outputFolder}/{sourceFolderName}";
            if (!Directory.Exists(fontFolder))
            {
                Directory.CreateDirectory(fontFolder);
                AssetDatabase.Refresh();
            }

            string assetPath = $"{fontFolder}/{sourceFont.name}{suffix}.asset";

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CreateAsset(fontAsset, assetPath);

            if (fontAsset.atlasTexture != null)
            {
                fontAsset.atlasTexture.name = $"{sourceFont.name}_Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = $"{sourceFont.name}_Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            return assetPath;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create TMP for {sourceFont.name}: {e.Message}");
            return null;
        }
    }

    #region Delete Unused Fonts

    /// <summary>
    /// Xóa các file font source (.ttf/.otf) không được sử dụng trong build.
    /// </summary>
    private int DeleteUnusedSourceFonts(List<FontAnalysis> usedFonts)
    {
        // Get list of used font paths
        var usedFontPaths = new HashSet<string>();
        foreach (var font in usedFonts)
        {
            if (font.Font != null)
            {
                string path = AssetDatabase.GetAssetPath(font.Font);
                if (!string.IsNullOrEmpty(path))
                {
                    usedFontPaths.Add(path.ToLower());
                }
            }
        }

        // Find all font files in source folder
        var allFontGuids = AssetDatabase.FindAssets("t:Font", new[] { sourceFontFolder });
        var unusedFonts = new List<string>();

        foreach (string guid in allFontGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!usedFontPaths.Contains(path.ToLower()))
            {
                unusedFonts.Add(path);
            }
        }

        if (unusedFonts.Count == 0)
        {
            Debug.Log("[Font Cleanup] Không có font nào cần xóa.");
            return 0;
        }

        // Confirm deletion
        string fontList = string.Join("\n", unusedFonts.Take(10).Select(p => "  • " + Path.GetFileName(p)));
        if (unusedFonts.Count > 10)
        {
            fontList += $"\n  ... và {unusedFonts.Count - 10} font khác";
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Xóa Font Không Sử Dụng",
            $"Sẽ xóa {unusedFonts.Count} font không được chọn:\n\n{fontList}\n\n⚠ KHÔNG THỂ HOÀN TÁC!",
            "Xóa",
            "Hủy"
        );

        if (!confirm)
        {
            Debug.Log("[Font Cleanup] Đã hủy xóa font.");
            return 0;
        }

        // Delete unused fonts
        int deletedCount = 0;
        foreach (string path in unusedFonts)
        {
            if (AssetDatabase.DeleteAsset(path))
            {
                Debug.Log($"[Font Cleanup] Đã xóa: {path}");
                deletedCount++;
            }
            else
            {
                Debug.LogWarning($"[Font Cleanup] Không thể xóa: {path}");
            }
        }

        AssetDatabase.Refresh();

        Debug.Log($"[Font Cleanup] Đã xóa {deletedCount}/{unusedFonts.Count} font không sử dụng.");
        return deletedCount;
    }

    #endregion

    #region Lightweight Mode - Auto Select Optimal Fonts

    /// <summary>
    /// Tự động chọn bộ font tối thiểu để cover tất cả ngôn ngữ.
    /// Ưu tiên:
    /// 1. Primary font (Regular, VN support)
    /// 2. Mỗi ngôn ngữ chỉ cần 1 font cover tốt nhất
    /// </summary>
    private void AutoSelectOptimalFonts()
    {
        // Deselect all first
        foreach (var f in analyzedFonts) f.Selected = false;

        var requiredLanguages = new HashSet<string> { "Latin", "VN", "JP", "KR", "CN", "TW", "Cyrillic", "Greek", "Thai", "Arabic", "Hebrew" };
        var coveredLanguages = new HashSet<string>();
        var selectedFonts = new List<FontAnalysis>();

        // Step 1: Find best Primary font (Regular weight, VN support)
        var bestPrimary = analyzedFonts
            .Where(f => f.LanguageCoverage.GetValueOrDefault("Latin", 0) >= 90 &&
                       f.LanguageCoverage.GetValueOrDefault("VN", 0) >= 90 &&
                       f.Weight == FontWeight.Regular && !f.IsItalic)
            .OrderByDescending(f => f.GetScore())
            .FirstOrDefault();

        if (bestPrimary != null)
        {
            bestPrimary.Selected = true;
            selectedFonts.Add(bestPrimary);
            primaryFontIndex = analyzedFonts.IndexOf(bestPrimary);
            for (int j = 0; j < analyzedFonts.Count; j++)
                analyzedFonts[j].IsPrimary = (j == primaryFontIndex);

            // Mark covered languages
            foreach (var lang in requiredLanguages.ToList())
            {
                if (bestPrimary.LanguageCoverage.GetValueOrDefault(lang, 0) >= 80)
                    coveredLanguages.Add(lang);
            }
        }

        // Step 2: For each uncovered language, find the smallest font that covers it well
        foreach (var lang in requiredLanguages)
        {
            if (coveredLanguages.Contains(lang)) continue;

            // Find font that covers this language best
            var bestForLang = analyzedFonts
                .Where(f => !f.Selected && f.LanguageCoverage.GetValueOrDefault(lang, 0) >= 80)
                .OrderByDescending(f => f.LanguageCoverage.GetValueOrDefault(lang, 0))
                .ThenBy(f => CountCoveredLanguages(f)) // Prefer specialized fonts (fewer total languages = likely smaller)
                .FirstOrDefault();

            if (bestForLang != null)
            {
                bestForLang.Selected = true;
                selectedFonts.Add(bestForLang);

                // Mark all languages this font covers
                foreach (var otherLang in requiredLanguages)
                {
                    if (bestForLang.LanguageCoverage.GetValueOrDefault(otherLang, 0) >= 80)
                        coveredLanguages.Add(otherLang);
                }
            }
        }

        // Step 3: If setupFontWeights is enabled, add Bold variant of primary
        if (setupFontWeights && bestPrimary != null)
        {
            var boldVariant = analyzedFonts
                .Where(f => !f.Selected &&
                           f.BaseName == bestPrimary.BaseName &&
                           f.Weight == FontWeight.Bold &&
                           !f.IsItalic)
                .FirstOrDefault();

            if (boldVariant != null)
            {
                boldVariant.Selected = true;
                selectedFonts.Add(boldVariant);
            }
        }

        // Report
        Debug.Log($"[Auto Select] Selected {selectedFonts.Count} fonts:");
        foreach (var f in selectedFonts)
        {
            Debug.Log($"  - {f.Name} ({f.Weight})");
        }

        var uncovered = requiredLanguages.Except(coveredLanguages).ToList();
        if (uncovered.Count > 0)
        {
            Debug.LogWarning($"[Auto Select] Uncovered languages: {string.Join(", ", uncovered)}");
        }
    }

    private int CountCoveredLanguages(FontAnalysis font)
    {
        return font.LanguageCoverage.Count(kv => kv.Value >= 80);
    }

    /// <summary>
    /// Hiển thị ước tính kích thước.
    /// </summary>
    private void DrawEstimatedSize()
    {
        var selected = analyzedFonts.Where(f => f.Selected).ToList();
        if (selected.Count == 0) return;

        // Estimate based on atlas resolution
        // Rough estimate: Atlas texture size + overhead
        // 2048x2048 RGBA = 16MB uncompressed, ~2-4MB compressed
        // 1024x1024 RGBA = 4MB uncompressed, ~0.5-1MB compressed
        float primarySizeMB = EstimateAtlasSizeMB(atlasResolution);
        float fallbackSizeMB = EstimateAtlasSizeMB(fallbackAtlasResolution);

        int primaryCount = selected.Count(f => f.IsPrimary);
        int fallbackCount = selected.Count - primaryCount;

        // If primary not set, treat first selected as primary
        if (primaryCount == 0 && selected.Count > 0)
        {
            primaryCount = 1;
            fallbackCount = selected.Count - 1;
        }

        float totalSizeMB = (primaryCount * primarySizeMB) + (fallbackCount * fallbackSizeMB);

        // Display
        EditorGUILayout.BeginHorizontal("box");

        var infoStyle = new GUIStyle(EditorStyles.label) { richText = true };

        string sizeColor = totalSizeMB < 10 ? "green" : totalSizeMB < 30 ? "yellow" : "red";

        EditorGUILayout.LabelField(
            $"<b>Ước tính:</b> {selected.Count} fonts | " +
            $"Primary: {atlasResolution}px ({primarySizeMB:F1}MB) | " +
            $"Fallback: {fallbackAtlasResolution}px ({fallbackSizeMB:F1}MB × {fallbackCount}) | " +
            $"<color={sizeColor}><b>Tổng: ~{totalSizeMB:F1}MB</b></color>",
            infoStyle);

        EditorGUILayout.EndHorizontal();
    }

    private float EstimateAtlasSizeMB(int resolution)
    {
        // RGBA uncompressed = resolution^2 * 4 bytes
        // With Crunch compression in Unity, typically 8-15% of original
        // Estimate ~12% average compression ratio for SDF fonts
        float uncompressedBytes = resolution * resolution * 4f;
        float compressedBytes = uncompressedBytes * 0.12f;
        return compressedBytes / (1024f * 1024f);
    }

    #endregion
}
