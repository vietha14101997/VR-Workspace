using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using UnityEngine;

namespace VRWorkspace.Media.Subtitles
{
    /// <summary>
    /// Represents a single subtitle entry.
    /// </summary>
    [Serializable]
    public struct SubtitleEntry
    {
        public int Index;
        public float StartTime;     // In seconds
        public float EndTime;       // In seconds
        public string Text;
        public string[] Lines;      // Text split by newlines

        public float Duration => EndTime - StartTime;

        public bool IsVisibleAt(float time)
        {
            return time >= StartTime && time <= EndTime;
        }
    }

    /// <summary>
    /// Parser for SRT (SubRip) subtitle format.
    /// Supports standard SRT format and common variations.
    /// </summary>
    public static class SrtParser
    {
        // Regex patterns
        private static readonly Regex IndexPattern = new Regex(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex TimecodePattern = new Regex(
            @"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Parse SRT content from string.
        /// </summary>
        public static List<SubtitleEntry> Parse(string content)
        {
            var entries = new List<SubtitleEntry>();

            if (string.IsNullOrEmpty(content))
                return entries;

            // Normalize line endings
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            // Split by double newline (subtitle blocks)
            string[] blocks = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string block in blocks)
            {
                var entry = ParseBlock(block.Trim());
                if (entry.HasValue)
                {
                    entries.Add(entry.Value);
                }
            }

            // Sort by start time
            entries.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            return entries;
        }

        /// <summary>
        /// Load and parse SRT file from path.
        /// </summary>
        public static List<SubtitleEntry> ParseFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"[SrtParser] File not found: {filePath}");
                    return new List<SubtitleEntry>();
                }

