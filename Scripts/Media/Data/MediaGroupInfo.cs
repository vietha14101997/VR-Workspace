using System;
using System.Collections.Generic;

/// <summary>
/// Represents a group of media items with a common grouping key.
/// Used for displaying grouped content with section headers.
/// </summary>
public class MediaGroupInfo
{
    /// <summary>
    /// The display title for this group (e.g., "Th 5, 22 thg 1" for date grouping).
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// The grouping key used for sorting/comparison.
    /// </summary>
    public string GroupKey { get; set; }

    /// <summary>
    /// Items belonging to this group.
    /// </summary>
    public List<MediaVideoInfo> Items { get; set; } = new List<MediaVideoInfo>();

    /// <summary>
    /// Number of items in this group.
    /// </summary>
    public int Count => Items?.Count ?? 0;

    /// <summary>
    /// Start index of this group in the flattened list (for grid positioning).
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// End index of this group in the flattened list.
    /// </summary>
    public int EndIndex => StartIndex + Count - 1;
}

/// <summary>
/// Helper class for generating group titles based on grouping type.
/// </summary>
public static class MediaGroupHelper
{
    /// <summary>
    /// Get group key for a video based on grouping type.
    /// </summary>
    public static string GetGroupKey(MediaVideoInfo video, string groupBy)
    {
        switch (groupBy?.ToLower().Replace(" ", ""))
        {
            case "dateadded":
                return video.DateAdded.Date.ToString("yyyy-MM-dd");

            case "duration":
                return GetDurationCategory(video.Duration).ToString();

            case "resolution":
                return GetResolutionCategory(video.Height).ToString();

            case "format":
                return System.IO.Path.GetExtension(video.Path)?.ToLower() ?? "";

            default:
                return video.DateAdded.Date.ToString("yyyy-MM-dd");
        }
    }

    /// <summary>
    /// Get display title for a group based on grouping type.
    /// </summary>
    public static string GetGroupTitle(MediaVideoInfo video, string groupBy)
    {
        switch (groupBy?.ToLower().Replace(" ", ""))
        {
            case "dateadded":
                return FormatDateTitle(video.DateAdded);

            case "duration":
                return GetDurationCategoryTitle(video.Duration);

            case "resolution":
                return GetResolutionCategoryTitle(video.Height);

            case "format":
                string ext = System.IO.Path.GetExtension(video.Path)?.ToUpper()?.TrimStart('.') ?? "Unknown";
                return ext;

            default:
                return FormatDateTitle(video.DateAdded);
        }
    }

    /// <summary>
    /// Format date as English-style title (e.g., "Thu, Jan 22").
    /// </summary>
    public static string FormatDateTitle(DateTime date)
    {
        // English day of week abbreviations
        string[] dayNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        string[] monthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        string dayOfWeek = dayNames[(int)date.DayOfWeek];
        string month = monthNames[date.Month - 1];

        // Format: "Thu, Jan 22" (Thursday, January 22)
        return $"{dayOfWeek}, {month} {date.Day}";
    }

    /// <summary>
    /// Get duration category (0-3).
    /// </summary>
    public static int GetDurationCategory(TimeSpan duration)
    {
        double minutes = duration.TotalMinutes;
        if (minutes < 5) return 0;       // Short
        if (minutes < 20) return 1;      // Medium
        if (minutes < 60) return 2;      // Long
        return 3;                         // Extended
    }

    /// <summary>
    /// Get duration category display title.
    /// </summary>
    public static string GetDurationCategoryTitle(TimeSpan duration)
    {
        int category = GetDurationCategory(duration);
        switch (category)
        {
            case 0: return "Short (< 5 min)";
            case 1: return "Medium (5-20 min)";
            case 2: return "Long (20-60 min)";
            case 3: return "Extended (> 60 min)";
            default: return "Unknown";
        }
    }

    /// <summary>
    /// Get resolution category (0-4).
    /// </summary>
    public static int GetResolutionCategory(int height)
    {
        if (height >= 2160) return 4;    // 4K
        if (height >= 1440) return 3;    // 1440p
        if (height >= 1080) return 2;    // 1080p
        if (height >= 720) return 1;     // 720p
        return 0;                         // SD
    }

    /// <summary>
    /// Get resolution category display title.
    /// </summary>
    public static string GetResolutionCategoryTitle(int height)
    {
        int category = GetResolutionCategory(height);
        switch (category)
        {
            case 4: return "4K (2160p+)";
            case 3: return "1440p";
            case 2: return "1080p";
            case 1: return "720p";
            case 0: return "SD";
            default: return "Unknown";
        }
    }

    /// <summary>
    /// Create groups from a sorted list of videos.
    /// </summary>
    public static List<MediaGroupInfo> CreateGroups(List<MediaVideoInfo> videos, string groupBy)
    {
        var groups = new List<MediaGroupInfo>();
        if (videos == null || videos.Count == 0) return groups;

        MediaGroupInfo currentGroup = null;
        string lastKey = null;
        int currentIndex = 0;

        foreach (var video in videos)
        {
            string key = GetGroupKey(video, groupBy);

            if (currentGroup == null || key != lastKey)
            {
                // Start new group
                currentGroup = new MediaGroupInfo
                {
                    GroupKey = key,
                    Title = GetGroupTitle(video, groupBy),
                    StartIndex = currentIndex
                };
                groups.Add(currentGroup);
                lastKey = key;
            }

            currentGroup.Items.Add(video);
            currentIndex++;
        }

        return groups;
    }
}
