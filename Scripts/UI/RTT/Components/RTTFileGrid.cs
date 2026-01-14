using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Grid View for File Manager.
/// Displays a grid of files/folders in a ScrollView.
/// Supports programmatic scrolling for Pagination.
/// </summary>
public class RTTFileGrid : MonoBehaviour
{
    private RTTFileManagerController _controller;
    private float _width;
    private float _height;
    
    private ScrollRect _scrollRect;
    private GridLayoutGroup _gridLayout;
    private RectTransform _contentRect;
    
    private List<RTTFileGridItem> _items = new List<RTTFileGridItem>();
    
    // Configuration
    private float _cellWidth = 310f; // Safely fits 5 cols in 1730px space (5*310=1550 + 120 spacing = 1670)
    private float _cellHeight = 320f; // Increased layout height (150 icon + 30 spacing + 90 text + 20 pad = 290+)
    private float _spacingX = 30f;
    private float _spacingY = 30f;
    // private int _cols = 4; // Unused
    
    public void Initialize(RTTFileManagerController controller, float w, float h)
    {
        _controller = controller;
        _width = w;
        _height = h;

        BuildUI();
    }

    private void BuildUI()
    {
        // 1. Setup ScrollRect
        _scrollRect = gameObject.AddComponent<ScrollRect>();
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.scrollSensitivity = 20f;
        _scrollRect.movementType = ScrollRect.MovementType.Elastic; // or Clamped
        
        // 2. Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(transform, false);
        RectTransform vpRect = viewport.AddComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.offsetMin = Vector2.zero;
        vpRect.offsetMax = Vector2.zero;
        
        // Use Mask + Image for robust clipping
        Image maskImg = viewport.AddComponent<Image>();
        maskImg.color = Color.white;
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        
        _scrollRect.viewport = vpRect;

        // 3. Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _contentRect = content.AddComponent<RectTransform>();
        _contentRect.anchorMin = new Vector2(0, 1);
        _contentRect.anchorMax = new Vector2(1, 1); // Stretch width, top aligned
        _contentRect.pivot = new Vector2(0.5f, 1);
        _contentRect.sizeDelta = Vector2.zero; // Height will set by fitter
        
        _scrollRect.content = _contentRect;

        // 4. Grid Layout
        _gridLayout = content.AddComponent<GridLayoutGroup>();
        _gridLayout.cellSize = new Vector2(_cellWidth, _cellHeight);
        _gridLayout.spacing = new Vector2(_spacingX, _spacingY);
        _gridLayout.padding = new RectOffset(20, 20, 20, 60); // Reduced side padding. Top padding handled by content offset.
        _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        _gridLayout.childAlignment = TextAnchor.UpperCenter; // Center the grid
        
        // Content Size Fitter
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Populate(List<MockFile> files)
    {
        // Clear existing
        foreach (var item in _items)
        {
            Destroy(item.gameObject);
        }
        _items.Clear();

        // Create new items
        foreach (var file in files)
        {
            GameObject itemObj = new GameObject($"Item_{file.Name}");
            itemObj.transform.SetParent(_contentRect, false);
            
            var gridItem = itemObj.AddComponent<RTTFileGridItem>();
            gridItem.Initialize(file.Name, file.IsFolder, file.Path, OnItemClicked, OnItemHover);
            
            _items.Add(gridItem);
        }
    }
    
    public void ScrollToPage(int pageIndex, int rowsPerPage)
    {
        if (_scrollRect == null || _contentRect == null) return;
        
        // Calculate Y position for the row
        // Page 1 (index 0) = Row 0
        // Page 2 (index 1) = Row (rowsPerPage)
        
        // Height of one row = CellHeight + SpacingY
        float rowHeight = _cellHeight + _spacingY;
        
        // Target Row Index
        int targetRow = (pageIndex - 1) * rowsPerPage;
        
        // Y Position (Content is Top-aligned, so Y increases downwards/negative)
        // ScrollRect content position is usually positive to scroll down
        float targetY = targetRow * rowHeight;
        
        // Clamp to max scroll info if needed, but ScrollRect handles content bounds
        // Just set anchoredPosition
        
        // We might need to consider top padding
        targetY += _gridLayout.padding.top; // Adjust for padding? Usually 0 is top.
        // If content is at Y=0, we see top. If we want row 1, we move content UP, so Y becomes Positive.
        
        // Wait, RectTransform coordinate system:
        // Pivot (0.5, 1) Top Center.
        // Initial Pos Y=0.
        // To scroll down (see lower items), Content moves UP (Positive Y).
        
        if (pageIndex == 1) targetY = 0; // Force top
        
        _contentRect.anchoredPosition = new Vector2(_contentRect.anchoredPosition.x, targetY);
    }

    private void OnItemHover(RTTFileGridItem item, bool isHover)
    {
        if (isHover)
        {
             _controller.HoverFile(item.FilePath);
        }
        else
        {
             _controller.UnhoverFile(item.FilePath);
        }
    }

    private void OnItemClicked(RTTFileGridItem item)
    {
        Debug.Log($"[RTTFileGrid] Clicked: {item.FilePath}");
        
        foreach (var i in _items)
        {
            i.SetSelected(i == item);
        }

        if (item.IsFolder)
        {
            _controller.NavigateTo(item.FilePath);
        }
        else
        {
            _controller.SelectFile(item.FilePath);
        }
    }
}
