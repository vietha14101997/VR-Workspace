using UnityEngine;
using System.Collections.Generic;
using VRWorkspace.Media.Data;
using VRWorkspace.UI.Config;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// RTTMediaLibrary partial: Rename/Delete popups, and grid/action event handlers.
    /// </summary>
    public partial class RTTMediaLibrary
    {
        private void OnCategorySelected(string categoryId)
        {
            // Exit edit mode when changing category
            if (_isEditMode)
            {
                ToggleEditMode();
            }

            // Clear video selection when changing category
            _hasSelectedVideo = false;
            // ActionBar stays visible for Media Library - don't hide on category change

            _controller?.SelectCategory(categoryId);
            UpdateBreadcrumbForCategory(categoryId);
        }

        private void OnGridVideoSelected(MediaVideoInfo video)
        {
            // In edit mode, don't process video selection (checkboxes are handled separately)
            if (_isEditMode) return;

            _hasSelectedVideo = true;
            _currentVideo = video;

            // Notify controller of selection (controller manages detail panel via hover/select logic)
            _controller?.SelectVideo(video);

            // Update favourite icon state
            UpdateFavouriteButtonState(video.IsFavorite);

            // Show action bar with fade animation only if Media app is the current visible app
            // During dwell pre-loading, CurrentVisibleAppId is null (main menu) or another app
            // OnEnable will show ActionBar when app is actually opened/re-opened
            bool isMediaAppVisible = RTTManager.Instance?.CurrentVisibleAppId == "media";
            if (isMediaAppVisible)
            {
                _mediaActionBar?.ShowWithFade();
            }
        }

        private void OnGridVideoHoverEnter(MediaVideoInfo video)
        {
            _controller?.HoverVideo(video);
        }

        private void OnGridVideoHoverExit(MediaVideoInfo video)
        {
            _controller?.UnhoverVideo(video);
        }

        private void OnSelectionChanged()
        {
            // Sync _selectedItems with grid's selection
            _selectedItems.Clear();
            if (_grid != null)
            {
                foreach (var path in _grid.GetSelectedPaths())
                {
                    _selectedItems.Add(path);
                }
            }

            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
            UpdateActionButtonsState();
        }

        private void OnGridVideoDoubleClicked(MediaVideoInfo video)
        {
            OnVideoPlayRequested?.Invoke(video);
        }

        private void OnGridPageChanged(int currentPage, int totalPages)
        {
            UpdatePagination();
        }

        // Action Button Handlers
        private void OnPlayButtonClicked()
        {
            if (_currentVideo.HasValue)
            {
                OnVideoPlayRequested?.Invoke(_currentVideo.Value);
            }
        }

        private void OnFavouriteButtonClicked()
        {
            if (_currentVideo.HasValue)
            {
                _controller?.ToggleFavorite(_currentVideo.Value);

                // Toggle the state locally for immediate UI feedback
                var video = _currentVideo.Value;
                video.IsFavorite = !video.IsFavorite;
                _currentVideo = video;
                UpdateFavouriteButtonState(video.IsFavorite);
            }
        }

        private void OnPlaylistButtonClicked()
        {
            if (_currentVideo.HasValue)
            {
                Debug.Log($"[RTTMediaLibrary] Add to playlist requested for: {_currentVideo.Value.Title}");
                // TODO: Show playlist selection dialog
            }
        }

        private void OnSearchValueChanged(string value)
        {
            _controller?.Search(value);
        }

        private void OnSearchEndEdit(string value)
        {
            // Optional: Additional handling when search is submitted
        }

        private void OnCloseButtonClicked()
        {
            OnCloseRequested?.Invoke();
        }

        private void OnEditButtonClicked()
        {
            ToggleEditMode();
        }

        private void OnSelectAllClicked()
        {
            if (_grid == null) return;

            bool allSelected = _grid.AreAllSelected();
            _grid.SetAllSelected(!allSelected);

            // Sync _selectedItems with grid's selection
            _selectedItems.Clear();
            foreach (var path in _grid.GetSelectedPaths())
            {
                _selectedItems.Add(path);
            }

            UpdateSelectAllCheckmark();
            UpdateSelectedCountText();
            UpdateActionButtonsState();
        }

        private void OnRenameClicked()
        {
            if (_selectedItems.Count != 1)
            {
                Debug.LogWarning("[RTTMediaLibrary] Rename requires exactly one selected item");
                return;
            }

            // Get the selected path
            foreach (var path in _selectedItems)
            {
                _renameTargetPath = path;
                break;
            }

            Debug.Log($"[RTTMediaLibrary] Rename requested for: {_renameTargetPath}");
            ShowRenamePopup();
        }

        private void OnDeleteClicked()
        {
            if (_selectedItems.Count == 0)
            {
                Debug.LogWarning("[RTTMediaLibrary] No items selected for delete");
                return;
            }

            Debug.Log($"[RTTMediaLibrary] Delete requested for {_selectedItems.Count} items");
            ShowDeleteConfirmPopup();
        }

        private void CreateRenamePopup()
        {
            if (_renamePopup != null) return;

            var config = new RTTPopupInputable.PopupConfig
            {
                title = "Rename",
                inputLabel = "New Name",
                inputPlaceholder = "Enter new name",
                buttonText = "Rename",
                width = 575f,
                padding = 33f,
                titleFontSize = 31,
                labelFontSize = 24,
                inputFontSize = 29,
                buttonFontSize = 26,
                buttonHeight = 72f,
                inputHeight = 72f,
                titleHeight = 55f,
                closeButtonSize = 50f,
                spacing = 22f,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, 0.4f),
                font = _font,
                layerName = "VirtualObjects"
            };

            _renamePopup = RTTPopupInputable.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowRenamePopup()
        {
            if (string.IsNullOrEmpty(_renameTargetPath))
            {
                Debug.LogWarning("[RTTMediaLibrary] No target path for rename");
                return;
            }

            // Create popup if not exists
            if (_renamePopup == null)
            {
                CreateRenamePopup();
            }

            // Get current name from path
            string currentName = System.IO.Path.GetFileName(_renameTargetPath);

            // Set default value to current name
            _renamePopup.SetDefaultValue(currentName);

            // Show with callbacks
            _renamePopup.Show(
                onConfirm: OnRenameConfirmed,
                onCancel: OnRenameCancelled
            );
        }

        private void OnRenameConfirmed(string newName)
        {
            Debug.Log($"[RTTMediaLibrary] Rename '{_renameTargetPath}' to '{newName}'");

            if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(_renameTargetPath))
            {
                Debug.LogWarning("[RTTMediaLibrary] Invalid rename parameters");
                return;
            }

            // Request controller to rename the item
            if (_controller != null)
            {
                _controller.RenameItem(_renameTargetPath, newName);
            }

            // Exit edit mode after rename (this will also clear selection)
            if (_isEditMode)
            {
                ToggleEditMode();
            }

            _renameTargetPath = null;
        }

        private void OnRenameCancelled()
        {
            Debug.Log("[RTTMediaLibrary] Rename cancelled");
            _renameTargetPath = null;
        }

        private void CreateDeleteConfirmPopup()
        {
            if (_deleteConfirmPopup != null) return;

            // Standardized Yes/No popup config using UIConstants for consistent styling
            var config = new RTTPopupMenu.PopupConfig
            {
                width = 550f,
                buttonHeight = 66f,
                sideSpacing = 26f,
                rowSpacing = 16f,
                labelHeight = 52f,
                labelFontSize = 32,
                fontSize = 25,
                borderWidth = UIConstants.PopupBorderWidth,
                glassAlpha = UIConstants.PopupGlassAlpha,
                primaryColor = _primaryColor,
                accentColor = _accentColor,
                overlayColor = new Color(0f, 0f, 0f, UIConstants.PopupOverlayAlpha),
                font = _font,
                layerName = UIConstants.VirtualObjectsLayer,
                buttonBorderWidth = UIConstants.PopupButtonBorderWidth,
                buttonGlowWidth = UIConstants.PopupButtonGlowWidth,
                buttonGlowIntensity = UIConstants.PopupButtonGlowIntensity,
                buttonCornerRadius = UIConstants.PopupButtonCornerRadius
            };

            _deleteConfirmPopup = RTTPopupMenu.CreateWorldSpace(config, _menuFrame.transform);
        }

        private void ShowDeleteConfirmPopup()
        {
            if (_deleteConfirmPopup == null)
            {
                CreateDeleteConfirmPopup();
            }

            // Clear previous content and rebuild
            _deleteConfirmPopup.Clear();

            // Build confirmation message
            int count = _selectedItems.Count;
            string itemText = count == 1 ? "item" : "items";
            string title = $"Delete {count} {itemText}?";

            // Add title section (centered)
            _deleteConfirmPopup.AddSectionBlock(title, new List<RTTPopupMenu.ButtonData>(), 2, centerTitle: true);

            // Add Yes/No buttons (accent for Yes, primary for No)
            var yesButton = new RTTPopupMenu.ButtonData(
                "Yes",
                OnDeleteConfirmed,
                null,
                false,
                _accentColor // Accent color (magenta/pink)
            );

            var noButton = new RTTPopupMenu.ButtonData(
                "No",
                OnDeleteCancelled,
                null,
                false,
                _primaryColor // Primary color (cyan)
            );

            _deleteConfirmPopup.AddSectionBlock("", new List<RTTPopupMenu.ButtonData> { yesButton, noButton }, 2);

            _deleteConfirmPopup.Build();
            _deleteConfirmPopup.Show();
        }

        private void OnDeleteConfirmed()
        {
            // Copy paths before clearing selection
            var pathsToDelete = new List<string>(_selectedItems);
            Debug.Log("[RTTMediaLibrary] Delete confirmed - deleting " + pathsToDelete.Count + " items");

            // Hide confirmation popup
            if (_deleteConfirmPopup != null)
            {
                _deleteConfirmPopup.Hide();
            }

            // Remember if we were in edit mode to exit after delete
            bool wasInEditMode = _isEditMode;

            // Request controller to delete items
            if (_controller != null)
            {
                _controller.DeleteItems(pathsToDelete);
            }

            // Exit edit mode after delete
            if (wasInEditMode && _isEditMode)
            {
                ToggleEditMode();
            }
        }

        private void OnDeleteCancelled()
        {
            Debug.Log("[RTTMediaLibrary] Delete cancelled");

            if (_deleteConfirmPopup != null)
            {
                _deleteConfirmPopup.Hide();
            }
        }
    }
}
