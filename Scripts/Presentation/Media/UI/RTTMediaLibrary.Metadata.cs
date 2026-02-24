using UnityEngine;
using TMPro;
using System;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.Components;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibrary partial: Detail panel data binding, favourite state, breadcrumb/category
    /// display logic, and MediaVideoInfo-to-MockFile conversion.
    /// </summary>
    public partial class RTTMediaLibrary
    {
        #region Detail Panel / Metadata Display

        /// <summary>
        /// Update the detail panel with video info.
        /// Called by controller based on hover/select state.
        /// </summary>
        /// <param name="video">Video info to display</param>
        /// <param name="forceRefresh">Force refresh even if same file path (used after data reload)</param>
        public void UpdateDetailPanel(MediaVideoInfo video, bool forceRefresh = false)
        {
            if (_detailPanel != null)
            {
                var mockFile = ConvertToMockFile(video);
                _detailPanel.UpdateInfo(mockFile, isCurrentFolder: false, forceRefresh: forceRefresh);
            }

            // Update current video for action buttons if this is the selected item (not just hovered)
            // The _currentVideo is set in OnGridVideoSelected, so we don't overwrite it here
            // This ensures the action buttons (Play, Favorite) work on the selected item

            // Update favourite icon state based on displayed video
            UpdateFavouriteButtonState(video.IsFavorite);

            // ActionBar is always visible for Media Library (when not in edit mode)
            // No need to toggle visibility here - it's managed in CreateMediaActionBar and ToggleEditMode
        }

        /// <summary>
        /// Clear the detail panel when no item is selected or hovered.
        /// Shows empty state in edit mode, otherwise keeps last item.
        /// </summary>
        public void ClearDetailPanel()
        {
            // In edit mode, show empty state
            if (_isEditMode)
            {
                _detailPanel?.ShowEmpty();
            }
            // Outside edit mode, keep showing last item (auto-select behavior)
        }

        /// <summary>
        /// Notify the view that a video was auto-selected by the controller.
        /// This is called when controller auto-selects first item on startup.
        /// </summary>
        public void NotifyVideoAutoSelected(MediaVideoInfo video)
        {
            _hasSelectedVideo = true;
            _currentVideo = video;

            // ActionBar is always visible for Media Library (managed in CreateMediaActionBar)
            // No need to toggle visibility here
        }

        private void UpdateFavouriteButtonState(bool isFavourite)
        {
            if (_mediaActionBar != null)
            {
                _mediaActionBar.UpdateFavouriteState(isFavourite);
            }
        }

        /// <summary>
        /// Convert MediaVideoInfo to MockFile for RTTFileDetail compatibility.
        /// </summary>
        private MockFile ConvertToMockFile(MediaVideoInfo video)
        {
            return new MockFile
            {
                Path = video.Path,
                Name = System.IO.Path.GetFileName(video.Path),  // Full filename with extension
                Type = System.IO.Path.GetExtension(video.Path).TrimStart('.').ToUpperInvariant(),
                Size = video.FileSizeBytes,
                Modified = video.DateAdded,
                IsFolder = false,
                Width = video.Width,
                Height = video.Height,
                Duration = video.Duration
            };
        }

        #endregion

        #region Breadcrumb / Category Display

        public void UpdateBreadcrumb(string path)
        {
            // Fallback: find breadcrumb container from hierarchy if reference is lost
            if (_breadcrumbContainer == null)
            {
                Debug.LogWarning($"[RTTMediaLibrary] Breadcrumb container reference lost (instanceID={GetInstanceID()}), searching in hierarchy...");
                var row2 = _headerRT?.Find("Row2");
                if (row2 != null)
                {
                    _breadcrumbContainer = row2.Find("Breadcrumbs");
                    Debug.Log($"[RTTMediaLibrary] Found breadcrumb container from hierarchy: {_breadcrumbContainer != null}");
                }
            }

            if (_breadcrumbContainer == null)
            {
                Debug.LogWarning("[RTTMediaLibrary] UpdateBreadcrumb: _breadcrumbContainer is null and could not be found!");
                return;
            }

            // Clear existing breadcrumbs (use reverse for loop to avoid collection modification issues)
            for (int i = _breadcrumbContainer.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(_breadcrumbContainer.GetChild(i).gameObject);
            }

            // Parse path into segments (split by " > ")
            string[] segments = path.Split(new[] { " > " }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                segments = new[] { path };
            }

            Debug.Log($"[RTTMediaLibrary] Creating {segments.Length} breadcrumb segments for: {path}");

            // Breadcrumb button dimensions (match RTTFileManager)
            float btnHeight = 75f;  // Match RTTFileManager
            float btnWidth = _sortTriggerWidth * 1.2f;  // Use consistent width like RTTFileManager
            float overlap = btnHeight * 0.45f;

            // Create buttons in REVERSE order like RTTFileManager for proper GraphicRaycaster priority
            // Left buttons created LAST = higher sibling index = hit first
            float effectiveWidth = btnWidth - overlap;
            int totalCount = segments.Length;

            for (int i = totalCount - 1; i >= 0; i--)
            {
                string label = segments[i].Trim();
                bool isFirst = (i == 0);
                bool isLast = (i == totalCount - 1);

                // Create pill button
                GameObject btn = CreateBreadcrumbPill(label, btnWidth, btnHeight, isFirst, isLast, i);

                RectTransform btnRT = btn.GetComponent<RectTransform>();
                btnRT.anchorMin = new Vector2(0, 0.5f);
                btnRT.anchorMax = new Vector2(0, 0.5f);
                btnRT.pivot = new Vector2(0, 0.5f);

                // X position: each button offset by effectiveWidth
                float xPos = i * effectiveWidth;
                btnRT.anchoredPosition = new Vector2(xPos, 0);

                // Z-position for visual layering (left buttons closer to camera)
                Vector3 pos = btnRT.localPosition;
                pos.z = i * -0.5f;
                btnRT.localPosition = pos;
            }

            Debug.Log($"[RTTMediaLibrary] Created {totalCount} breadcrumb pills for path: {path}");
        }

        /// <summary>
        /// Create a pill-shaped breadcrumb button (styled like RTTFileManager but not clickable).
        /// </summary>
        private GameObject CreateBreadcrumbPill(string label, float width, float height, bool isFirst, bool isLast, int index)
        {
            // Only use accent color for the last item if there are multiple breadcrumbs
            // When there's only one breadcrumb (isFirst && isLast), use primary color
            Color btnColor = (isLast && !isFirst) ? _accentColor : _primaryColor;

            GameObject btnObj = new GameObject($"Crumb_{label}");
            btnObj.layer = LayerMask.NameToLayer("UI");
            btnObj.transform.SetParent(_breadcrumbContainer, false);

            RectTransform btnRT = btnObj.AddComponent<RectTransform>();
            btnRT.sizeDelta = new Vector2(width, height);

            // Background image with shader
            UnityEngine.UI.Image bgImage = btnObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.raycastTarget = false; // Not clickable

            float aspect = width / height;
            float glassAlpha = 0.2f;

            if (isFirst)
            {
                // First button: rounded rect
                Shader pillShader = Shader.Find("Custom/GlassGradientBackgroundWide");
                if (pillShader != null)
                {
                    Material mat = new Material(pillShader);
                    mat.SetFloat("_Aspect", aspect);
                    mat.SetFloat("_CornerRadius", 0.48f);
                    mat.SetFloat("_EdgePadding", 0.02f);
                    Color colorA = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 1.5f);
                    Color colorB = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha * 0.5f);
                    mat.SetColor("_ColorA", colorA);
                    mat.SetColor("_ColorB", colorB);
                    mat.SetFloat("_GlassAlpha", glassAlpha);
                    mat.SetFloat("_FresnelStrength", 0.15f);
                    bgImage.material = mat;
                    bgImage.color = Color.white;
                }
                else
                {
                    bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
                }
            }
            else
            {
                // Subsequent buttons: chevron shape
                Shader chevronShader = Shader.Find("Custom/ChevronBackground");
                if (chevronShader != null)
                {
                    Material mat = new Material(chevronShader);
                    mat.SetFloat("_Aspect", aspect);
                    mat.SetFloat("_EdgePadding", 0.02f);
                    mat.SetColor("_BackgroundColor", btnColor);
                    mat.SetFloat("_BackgroundAlpha", glassAlpha);
                    mat.SetFloat("_EdgeGlow", 0.15f);
                    mat.SetFloat("_CenterGlow", 0.1f);
                    bgImage.material = mat;
                    bgImage.color = Color.white;
                }
                else
                {
                    bgImage.color = new Color(btnColor.r, btnColor.g, btnColor.b, glassAlpha);
                }
            }

            // Text label
            GameObject textObj = new GameObject("Text");
            textObj.layer = LayerMask.NameToLayer("UI");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;

            float curveR = height * 0.5f;
            if (isFirst)
            {
                textRT.offsetMin = new Vector2(curveR * 0.85f, 0);
                textRT.offsetMax = new Vector2(-curveR * 0.75f, 0);
            }
            else
            {
                textRT.offsetMin = new Vector2(curveR * 1.1f, 0);
                textRT.offsetMax = new Vector2(-curveR * 0.6f, 0);
            }

            TextMeshProUGUI txt = textObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 24;
            txt.font = _font;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
            txt.fontStyle = FontStyles.Bold;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Ellipsis;

            return btnObj;
        }

        private void UpdateBreadcrumbForCategory(string categoryId)
        {
            string breadcrumbText;

            switch (categoryId)
            {
                case "all":
                    breadcrumbText = "All Media";
                    break;
                case "videos":
                    breadcrumbText = "All Media > Videos";
                    break;
                case "images":
                    breadcrumbText = "All Media > Images";
                    break;
                case "audio":
                    breadcrumbText = "All Media > Music";
                    break;
                case "recent":
                    breadcrumbText = "Recent";
                    break;
                case "favorites":
                    breadcrumbText = "Favorites";
                    break;
                case "playlists":
                    breadcrumbText = "Playlists";
                    break;
                default:
                    breadcrumbText = categoryId;
                    break;
            }

            UpdateBreadcrumb(breadcrumbText);
        }

        #endregion
    }
}
