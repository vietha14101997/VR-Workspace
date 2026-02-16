using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Detail View for RTT File Manager.
/// Displays information about the currently Hovered or Selected file/folder.
/// Layout: File name (top) -> Preview (centered) -> Metadata rows (bottom)
/// Metadata varies by file type: Image, Video, Music, Folder, Other
/// </summary>
public class RTTFileDetail : MonoBehaviour
{
    // UI References
    private TextMeshProUGUI _nameText;
    private MarqueeText _nameMarquee;
    private Image _previewImage;
    private Image _previewPlaceholder;
    private Transform _metadataContainer;

    // Current file for thumbnail loading
    private MockFile _currentFile;

    // Configuration
    private Color _primaryColor = Color.white;
    private Color _accentColor = new Color(0f, 0.9f, 1f);
    private TMP_FontAsset _font;

    // Layout constants - aligned with sortTrigger in RTTFileManager
    private const float NAME_HEIGHT = 68f;       // Match sortTrigger button height
    private const float TOP_PADDING = 45f;       // 50% more than original 30
    private const float METADATA_ROW_HEIGHT = 44f;
    private const float HORIZONTAL_PADDING = 30f; // 50% more than original 20
    private const float VERTICAL_SPACING = 28f;   // 50% more than original 15

    // Store preview Y position for metadata positioning
    private float _previewBottomY;

    public void Initialize(Color primary, Color accent, TMP_FontAsset font = null)
    {
        _primaryColor = primary;
        _accentColor = accent;
        _font = font;
        BuildUI();
    }

    private void BuildUI()
    {
        RectTransform containerRT = GetComponent<RectTransform>();
        float containerHeight = containerRT.rect.height > 0 ? containerRT.rect.height : 800f;
        float containerWidth = containerRT.rect.width > 0 ? containerRT.rect.width : 400f;

        // 1. File Name (top)
        CreateNameSection(containerWidth);

        // 2. Preview (centered, below name)
        CreatePreviewSection(containerWidth, containerHeight);

        // 3. Metadata Container (below preview)
        CreateMetadataContainer();
    }

    private void CreateNameSection(float containerWidth)
    {
        GameObject nameObj = new GameObject("FileName");
        nameObj.transform.SetParent(transform, false);

        RectTransform nameRT = nameObj.AddComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 1);
        nameRT.anchorMax = new Vector2(1, 1);
        nameRT.pivot = new Vector2(0.5f, 1);
        nameRT.anchoredPosition = new Vector2(0, -TOP_PADDING);
        nameRT.sizeDelta = new Vector2(-HORIZONTAL_PADDING * 2, NAME_HEIGHT);

