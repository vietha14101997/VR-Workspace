using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using System.Linq;

/// <summary>
/// Editor tool to analyze font character coverage.
/// Determines which languages/character sets a font supports.
/// </summary>
public class FontCoverageAnalyzer : EditorWindow
{
    private Font selectedFont;
    private Vector2 scrollPosition;
    private Dictionary<string, CoverageResult> coverageResults = new Dictionary<string, CoverageResult>();
    private bool analyzed = false;

    private class CoverageResult
    {
        public string Name;
        public string UnicodeRange;
        public int TotalChars;
        public int SupportedChars;
        public float Percentage => TotalChars > 0 ? (SupportedChars * 100f / TotalChars) : 0;
        public string MissingSample;
    }

    // Test character sets for each language (no commas in strings!)
    private static readonly Dictionary<string, TestData> LanguageTests = new Dictionary<string, TestData>
    {
        { "Basic Latin", new TestData("0020-007E", " !\"#$%&'()*+-./:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~0123456789") },
        { "Vietnamese", new TestData("1EA0-1EFF", "ÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚÝàáâãèéêìíòóôõùúýĂăĐđĨĩŨũƠơƯưẠạẢảẤấẦầẨẩẪẫẬậẮắẰằẲẳẴẵẶặẸẹẺẻẼẽẾếỀềỂểỄễỆệỈỉỊịỌọỎỏỐốỒồỔổỖỗỘộỚớỜờỞởỠỡỢợỤụỦủỨứỪừỬửỮữỰựỲỳỴỵỶỷỸỹ") },
        { "Latin Extended", new TestData("0100-017F", "ĀāĂăĄąĆćĈĉĊċČčĎďĐđĒēĔĕĖėĘęĚěĜĝĞğĠġĢģĤĥĦħĨĩĪīĬĭĮįİıĲĳĴĵĶķĸĹĺĻļĽľĿŀŁłŃńŅņŇňŉŊŋŌōŎŏŐőŒœŔŕŖŗŘřŚśŜŝŞşŠšŢţŤťŦŧŨũŪūŬŭŮůŰűŲųŴŵŶŷŸŹźŻżŽž") },
        { "Latin-1 Supplement", new TestData("00A0-00FF", "¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ") },
        { "Cyrillic (Russian)", new TestData("0400-04FF", "ЀЁЂЃЄЅІЇЈЉЊЋЌЍЎЏАБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдежзийклмнопрстуфхцчшщъыьэюя") },
        { "Greek", new TestData("0370-03FF", "ΆΈΉΊΌΎΏΐΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩαβγδεζηθικλμνξοπρστυφχψω") },
        { "CJK Chinese", new TestData("4E00-9FFF", "的一是不了在人有我他这个们中来上大为和国地到以说时要就出会可也你对生能而子那得于着下自之年过发后作里用道行所然家种事成方多经么去法学如都同现当没动面起看定天分还进好小部其些主样理心她本前开但因只从想实日军者意无力它与长把机十民") },
        { "Japanese Hiragana", new TestData("3040-309F", "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをんゔ") },
        { "Japanese Katakana", new TestData("30A0-30FF", "ァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵヶ") },
        { "Korean Hangul", new TestData("AC00-D7AF", "가각간갈감갑강개객갱갸걀거걱건걸검겁게겨격견결겸겹경계고곡곤골곰곱공과곽관광괘괴굉교구국군굴굼굽궁권궐궤귀규균귤그극근글금급긍기긴길김깁깅까깍깐깔깜깝깡깨깻꺼꺾껀껄껌껍껏껑께껴꼬꼭꼴꼼꼽꽁꽂꽃꽈꽉꽝꽤꾀꾸꾹꾼꿀꿇꿈꿉꿍꿔꿨꿩꿰뀌뀐뀔뀜뀝뀨끄끈끊끌끓끔끕끗끙끝끼끽낀낄낌낍낏낑나낙낚난날남납낫났낭낮낯낱낳내낵낸낼냄냅냇냈냉냐냔냘냥너넉넌널넘넙넛넝넣네넥넨넬넴넵넷넹녀녁년념녑녕녘녜녠노녹논놀놈놉놋농높놓놔놨뇌뇨") },
        { "Arabic", new TestData("0600-06FF", "ءآأؤإئابةتثجحخدذرزسشصضطظعغفقكلمنهوىيًٌٍَُِّْٰٱٲٳٴٵٶٷٸٹٺٻټٽپٿڀځڂڃڄڅچڇڈډڊڋڌڍڎڏڐڑڒړڔڕږڗژڙښڛڜڝڞڟڠڡڢڣڤڥڦڧڨکڪګڬڭڮگڰڱڲڳڴڵڶڷڸڹںڻڼڽھڿۀہۂۃۄۅۆۇۈۉۊۋیۍێۏېۑےۓەۥۦ") },
        { "Thai", new TestData("0E00-0E7F", "กขฃคฅฆงจฉชซฌญฎฏฐฑฒณดตถทธนบปผฝพฟภมยรลวศษสหฬอฮฯะัาำิีึืฺุู฿เแโใไๅๆ็่้๊๋์ํ๎๏๐๑๒๓๔๕๖๗๘๙๚๛") },
        { "Currency Symbols", new TestData("20A0-20CF", "₠₡₢₣₤₥₦₧₨₩₪₫€₭₮₯₰₱₲₳₴₵₶₷₸₹₺₻₼₽₾₿") },
        { "Common Punctuation", new TestData("2000-206F", "\u2010\u2011\u2012\u2013\u2014\u2015\u2016\u2017\u2018\u2019\u201A\u201B\u201C\u201D\u201E\u201F\u2020\u2021\u2022\u2023\u2024\u2025\u2026\u2027\u2030\u2031\u2032\u2033\u2034\u2035\u2036\u2037\u2038\u2039\u203A\u203B\u203C\u203D\u203E\u203F\u2040\u2041\u2042\u2043\u2044\u2045\u2046\u2047\u2048\u2049") },
    };