                // Try to detect encoding
                string content = ReadFileWithEncoding(filePath);
                return Parse(content);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SrtParser] Error parsing file '{filePath}': {ex.Message}");
                return new List<SubtitleEntry>();
            }
        }

        /// <summary>
        /// Find subtitle file for a video file.
        /// Searches for .srt files with same name or in same directory.
        /// </summary>
        public static string FindSubtitleFile(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath))
                return null;

            try
            {
                string dir = Path.GetDirectoryName(videoPath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);

                // 1. Same name, .srt extension
                string srtPath = Path.Combine(dir, nameWithoutExt + ".srt");
                if (File.Exists(srtPath))
                    return srtPath;

                // 2. Same name with language suffix (e.g., video.en.srt, video.vie.srt)
                string[] patterns = new[] {
                    $"{nameWithoutExt}.*.srt",
                    $"{nameWithoutExt}_*.srt"
                };

                foreach (string pattern in patterns)
                {
                    string[] files = Directory.GetFiles(dir, pattern);
                    if (files.Length > 0)
                    {
                        // Prefer Vietnamese or English subtitles
                        foreach (string file in files)
                        {
                            string lower = file.ToLowerInvariant();
                            if (lower.Contains(".vie.") || lower.Contains("_vie.") ||
                                lower.Contains(".vi.") || lower.Contains("_vi."))
                                return file;
                        }
                        foreach (string file in files)
                        {
                            string lower = file.ToLowerInvariant();
                            if (lower.Contains(".eng.") || lower.Contains("_eng.") ||
                                lower.Contains(".en.") || lower.Contains("_en."))
                                return file;
                        }
                        // Return first found
                        return files[0];
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SrtParser] Error finding subtitle: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all available subtitle files for a video.
        /// </summary>
        public static List<SubtitleFileInfo> GetAvailableSubtitles(string videoPath)
        {
            var result = new List<SubtitleFileInfo>();

            if (string.IsNullOrEmpty(videoPath))
                return result;

            try
            {
                string dir = Path.GetDirectoryName(videoPath);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);

                // Find all .srt files that could be subtitles for this video
                string[] srtFiles = Directory.GetFiles(dir, "*.srt");

                foreach (string srtFile in srtFiles)
                {
                    string srtName = Path.GetFileNameWithoutExtension(srtFile);

                    // Check if this subtitle belongs to the video
                    if (srtName.StartsWith(nameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    {
                        var info = new SubtitleFileInfo
                        {
                            FilePath = srtFile,
                            FileName = Path.GetFileName(srtFile),
                            Language = DetectLanguage(srtFile)
                        };
                        result.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SrtParser] Error getting subtitles: {ex.Message}");
            }

            return result;
        }

        #region Private Methods
        private static SubtitleEntry? ParseBlock(string block)
        {
            if (string.IsNullOrEmpty(block))
                return null;

            string[] lines = block.Split('\n');
            if (lines.Length < 3)
                return null;

            // Line 0: Index (optional, skip if not a number)
            int startLine = 0;
            int index = 0;

            if (IndexPattern.IsMatch(lines[0].Trim()))
            {
                int.TryParse(lines[0].Trim(), out index);
                startLine = 1;
            }

            // Find timecode line
            if (startLine >= lines.Length)
                return null;

            Match match = TimecodePattern.Match(lines[startLine]);
            if (!match.Success)
            {
                // Try next line
                startLine++;
                if (startLine >= lines.Length)
                    return null;
                match = TimecodePattern.Match(lines[startLine]);
                if (!match.Success)
                    return null;
            }

            // Parse timecodes
            float startTime = ParseTimecode(match.Groups[1].Value, match.Groups[2].Value,
                match.Groups[3].Value, match.Groups[4].Value);
            float endTime = ParseTimecode(match.Groups[5].Value, match.Groups[6].Value,
                match.Groups[7].Value, match.Groups[8].Value);

            // Remaining lines are text
            var textLines = new List<string>();
            for (int i = startLine + 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    // Remove basic HTML tags
                    line = StripHtmlTags(line);
                    textLines.Add(line);
                }
            }

            if (textLines.Count == 0)
                return null;

            return new SubtitleEntry
            {
                Index = index,
                StartTime = startTime,
                EndTime = endTime,
                Text = string.Join("\n", textLines.ToArray()),
                Lines = textLines.ToArray()
            };
        }

        private static float ParseTimecode(string hours, string minutes, string seconds, string milliseconds)
        {
            int h = int.Parse(hours);
            int m = int.Parse(minutes);
            int s = int.Parse(seconds);
            int ms = int.Parse(milliseconds.PadRight(3, '0').Substring(0, 3));

            return h * 3600f + m * 60f + s + ms / 1000f;
        }

        private static string StripHtmlTags(string text)
        {
            // Remove common SRT HTML tags: <b>, </b>, <i>, </i>, <u>, </u>, <font>, </font>
            text = Regex.Replace(text, @"<\/?[biuBIU]>", "");
            text = Regex.Replace(text, @"<font[^>]*>|<\/font>", "", RegexOptions.IgnoreCase);
            return text;
        }

        private static string ReadFileWithEncoding(string filePath)
        {
            // Try to detect encoding from BOM or content
            byte[] bytes = File.ReadAllBytes(filePath);

            // Check for BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                // UTF-8 with BOM
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                // UTF-16 LE
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                // UTF-16 BE
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            // No BOM, try UTF-8 first, then system default
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static string DetectLanguage(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

            // Common language suffixes
            if (name.Contains(".vie") || name.Contains("_vie") || name.Contains(".vi") || name.Contains("_vi"))
                return "Vietnamese";
            if (name.Contains(".eng") || name.Contains("_eng") || name.Contains(".en") || name.Contains("_en"))
                return "English";
            if (name.Contains(".jpn") || name.Contains("_jpn") || name.Contains(".jp") || name.Contains("_jp"))
                return "Japanese";
            if (name.Contains(".chi") || name.Contains("_chi") || name.Contains(".zh") || name.Contains("_zh"))
                return "Chinese";
            if (name.Contains(".kor") || name.Contains("_kor") || name.Contains(".ko") || name.Contains("_ko"))
                return "Korean";

            return "Unknown";
        }
        #endregion
    }

    /// <summary>
    /// Information about a subtitle file.
    /// </summary>
    public struct SubtitleFileInfo
    {
        public string FilePath;
        public string FileName;
        public string Language;
    }
}
