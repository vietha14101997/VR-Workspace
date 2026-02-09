using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Popup for displaying and managing the current playback queue/playlist.
/// Allows selecting videos to play.
/// </summary>
public class RTTMediaQueuePopup : MonoBehaviour
{
    #region Constants
    private const float POPUP_WIDTH = 500f;
    private const float POPUP_HEIGHT = 600f;
    private const float HEADER_HEIGHT = 60f;
    private const float ITEM_HEIGHT = 50f;
    private const float PADDING = 20f;
    private const float SPACING = 5f;
    #endregion

    #region Events
    public event Action<string> OnVideoSelected; // Returns video path
    public event Action OnCloseRequested;
    #endregion

    #region Private Fields
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private GameObject _popup;
    private Transform _contentContainer;
    private List<GameObject> _itemObjects = new List<GameObject>();
    private string _currentVideoPath;
    #endregion

    #region Initialization
    public void Initialize(TMP_FontAsset font, Color primary, Color accent)
    {
        _font = font;
        _primaryColor = primary;
        _accentColor = accent;

        BuildUI();
        Hide();
    }

    private void BuildUI()
    {
        // Main popup container
        _popup = new GameObject("QueuePopup");
        _popup.transform.SetParent(transform, false);

        var popupRT = _popup.AddComponent<RectTransform>();
        popupRT.sizeDelta = new Vector2(POPUP_WIDTH, POPUP_HEIGHT);

        // Background
        var bg = _popup.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        
        // Add canvas group for fade animations if needed
        var cg = _popup.AddComponent<CanvasGroup>();

        // Header
        CreateHeader();

        // Scroll View
        CreateScrollView();
    }

    private void CreateHeader()
    {
        GameObject headerObj = new GameObject("Header");
        headerObj.transform.SetParent(_popup.transform, false);

        var headerRT = headerObj.AddComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0, 1);
        headerRT.anchorMax = new Vector2(1, 1);
        headerRT.pivot = new Vector2(0.5f, 1);
        headerRT.anchoredPosition = Vector2.zero;
        headerRT.sizeDelta = new Vector2(0, HEADER_HEIGHT);

        // Header background
        var headerBg = headerObj.AddComponent<Image>();
        headerBg.color = new Color(0, 0, 0, 0.3f);

        // Title
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(headerObj.transform, false);

        var titleRT = titleObj.AddComponent<RectTransform>();
        titleRT.anchorMin = Vector2.zero;
        titleRT.anchorMax = Vector2.one;
        titleRT.offsetMin = new Vector2(PADDING, 0);
        titleRT.offsetMax = new Vector2(-60, 0);

        var titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "PLAY QUEUE";
        titleText.font = _font;
        titleText.fontSize = 24;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.MidlineLeft;