        _nameText = nameObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) _nameText.font = _font;
        _nameText.fontSize = 36;
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;
        _nameText.color = Color.white;
        _nameText.enableWordWrapping = false;
        _nameText.overflowMode = TextOverflowModes.Overflow;

        // Setup marquee for auto-scrolling long file names (centered when fits)
        _nameMarquee = MarqueeText.Setup(_nameText, 40f, centerWhenFits: true);
    }

    private void CreatePreviewSection(float containerWidth, float containerHeight)
    {
        // Preview: full content width, 16:9 aspect ratio
        float previewWidth = containerWidth - HORIZONTAL_PADDING * 2;
        float previewHeight = previewWidth * 9f / 16f; // 16:9 aspect ratio
        float previewY = -TOP_PADDING - NAME_HEIGHT - VERTICAL_SPACING;

        // Store bottom position for metadata container
        _previewBottomY = previewY - previewHeight;

        // Preview container - full width, 16:9 aspect ratio
        GameObject previewContainer = new GameObject("PreviewContainer");
        previewContainer.transform.SetParent(transform, false);

        RectTransform containerRT = previewContainer.AddComponent<RectTransform>();
        containerRT.anchorMin = new Vector2(0.5f, 1);
        containerRT.anchorMax = new Vector2(0.5f, 1);
        containerRT.pivot = new Vector2(0.5f, 1);
        containerRT.anchoredPosition = new Vector2(0, previewY);
        containerRT.sizeDelta = new Vector2(previewWidth, previewHeight);

        // Placeholder icon (shown when no thumbnail) - centered, sized to fit height
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(previewContainer.transform, false);

        RectTransform placeholderRT = placeholderObj.AddComponent<RectTransform>();
        // Center the icon with fixed size based on container height
        float iconSize = previewHeight * 0.6f; // 60% of preview height
        placeholderRT.anchorMin = new Vector2(0.5f, 0.5f);
        placeholderRT.anchorMax = new Vector2(0.5f, 0.5f);
        placeholderRT.pivot = new Vector2(0.5f, 0.5f);
        placeholderRT.sizeDelta = new Vector2(iconSize, iconSize);

        _previewPlaceholder = placeholderObj.AddComponent<Image>();
        _previewPlaceholder.preserveAspect = true;
        _previewPlaceholder.color = Color.white;

        // Image for actual thumbnails (images/videos)
        GameObject previewObj = new GameObject("Preview");
        previewObj.transform.SetParent(previewContainer.transform, false);

        RectTransform previewRT = previewObj.AddComponent<RectTransform>();
        previewRT.anchorMin = Vector2.zero;
        previewRT.anchorMax = Vector2.one;
        previewRT.offsetMin = Vector2.zero;
        previewRT.offsetMax = Vector2.zero;

        _previewImage = previewObj.AddComponent<Image>();
        _previewImage.color = Color.white;
        _previewImage.preserveAspect = true;
        _previewImage.material = RoundedCorners.SharedMaterial;

        var roundedCorners = previewObj.AddComponent<RoundedCorners>();
        roundedCorners.Radius = 16f;
        roundedCorners.UseParentRect = false;

        _previewImage.gameObject.SetActive(false);
    }

    private void CreateMetadataContainer()
    {
        GameObject containerObj = new GameObject("MetadataContainer");
        containerObj.transform.SetParent(transform, false);

        RectTransform containerRT = containerObj.AddComponent<RectTransform>();
        // Position from top, right below preview
        containerRT.anchorMin = new Vector2(0, 1);
        containerRT.anchorMax = new Vector2(1, 1);
        containerRT.pivot = new Vector2(0.5f, 1);
        containerRT.anchoredPosition = new Vector2(0, _previewBottomY - VERTICAL_SPACING);
        // Stretch to bottom with some padding
        containerRT.sizeDelta = new Vector2(-HORIZONTAL_PADDING * 2, 600f); // Fixed height, will be clipped

        // Vertical layout for metadata rows
        VerticalLayoutGroup layout = containerObj.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = 12f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        _metadataContainer = containerObj.transform;
    }

    public void UpdateInfo(MockFile file, bool isCurrentFolder = false, bool forceRefresh = false)
    {
        // Skip update if same file (prevents lag when hovering between same items)
        // Unless forceRefresh is true (used after data reload to update metadata/thumbnail)
        if (_currentFile.Path == file.Path && !isCurrentFolder && !forceRefresh)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        _currentFile = file;

        // Update file name with marquee scroll
        if (_nameMarquee != null)
        {
            _nameMarquee.SetText(file.Name);
        }
        else if (_nameText != null)
        {
            _nameText.text = file.Name;
        }

        var nameTime = sw.ElapsedMilliseconds;

        // Update preview
        UpdatePreview(file);

        var previewTime = sw.ElapsedMilliseconds;

        // Update metadata based on file type
        UpdateMetadata(file, isCurrentFolder);

        var metadataTime = sw.ElapsedMilliseconds;

        Debug.Log($"[Detail] UpdateInfo: {file.Name} name={nameTime}ms preview={previewTime - nameTime}ms metadata={metadataTime - previewTime}ms total={metadataTime}ms");
    }

    /// <summary>
    /// Show empty/placeholder state (used when entering edit mode).
    /// Clears the current file reference so next UpdateInfo will work properly.
    /// </summary>
    public void ShowEmpty()
    {
        // Clear current file reference
        _currentFile = default;

        // Clear file name
        if (_nameMarquee != null)
        {
            _nameMarquee.SetText("");
        }
        else if (_nameText != null)
        {
            _nameText.text = "";
        }

        // Hide preview
        if (_previewImage != null)
        {
            _previewImage.gameObject.SetActive(false);
        }
        if (_previewPlaceholder != null)
        {
            _previewPlaceholder.gameObject.SetActive(false);
        }

        // Clear metadata
        if (_metadataContainer != null)
        {
            foreach (Transform child in _metadataContainer)
            {
                Destroy(child.gameObject);
            }
            _metadataValueMarquees.Clear();
        }
    }

    private void UpdatePreview(MockFile file)
    {
        if (_previewImage == null || _previewPlaceholder == null) return;

        // Determine file category
        FileCategory category = file.IsFolder ? FileCategory.Folder : FileCategoryHelper.GetCategory(file.Type);

        // For images/videos that will have thumbnails, don't show placeholder icon
        bool willHaveThumbnail = FileCategoryHelper.RequiresThumbnailGeneration(category) && !string.IsNullOrEmpty(file.Path);

        if (willHaveThumbnail)
        {
            // Hide placeholder while thumbnail loads (leave preview area blank)
            _previewPlaceholder.gameObject.SetActive(false);
            _previewImage.gameObject.SetActive(false);
            LoadThumbnail(file);
        }
        else
        {
            // Show placeholder icon for folders and other files
            string iconName;
            if (file.IsFolder)
            {
                iconName = file.IsFolderEmpty ? "icon_folder_empty" : "icon_folder_not_empty";
            }
            else
            {
                iconName = FileCategoryHelper.GetDefaultIconName(category);
            }

            Sprite icon = Resources.Load<Sprite>(iconName);
            if (icon == null) icon = Resources.Load<Sprite>("icon_file_unknown");

            _previewPlaceholder.sprite = icon;
            _previewPlaceholder.color = Color.white;
            _previewPlaceholder.gameObject.SetActive(true);
            _previewImage.gameObject.SetActive(false);
        }
    }

    private void LoadThumbnail(MockFile file)
    {
        if (FileThumbnailService.Instance == null) return;

        // Use larger size for detail panel high-quality preview
        int thumbnailSize = 512; // Reduced from 720 for better performance

        var requestTime = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log($"[Detail] REQUEST: {file.Name}");

        FileThumbnailService.Instance.RequestThumbnail(
            file,
            thumbnailSize,
            onSuccess: (sprite) =>
            {
                var assignStart = System.Diagnostics.Stopwatch.StartNew();
                // Verify this is still the current file being displayed
                if (sprite != null && _previewImage != null && _currentFile.Path == file.Path)
                {
                    _previewImage.sprite = sprite;
                    _previewImage.gameObject.SetActive(true);
                    _previewPlaceholder.gameObject.SetActive(false);
                }
                Debug.Log($"[Detail] RECEIVED: {file.Name} wait={requestTime.ElapsedMilliseconds}ms assign={assignStart.ElapsedMilliseconds}ms");
            },
            onFailed: () =>
            {
                Debug.Log($"[Detail] FAILED: {file.Name}");
                // On failure, leave preview area blank (no placeholder shown)
            },
            priority: 0,
            skipOverlay: true  // Don't show icon_media overlay on detail panel preview
        );
    }

    // Dictionary to store metadata row value marquee references for async updates
    private System.Collections.Generic.Dictionary<string, MarqueeText> _metadataValueMarquees =
        new System.Collections.Generic.Dictionary<string, MarqueeText>();

    private void UpdateMetadata(MockFile file, bool isCurrentFolder)
    {
        if (_metadataContainer == null) return;

        // Clear existing metadata rows
        foreach (Transform child in _metadataContainer)
        {
            Destroy(child.gameObject);
        }
        _metadataValueMarquees.Clear();

        FileCategory category = file.IsFolder ? FileCategory.Folder : FileCategoryHelper.GetCategory(file.Type);

        switch (category)
        {
            case FileCategory.Folder:
                CreateMetadataForFolder(file, isCurrentFolder);
                break;
            case FileCategory.Image:
                CreateMetadataForImage(file);
                FetchImageMetadata(file);
                break;
            case FileCategory.Video:
                CreateMetadataForVideo(file);
                FetchVideoMetadata(file);
                break;
            case FileCategory.Music:
                CreateMetadataForMusic(file);
                FetchAudioMetadata(file);
                break;
            default:
                CreateMetadataForOther(file);
                break;
        }
    }

    private void FetchImageMetadata(MockFile file)
    {
        if (FileMetadataService.Instance == null) return;

        FileMetadataService.Instance.GetImageMetadata(file.Path, (width, height) =>
        {
            // Verify still showing same file
            if (_currentFile.Path != file.Path) return;

            if (width > 0 && height > 0)
            {
                UpdateMetadataValue("Dimensions", $"{width}x{height}");
            }
        });
    }

    private void FetchVideoMetadata(MockFile file)
    {
        if (FileMetadataService.Instance == null) return;

        FileMetadataService.Instance.GetVideoMetadata(file.Path, (metadata) =>
        {
            // Verify still showing same file
            if (_currentFile.Path != file.Path) return;

            if (metadata.Width > 0 && metadata.Height > 0)
                UpdateMetadataValue("Dimensions", $"{metadata.Width}x{metadata.Height}");

            if (metadata.Duration.TotalSeconds > 0)
                UpdateMetadataValue("Length", FormatDuration(metadata.Duration));

            if (metadata.FrameRate > 0)
                UpdateMetadataValue("Frame rate", $"{metadata.FrameRate:F2} fps");

            if (metadata.TotalBitrate > 0)
                UpdateMetadataValue("Total bitrate", FormatBitrate(metadata.TotalBitrate));
        });
    }

    private void FetchAudioMetadata(MockFile file)
    {
        if (FileMetadataService.Instance == null) return;

        FileMetadataService.Instance.GetAudioMetadata(file.Path, (metadata) =>
        {
            // Verify still showing same file
            if (_currentFile.Path != file.Path) return;

            if (metadata.Duration.TotalSeconds > 0)
                UpdateMetadataValue("Length", FormatDuration(metadata.Duration));

            if (metadata.BitRate > 0)
                UpdateMetadataValue("Bit rate", $"{metadata.BitRate} kbps");

            if (!string.IsNullOrEmpty(metadata.Artist))
                UpdateMetadataValue("Contributing artists", metadata.Artist);

            if (!string.IsNullOrEmpty(metadata.Album))
                UpdateMetadataValue("Album", metadata.Album);

            if (!string.IsNullOrEmpty(metadata.Genre))
                UpdateMetadataValue("Genre", metadata.Genre);

            if (!string.IsNullOrEmpty(metadata.Title))
                UpdateMetadataValue("Title", metadata.Title);
        });
    }

    private void UpdateMetadataValue(string label, string value)
    {
        if (_metadataValueMarquees.TryGetValue(label, out MarqueeText marquee))
        {
            if (marquee != null)
            {
                marquee.SetText(value);
            }
        }
    }

    private void CreateMetadataForFolder(MockFile file, bool isCurrentFolder)
    {
        // Folder: Type, Date modified
        AddMetadataRow("Type", "Folder");
        AddMetadataRow("Date modified", FormatDate(file.Modified));
    }

    private void CreateMetadataForImage(MockFile file)
    {
        // Image: Type, Size, Date modified, Dimensions
        AddMetadataRow("Type", file.Type.ToUpper());
        AddMetadataRow("Size", FileSystemService.FormatFileSize(file.Size));
        AddMetadataRow("Date modified", FormatDate(file.Modified));

        string dimensions = (file.Width > 0 && file.Height > 0)
            ? $"{file.Width}x{file.Height}"
            : "-";
        AddMetadataRow("Dimensions", dimensions);
    }

    private void CreateMetadataForVideo(MockFile file)
    {
        // Video: Type, Size, Date modified, Dimensions, Length, Frame rate, Total bitrate
        AddMetadataRow("Type", file.Type.ToUpper());
        AddMetadataRow("Size", FileSystemService.FormatFileSize(file.Size));
        AddMetadataRow("Date modified", FormatDate(file.Modified));

        string dimensions = (file.Width > 0 && file.Height > 0)
            ? $"{file.Width}x{file.Height}"
            : "-";
        AddMetadataRow("Dimensions", dimensions);

        AddMetadataRow("Length", FormatDuration(file.Duration));

        string frameRate = file.FrameRate > 0 ? $"{file.FrameRate:F2} fps" : "-";
        AddMetadataRow("Frame rate", frameRate);

        string totalBitrate = file.TotalBitrate > 0 ? FormatBitrate(file.TotalBitrate) : "-";
        AddMetadataRow("Total bitrate", totalBitrate);
    }

    private void CreateMetadataForMusic(MockFile file)
    {
        // Music: Type, Size, Date modified, Contributing artists, Album, Genre, Length, Title, Bit rate
        AddMetadataRow("Type", file.Type.ToUpper());
        AddMetadataRow("Size", FileSystemService.FormatFileSize(file.Size));
        AddMetadataRow("Date modified", FormatDate(file.Modified));

        string artist = !string.IsNullOrEmpty(file.Artist) ? file.Artist : "-";
        AddMetadataRow("Contributing artists", artist);

        string album = !string.IsNullOrEmpty(file.Album) ? file.Album : "-";
        AddMetadataRow("Album", album);

        string genre = !string.IsNullOrEmpty(file.Genre) ? file.Genre : "-";
        AddMetadataRow("Genre", genre);

        AddMetadataRow("Length", FormatDuration(file.Duration));

        string title = !string.IsNullOrEmpty(file.Title) ? file.Title : "-";
        AddMetadataRow("Title", title);

        string bitrate = file.BitRate > 0 ? $"{file.BitRate} kbps" : "-";
        AddMetadataRow("Bit rate", bitrate);
    }

    private void CreateMetadataForOther(MockFile file)
    {
        // Other: Type, Size, Date modified
        string fileType = !string.IsNullOrEmpty(file.Type) ? file.Type.ToUpper() : (file.IsFolder ? "FOLDER" : "FILE");
        AddMetadataRow("Type", fileType);
        AddMetadataRow("Size", FileSystemService.FormatFileSize(file.Size));
        AddMetadataRow("Date modified", FormatDate(file.Modified));
    }

    // Fixed label width to align all values consistently
    private const float LABEL_WIDTH = 210f;

    private void AddMetadataRow(string label, string value)
    {
        GameObject rowObj = new GameObject($"Row_{label}");
        rowObj.transform.SetParent(_metadataContainer, false);

        RectTransform rowRT = rowObj.AddComponent<RectTransform>();
        rowRT.sizeDelta = new Vector2(0, METADATA_ROW_HEIGHT);

        // Label (fixed width on left)
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(rowObj.transform, false);

        RectTransform labelRT = labelObj.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0, 0);
        labelRT.anchorMax = new Vector2(0, 1);
        labelRT.pivot = new Vector2(0, 0.5f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta = new Vector2(LABEL_WIDTH, 0);

        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) labelText.font = _font;
        labelText.fontSize = 30;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.color = new Color(1f, 1f, 1f, 0.85f); // Semi-transparent white
        labelText.text = label;
        labelText.enableWordWrapping = false;
        labelText.overflowMode = TextOverflowModes.Ellipsis;

        // Value (starts after label with spacing, stretches to right)
        GameObject valueObj = new GameObject("Value");
        valueObj.transform.SetParent(rowObj.transform, false);

        RectTransform valueRT = valueObj.AddComponent<RectTransform>();
        valueRT.anchorMin = new Vector2(0, 0);
        valueRT.anchorMax = new Vector2(1, 1);
        valueRT.offsetMin = new Vector2(LABEL_WIDTH + 10f, 0);
        valueRT.offsetMax = Vector2.zero;

        TextMeshProUGUI valueText = valueObj.AddComponent<TextMeshProUGUI>();
        if (_font != null) valueText.font = _font;
        valueText.fontSize = 30;
        valueText.fontStyle = FontStyles.Bold;
        valueText.alignment = TextAlignmentOptions.MidlineLeft;
        valueText.color = Color.white; // Full white
        valueText.enableWordWrapping = false;
        valueText.overflowMode = TextOverflowModes.Overflow;

        // Setup marquee for auto-scrolling long values
        MarqueeText marquee = MarqueeText.Setup(valueText, 40f);
        marquee.SetText(value);

        // Store reference for async updates
        _metadataValueMarquees[label] = marquee;
    }

    #region Formatting Helpers
    private string FormatPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "-";

        // Get directory path (parent folder)
        string directory = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return path;

        // Shorten if too long
        if (directory.Length > 40)
        {
            return "..." + directory.Substring(directory.Length - 37);
        }
        return directory;
    }

    private string FormatDate(DateTime date)
    {
        if (date == DateTime.MinValue) return "-";
        return date.ToString("MMM dd, yyyy");
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0) return "-";

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} hr {duration.Minutes} min";
        }
        else if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} min {duration.Seconds} sec";
        }
        else
        {
            return $"{duration.Seconds} sec";
        }
    }

    private string FormatBitrate(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "-";

        double kbps = bitsPerSecond / 1000.0;
        if (kbps >= 1000)
        {
            return $"{kbps / 1000:F1} Mbps";
        }
        return $"{kbps:F0} kbps";
    }
    #endregion
}