    private class TestData
    {
        public string Range;
        public string TestChars;

        public TestData(string range, string testChars)
        {
            Range = range;
            TestChars = testChars;
        }
    }

    [MenuItem("Tools/Font Coverage Analyzer")]
    public static void ShowWindow()
    {
        var window = GetWindow<FontCoverageAnalyzer>("Font Analyzer");
        window.minSize = new Vector2(550, 500);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Font Coverage Analyzer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Phân tích font để xác định các ngôn ngữ được hỗ trợ.\n" +
            "Kéo thả font (.ttf/.otf) vào đây để kiểm tra.",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Font selection
        EditorGUI.BeginChangeCheck();
        selectedFont = (Font)EditorGUILayout.ObjectField("Font File", selectedFont, typeof(Font), false);

        if (EditorGUI.EndChangeCheck())
        {
            analyzed = false;
            coverageResults.Clear();
        }

        EditorGUILayout.Space(5);

        EditorGUI.BeginDisabledGroup(selectedFont == null);

        if (GUILayout.Button("Analyze Font Coverage", GUILayout.Height(30)))
        {
            AnalyzeFont();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(15);

        // Results
        if (analyzed && coverageResults.Count > 0)
        {
            EditorGUILayout.LabelField($"Results for: {selectedFont.name}", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Sort by coverage percentage
            var sortedResults = coverageResults.Values.OrderByDescending(r => r.Percentage).ToList();

            foreach (var result in sortedResults)
            {
                DrawCoverageResult(result);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Copy Report to Clipboard"))
            {
                CopyReportToClipboard();
            }
        }

        EditorGUILayout.Space(10);

        // Batch analyze
        EditorGUILayout.LabelField("Batch Analyze", EditorStyles.boldLabel);

        if (GUILayout.Button("Analyze All Fonts in Folder..."))
        {
            BatchAnalyzeFonts();
        }
    }

    private void AnalyzeFont()
    {
        if (selectedFont == null) return;

        coverageResults.Clear();

        // Load font into FontEngine for accurate glyph detection
        if (!LoadFontForAnalysis(selectedFont))
        {
            Debug.LogError($"Failed to load font: {selectedFont.name}");
            return;
        }

        foreach (var kvp in LanguageTests)
        {
            string langName = kvp.Key;
            TestData data = kvp.Value;

            var result = new CoverageResult
            {
                Name = langName,
                UnicodeRange = data.Range,
                TotalChars = data.TestChars.Length,
                SupportedChars = 0,
                MissingSample = ""
            };

            var missingChars = new List<char>();

            foreach (char c in data.TestChars)
            {
                // Use FontEngine for accurate glyph detection
                if (HasActualGlyph(selectedFont, c))
                {
                    result.SupportedChars++;
                }
                else
                {
                    if (missingChars.Count < 10)
                    {
                        missingChars.Add(c);
                    }
                }
            }

            result.MissingSample = new string(missingChars.ToArray());
            coverageResults[langName] = result;
        }

        analyzed = true;
    }

    // Cache loaded font to avoid reloading
    private Font _loadedFont = null;

    /// <summary>
    /// Check if font has an actual glyph using TMP FontEngine.
    /// Unity's Font API is unreliable as it may use OS fallback fonts.
    /// FontEngine reads directly from the font file for accurate results.
    /// </summary>
    private bool HasActualGlyph(Font font, char c)
    {
        // TryGetGlyphIndex returns false if character is not in font
        if (UnityEngine.TextCore.LowLevel.FontEngine.TryGetGlyphIndex((uint)c, out uint glyphIndex))
        {
            return glyphIndex != 0;
        }
        return false;
    }

    /// <summary>
    /// Load font into FontEngine for accurate glyph detection.
    /// </summary>
    private bool LoadFontForAnalysis(Font font)
    {
        // Initialize FontEngine if not already initialized
        UnityEngine.TextCore.LowLevel.FontEngine.InitializeFontEngine();

        // Skip if already loaded
        if (_loadedFont == font) return true;

        // Use Font object overload for reliable path handling
        var loadResult = UnityEngine.TextCore.LowLevel.FontEngine.LoadFontFace(font);
        if (loadResult != UnityEngine.TextCore.LowLevel.FontEngineError.Success)
        {
            Debug.LogWarning($"Failed to load font face for {font.name}: {loadResult}");
            return false;
        }

        _loadedFont = font;
        return true;
    }

    private void DrawCoverageResult(CoverageResult result)
    {
        EditorGUILayout.BeginVertical("box");

        Color bgColor;
        string status;

        if (result.Percentage >= 95)
        {
            bgColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            status = "✓ Full";
        }
        else if (result.Percentage >= 50)
        {
            bgColor = new Color(0.8f, 0.8f, 0.2f, 0.3f);
            status = "◐ Partial";
        }
        else if (result.Percentage > 0)
        {
            bgColor = new Color(0.8f, 0.5f, 0.2f, 0.3f);
            status = "◔ Limited";
        }
        else
        {
            bgColor = new Color(0.8f, 0.2f, 0.2f, 0.3f);
            status = "✗ None";
        }

        var rect = EditorGUILayout.BeginHorizontal();
        EditorGUI.DrawRect(rect, bgColor);

        EditorGUILayout.LabelField(result.Name, EditorStyles.boldLabel, GUILayout.Width(180));
        EditorGUILayout.LabelField($"{result.Percentage:F0}%", GUILayout.Width(50));
        EditorGUILayout.LabelField($"({result.SupportedChars}/{result.TotalChars})", GUILayout.Width(80));
        EditorGUILayout.LabelField(status);

        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(result.MissingSample) && result.Percentage < 100)
        {
            EditorGUILayout.LabelField($"  Missing: {result.MissingSample}...", EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    private void CopyReportToClipboard()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Font Coverage Report: {selectedFont.name} ===\n");

        var sortedResults = coverageResults.Values.OrderByDescending(r => r.Percentage).ToList();

        foreach (var result in sortedResults)
        {
            string status = result.Percentage >= 95 ? "✓" : result.Percentage >= 50 ? "◐" : result.Percentage > 0 ? "◔" : "✗";
            sb.AppendLine($"{status} {result.Name}: {result.Percentage:F1}% ({result.SupportedChars}/{result.TotalChars})");

            if (!string.IsNullOrEmpty(result.MissingSample))
            {
                sb.AppendLine($"   Missing: {result.MissingSample}");
            }
        }

        GUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("Report copied to clipboard!");
    }

    private void BatchAnalyzeFonts()
    {
        string folderPath = EditorUtility.OpenFolderPanel("Select Fonts Folder", "Assets", "");

        if (string.IsNullOrEmpty(folderPath)) return;

        if (folderPath.StartsWith(Application.dataPath))
        {
            folderPath = "Assets" + folderPath.Substring(Application.dataPath.Length);
        }

        string[] fontGuids = AssetDatabase.FindAssets("t:Font", new[] { folderPath });

        var report = new StringBuilder();
        report.AppendLine("=== Batch Font Analysis Report ===\n");

        foreach (string guid in fontGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(path);

            if (font != null)
            {
                // Load font into FontEngine for accurate glyph detection
                if (!LoadFontForAnalysis(font))
                {
                    report.AppendLine($"\n--- {font.name} --- (Failed to load)");
                    continue;
                }

                report.AppendLine($"\n--- {font.name} ---");

                foreach (var kvp in LanguageTests)
                {
                    string testChars = kvp.Value.TestChars;

                    int supported = 0;
                    foreach (char c in testChars)
                    {
                        if (HasActualGlyph(font, c)) supported++;
                    }

                    float percentage = (supported * 100f) / testChars.Length;
                    string status = percentage >= 95 ? "✓" : percentage >= 50 ? "◐" : percentage > 0 ? "◔" : "✗";

                    report.AppendLine($"  {status} {kvp.Key}: {percentage:F0}%");
                }
            }
        }

        EditorUtility.DisplayDialog("Batch Analysis Complete",
            $"Analyzed {fontGuids.Length} fonts.\nReport copied to clipboard.",
            "OK");

        GUIUtility.systemCopyBuffer = report.ToString();
        Debug.Log(report.ToString());
    }
}