        // Close button
        CreateCloseButton(headerObj.transform);
    }

    private void CreateCloseButton(Transform parent)
    {
        GameObject buttonObj = new GameObject("CloseButton");
        buttonObj.transform.SetParent(parent, false);

        var buttonRT = buttonObj.AddComponent<RectTransform>();
        buttonRT.anchorMin = new Vector2(1, 0.5f);
        buttonRT.anchorMax = new Vector2(1, 0.5f);
        buttonRT.pivot = new Vector2(1, 0.5f);
        buttonRT.anchoredPosition = new Vector2(-15, 0);
        buttonRT.sizeDelta = new Vector2(40, 40);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = new Color(1, 1, 1, 0.1f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;
        button.onClick.AddListener(() => OnCloseRequested?.Invoke());

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "X";
        text.font = _font;
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(40, 40, 10);
        collider.center = new Vector3(0, 0, -5);
    }

    private void CreateScrollView()
    {
        GameObject scrollObj = new GameObject("ScrollView");
        scrollObj.transform.SetParent(_popup.transform, false);

        var scrollRT = scrollObj.AddComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero;
        scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = new Vector2(PADDING, PADDING);
        scrollRT.offsetMax = new Vector2(-PADDING, -HEADER_HEIGHT - 10);

        var scrollRect = scrollObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;

        // Viewport
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        
        var viewportRT = viewportObj.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;

        var mask = viewportObj.AddComponent<RectMask2D>();
        scrollRect.viewport = viewportRT;

        // Content
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);

        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1);
        contentRT.sizeDelta = new Vector2(0, 0); // Height driven by layout

        var layout = contentObj.AddComponent<VerticalLayoutGroup>();
        layout.spacing = SPACING;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        
        var csf = contentObj.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = contentRT;
        _contentContainer = contentObj.transform;
        
        // Add scrollbar (optional/simplified)
    }
    #endregion

    #region Public Methods
    public void Show()
    {
        gameObject.SetActive(true);
        _popup.SetActive(true);
        RefreshQueue();
    }

    public void Hide()
    {
        _popup.SetActive(false);
        gameObject.SetActive(false);
    }

    public void SetCurrentVideo(string path)
    {
        _currentVideoPath = path;
        RefreshHighlight();
    }
    #endregion

    #region Private Methods
    private void RefreshQueue()
    {
        // Clear items
        foreach (var item in _itemObjects)
        {
            Destroy(item);
        }
        _itemObjects.Clear();

        // Get queue from service
        var service = MediaPlaylistService.Instance;
        if (service == null) return;

        // We need access to the queue. MediaPlaylistService doesn't expose the full queue list publicly,
        // but it has GetPlaylistVideos. 
        // NOTE: MediaPlaylistService.SetPlaybackPlaylist overwrites the queue.
        // We might need to inspect the queue or just show the current playlist.
        // For now, let's assume we want to show the currently ACTIVE playlist if possible.
        // But MediaPlaylistService doesn't expose "GetCurrentPlaylistId".
        
        // LIMITATION: MediaPlaylistService hides _playbackQueue. 
        // Accessing it might require an update to MediaPlaylistService or just showing the items.
        // Assuming we can't access _playbackQueue directly, let's look at the API.
        // GetNextVideo, GetPreviousVideo. Is there "GetAllQueueVideos"? No.
        
        // Ideally MediaPlaylistService should expose the queue. 
        // I will assume for now I should add 'GetQueue()' to MediaPlaylistService or I will update it.
        // Since I'm making progress, I will update MediaPlaylistService next.
        // For now, I'll add a placeholder or rely on a "CurrentPlaylist" concept if I add it.
        
        // Let's modify MediaPlaylistService to expose the queue first? 
        // Yes, that's better design.
        
        // HOLD ON: I'll finish this file assuming GetPlaybackQueue() exists, then update MediaPlaylistService.
    }
    
    // TEMPORARY: I'll write the update logic for RefreshQueue assuming I'll add GetPlaybackQueue to the service.
    public void RefreshQueueWithList(List<string> videoPaths)
    {
        // Clear items
        foreach (var item in _itemObjects)
        {
            Destroy(item);
        }
        _itemObjects.Clear();
        
        if (videoPaths == null) return;

        for (int i = 0; i < videoPaths.Count; i++)
        {
            string path = videoPaths[i];
            CreateQueueItem(i, path);
        }
        
        RefreshHighlight();
    }

    private void CreateQueueItem(int index, string path)
    {
        GameObject itemObj = new GameObject($"Item_{index}");
        itemObj.transform.SetParent(_contentContainer, false);

        var le = itemObj.AddComponent<LayoutElement>();
        le.minHeight = ITEM_HEIGHT;
        le.preferredHeight = ITEM_HEIGHT;

        // Background
        var bg = itemObj.AddComponent<Image>();
        bg.color = new Color(1, 1, 1, 0.05f);

        var btn = itemObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(() => OnVideoSelected?.Invoke(path));

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(itemObj.transform, false);
        
        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10, 0);
        textRT.offsetMax = new Vector2(-10, 0);

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = System.IO.Path.GetFileName(path);
        text.font = _font;
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Ellipsis;

        // Collider
        var collider = itemObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(POPUP_WIDTH - PADDING * 2, ITEM_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        // Store for highlighting
        var itemData = itemObj.AddComponent<QueueItemData>();
        itemData.Path = path;
        itemData.Background = bg;
        itemData.Text = text;

        _itemObjects.Add(itemObj);
    }

    private void RefreshHighlight()
    {
        foreach (var obj in _itemObjects)
        {
            var data = obj.GetComponent<QueueItemData>();
            if (data != null)
            {
                bool isSelected = data.Path == _currentVideoPath;
                data.Background.color = isSelected ? _primaryColor : new Color(1, 1, 1, 0.05f);
                data.Text.color = isSelected ? Color.black : Color.white;
            }
        }
    }

    private class QueueItemData : MonoBehaviour
    {
        public string Path;
        public Image Background;
        public TextMeshProUGUI Text;
    }
    #endregion
}
