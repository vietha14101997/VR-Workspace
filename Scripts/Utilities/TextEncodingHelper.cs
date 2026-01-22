using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Helper class to detect and fix text encoding issues.
/// Handles common mojibake scenarios where UTF-8 text is incorrectly decoded as Latin-1/Windows-1252.
/// </summary>
public static class TextEncodingHelper
{
    // Mojibake detection patterns - VERY SPECIFIC patterns only
    // These patterns indicate UTF-8 bytes were incorrectly decoded as Latin-1/Windows-1252
    // IMPORTANT: Do NOT add single-character patterns as they cause false positives with valid Vietnamese
    private static readonly string[] MojibakePatterns = new string[]
    {
        // Double-encoded UTF-8 (very specific, safe to detect)
        "\u00C3\u0083\u00C2",  // Triple-encoded pattern
        "\u00C3\u00C2",       // Double Ã sequence

        // Specific Windows smart quotes mojibake (3-char sequences)
        "\u00E2\u0080\u0099",  // Right single quote '
        "\u00E2\u0080\u009C",  // Left double quote "
        "\u00E2\u0080\u009D",  // Right double quote "
        "\u00E2\u0080\u0093",  // En dash –
        "\u00E2\u0080\u0094",  // Em dash —
        "\u00E2\u0080\u00A6",  // Ellipsis …

        // Non-breaking space mojibake
        "\u00C2\u00A0",  // NBSP when double-encoded
    };

    // Regex to detect mojibake - looks for sequences that are characteristic of UTF-8 bytes decoded as Latin-1
    private static readonly Regex MojibakeRegex = new Regex(
        @"[\xC0-\xDF][\x80-\xBF]|[\xE0-\xEF][\x80-\xBF]{2}|[\xF0-\xF7][\x80-\xBF]{3}",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Read a text file with automatic encoding detection and fixing.
    /// </summary>
    /// <param name="filePath">Path to the text file</param>
    /// <returns>Properly decoded text content</returns>
    public static string ReadTextFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[TextEncodingHelper] File not found: {filePath}");
            return null;
        }

        try
        {
            // First, try reading as UTF-8
            string content = File.ReadAllText(filePath, Encoding.UTF8);

            // Normalize Unicode to NFC (composed form) for proper Vietnamese display
            if (!string.IsNullOrEmpty(content) && !content.IsNormalized(NormalizationForm.FormC))
            {
                content = content.Normalize(NormalizationForm.FormC);
            }

            // Check if the content looks like mojibake
            if (IsMojibake(content))
            {
                Debug.Log($"[TextEncodingHelper] Detected mojibake in {Path.GetFileName(filePath)}, attempting to fix...");

                // Try to fix by re-interpreting as Latin-1 bytes then UTF-8
                content = FixMojibake(content);

                // If still mojibake, try reading with different encodings
                if (IsMojibake(content))
                {
                    content = TryAlternateEncodings(filePath);
                }
            }

            return content;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TextEncodingHelper] Error reading file: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read a text file with automatic encoding detection, with size limit.
    /// </summary>
    /// <param name="filePath">Path to the text file</param>
    /// <param name="maxBytes">Maximum bytes to read (0 = no limit)</param>
    /// <returns>Properly decoded text content</returns>
    public static string ReadTextFileWithLimit(string filePath, int maxBytes = 0)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[TextEncodingHelper] File not found: {filePath}");
            return null;
        }

        try
        {
            byte[] bytes;

            if (maxBytes > 0)
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    int bytesToRead = (int)Math.Min(maxBytes, fs.Length);
                    bytes = new byte[bytesToRead];
                    fs.Read(bytes, 0, bytesToRead);
                }
            }
            else
            {
                bytes = File.ReadAllBytes(filePath);
            }

            // Detect encoding from BOM or content
            Encoding detectedEncoding = DetectEncoding(bytes);
            string content = detectedEncoding.GetString(bytes);

            // Normalize Unicode to NFC (composed form) for proper Vietnamese display
            if (!string.IsNullOrEmpty(content) && !content.IsNormalized(NormalizationForm.FormC))
            {
                content = content.Normalize(NormalizationForm.FormC);
            }

            // Check and fix mojibake
            if (IsMojibake(content))
            {
                content = FixMojibake(content);
            }

            return content;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TextEncodingHelper] Error reading file: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if text appears to be mojibake (incorrectly decoded UTF-8).
    /// CONSERVATIVE: Only returns true for definite mojibake patterns.
    /// Vietnamese UTF-8 text should NOT trigger this.
    /// </summary>
    public static bool IsMojibake(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Only check for very specific mojibake patterns
        // Do NOT use character range detection as it causes false positives with Vietnamese
        foreach (var pattern in MojibakePatterns)
        {
            if (text.Contains(pattern))
            {
                return true;
            }
        }

        // Check for replacement character (indicates encoding failure)
        if (text.Contains('\uFFFD'))
        {
            return true;
        }

        // Check for specific "Ã + Latin-1 Supplement" pattern which is definite mojibake
        // This occurs when UTF-8 2-byte sequences are read as Latin-1
        // Pattern: Ã (U+00C3) followed by character in range 0x80-0xBF
        for (int i = 0; i < text.Length - 1; i++)
        {
            char c1 = text[i];
            char c2 = text[i + 1];

            // Ã (0xC3) followed by continuation byte range when read as Latin-1
            // This is a strong mojibake indicator for 2-byte UTF-8 sequences
            if (c1 == '\u00C3' && c2 >= '\u0080' && c2 <= '\u00BF')
            {
                return true;
            }

            // Ä (0xC4) followed by 0x80-0x8F (Đ, đ range in UTF-8)
            if (c1 == '\u00C4' && c2 >= '\u0080' && c2 <= '\u009F')
            {
                return true;
            }

            // Æ (0xC6) followed by continuation byte (ơ, ư range)
            if (c1 == '\u00C6' && c2 >= '\u00A0' && c2 <= '\u00BF')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempt to fix mojibake by re-interpreting as Latin-1 bytes then UTF-8.
    /// </summary>
    public static string FixMojibake(string mojibakeText)
    {
        if (string.IsNullOrEmpty(mojibakeText)) return mojibakeText;

        try
        {
            // Get bytes as if the string was Latin-1 encoded
            byte[] bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(mojibakeText);

            // Re-decode as UTF-8
            string fixedText = Encoding.UTF8.GetString(bytes);

            // Verify the fix worked (should have fewer suspicious patterns)
            if (!IsMojibake(fixedText) || CountMojibakePatterns(fixedText) < CountMojibakePatterns(mojibakeText))
            {
                Debug.Log("[TextEncodingHelper] Successfully fixed mojibake");
                return fixedText;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TextEncodingHelper] Failed to fix mojibake: {ex.Message}");
        }

        // Return original if fix didn't help
        return mojibakeText;
    }

    /// <summary>
    /// Fix a single string that may be mojibake (useful for file names).
    /// Also normalizes Unicode to NFC (composed form) for proper Vietnamese display.
    /// </summary>
    public static string FixString(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Step 1: Normalize Unicode to NFC (Composed form)
        // This converts decomposed Vietnamese (e + combining marks) to composed (ề)
        // Critical for proper font rendering!
        string normalizedText = text;
        try
        {
            if (!text.IsNormalized(NormalizationForm.FormC))
            {
                normalizedText = text.Normalize(NormalizationForm.FormC);
                if (normalizedText != text)
                {
                    Debug.Log($"[TextEncodingHelper] Normalized NFD->NFC: '{text}' ({text.Length} chars) -> '{normalizedText}' ({normalizedText.Length} chars)");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TextEncodingHelper] Unicode normalization failed: {ex.Message}");
        }

        // Step 2: Check and fix mojibake if present
        if (IsMojibake(normalizedText))
        {
            string fixedText = FixMojibake(normalizedText);

            // Verify the fix actually improved the text
            if (fixedText != normalizedText && IsFixBetter(normalizedText, fixedText))
            {
                Debug.Log($"[TextEncodingHelper] Fixed mojibake: '{normalizedText}' -> '{fixedText}'");
                return fixedText;
            }
            else if (fixedText != normalizedText)
            {
                Debug.Log($"[TextEncodingHelper] Fix rejected (would make text worse): '{normalizedText}'");
            }
        }

        return normalizedText;
    }

    /// <summary>
    /// Check if the fixed text is actually better than the original.
    /// Returns false if fix introduced replacement characters or other problems.
    /// </summary>
    private static bool IsFixBetter(string original, string fixedText)
    {
        if (string.IsNullOrEmpty(fixedText)) return false;

        // If fix introduced replacement characters, it's worse
        int originalReplacements = CountChar(original, '\uFFFD');
        int fixedReplacements = CountChar(fixedText, '\uFFFD');
        if (fixedReplacements > originalReplacements) return false;

        // If fix introduced '?' characters (encoding failure), it's worse
        int originalQuestions = CountChar(original, '?');
        int fixedQuestions = CountChar(fixedText, '?');
        // Allow some tolerance but reject if many more questions appeared
        if (fixedQuestions > originalQuestions + 2) return false;

        // If the fixed text is still mojibake, the fix didn't help
        if (IsMojibake(fixedText)) return false;

        return true;
    }

    private static int CountChar(string text, char c)
    {
        int count = 0;
        foreach (char ch in text)
        {
            if (ch == c) count++;
        }
        return count;
    }

    /// <summary>
    /// Debug helper: Get hex dump of string's character codes.
    /// </summary>
    public static string GetCharCodeDump(string text, int maxChars = 20)
    {
        if (string.IsNullOrEmpty(text)) return "empty";

        var sb = new StringBuilder();
        int len = Math.Min(text.Length, maxChars);
        for (int i = 0; i < len; i++)
        {
            sb.Append($"U+{(int)text[i]:X4} ");
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Try reading file with alternate encodings.
    /// </summary>
    private static string TryAlternateEncodings(string filePath)
    {
        Encoding[] encodingsToTry = new Encoding[]
        {
            Encoding.UTF8,
            Encoding.GetEncoding("Windows-1252"),
            Encoding.GetEncoding("ISO-8859-1"),
            Encoding.Unicode,
            Encoding.BigEndianUnicode,
            Encoding.UTF32,
        };

        string bestResult = null;
        int lowestMojibakeCount = int.MaxValue;

        foreach (var encoding in encodingsToTry)
        {
            try
            {
                string content = File.ReadAllText(filePath, encoding);
                int mojibakeCount = CountMojibakePatterns(content);

                if (mojibakeCount < lowestMojibakeCount)
                {
                    lowestMojibakeCount = mojibakeCount;
                    bestResult = content;

                    if (mojibakeCount == 0)
                    {
                        Debug.Log($"[TextEncodingHelper] Found correct encoding: {encoding.EncodingName}");
                        break;
                    }
                }
            }
            catch
            {
                // Skip this encoding if it fails
            }
        }

        return bestResult;
    }

    /// <summary>
    /// Count the number of mojibake patterns in text.
    /// </summary>
    private static int CountMojibakePatterns(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int count = 0;
        foreach (var pattern in MojibakePatterns)
        {
            int index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }
        }
        return count;
    }

    /// <summary>
    /// Detect encoding from byte array (BOM detection + heuristics).
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2)
            return Encoding.UTF8;

        // Check for BOM (Byte Order Mark)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Encoding.UTF32;

        // No BOM, try to detect UTF-8 by checking for valid UTF-8 sequences
        if (IsValidUtf8(bytes))
            return Encoding.UTF8;

        // Default to Windows-1252 for legacy files
        return Encoding.GetEncoding("Windows-1252");
    }

    /// <summary>
    /// Check if byte array is valid UTF-8.
    /// </summary>
    private static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] <= 0x7F)
            {
                // ASCII
                i++;
            }
            else if (bytes[i] >= 0xC2 && bytes[i] <= 0xDF)
            {
                // 2-byte sequence
                if (i + 1 >= bytes.Length || bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF)
                    return false;
                i += 2;
            }
            else if (bytes[i] >= 0xE0 && bytes[i] <= 0xEF)
            {
                // 3-byte sequence
                if (i + 2 >= bytes.Length)
                    return false;
                if (bytes[i] == 0xE0 && (bytes[i + 1] < 0xA0 || bytes[i + 1] > 0xBF))
                    return false;
                if (bytes[i] == 0xED && (bytes[i + 1] < 0x80 || bytes[i + 1] > 0x9F))
                    return false;
                if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF || bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF)
                    return false;
                i += 3;
            }
            else if (bytes[i] >= 0xF0 && bytes[i] <= 0xF4)
            {
                // 4-byte sequence
                if (i + 3 >= bytes.Length)
                    return false;
                if (bytes[i] == 0xF0 && (bytes[i + 1] < 0x90 || bytes[i + 1] > 0xBF))
                    return false;
                if (bytes[i] == 0xF4 && (bytes[i + 1] < 0x80 || bytes[i + 1] > 0x8F))
                    return false;
                if (bytes[i + 1] < 0x80 || bytes[i + 1] > 0xBF ||
                    bytes[i + 2] < 0x80 || bytes[i + 2] > 0xBF ||
                    bytes[i + 3] < 0x80 || bytes[i + 3] > 0xBF)
                    return false;
                i += 4;
            }
            else
            {
                // Invalid UTF-8 start byte
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Get file encoding name for display.
    /// </summary>
    public static string GetEncodingName(string filePath)
    {
        if (!File.Exists(filePath)) return "Unknown";

        try
        {
            byte[] bytes = new byte[4096];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(bytes, 0, bytes.Length);
                Array.Resize(ref bytes, bytesRead);
            }

            return DetectEncoding(bytes).EncodingName;
        }
        catch
        {
            return "Unknown";
        }
    }
}
