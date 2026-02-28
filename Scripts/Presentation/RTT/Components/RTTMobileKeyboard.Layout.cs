using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTMobileKeyboard
    {
        #region Key Rows (Layout Orchestration)
        private void CreateKeyRows()
        {
            _allKeys.Clear();
            _keyMap.Clear();
            _letterLabels.Clear();
            _shiftKey = null;

            // Update VerticalLayoutGroup spacing based on layout type
            VerticalLayoutGroup vlg = _contentContainer?.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                // 5-row layout uses normal spacing, 4-row layouts use increased spacing
                vlg.spacing = (_currentLayout == KeyboardLayout.Letters) ? _keySpacing : _increasedSpacing;
            }

            if (_currentLayout == KeyboardLayout.Letters)
            {
                CreateLettersLayout();
            }
            else if (_currentLayout == KeyboardLayout.Symbols)
            {
                CreateSymbolsLayout();
            }
            else
            {
                CreateMoreSymbolsLayout();
            }
        }

        private void CreateLettersLayout()
        {
            // Row 0 - Numbers
            CreateKeyRow(LETTERS_ROW_0, 0);

            // Row 1 - QWERTY (letter row)
            CreateKeyRow(LETTERS_ROW_1, 1, 0, true);

            // Row 2 - ASDF (with offset, letter row)
            CreateKeyRow(LETTERS_ROW_2, 2, _keyWidth * 0.5f, true);

            // Row 3 - ZXCV (with shift and backspace)
            CreateBottomLetterRow();

            // Row 4 - Space row
            CreateSpaceRow();
        }

        private void CreateSymbolsLayout()
        {
            // 4-row symbol layout like VRMobileKeyboard
            // Top 3 rows use _topRowKeyHeight, bottom row uses normal _keyHeight

            // Row 0 - Numbers (taller)
            CreateKeyRowWithHeight(SYMBOLS_ROW_0, 0, _topRowKeyHeight);

            // Row 1 - Symbols @#$... (taller)
            CreateKeyRowWithHeight(SYMBOLS_ROW_1, 1, _topRowKeyHeight);

            // Row 2 - Symbols *"'... with =\< and Back (taller)
            CreateSymbolRow2();

            // Row 3 - Bottom row: ABC , spacebar . Enter (normal height)
            CreateSymbolBottomRow();
        }

        private void CreateMoreSymbolsLayout()
        {
            // 4-row more symbols layout like VRMobileKeyboard
            // Top 3 rows use _topRowKeyHeight, bottom row uses normal _keyHeight

            // Row 0 - More symbols ~`|... (taller)
            CreateKeyRowWithHeight(MORE_SYMBOLS_ROW_0, 0, _topRowKeyHeight);

            // Row 1 - More symbols £€$... (taller)
            CreateKeyRowWithHeight(MORE_SYMBOLS_ROW_1, 1, _topRowKeyHeight);

            // Row 2 - More symbols with ?123 and Back (taller)
            CreateMoreSymbolRow2();

            // Row 3 - Bottom row: ABC < spacebar > Enter (normal height)
            CreateMoreSymbolBottomRow();
        }

        private void RebuildKeyboard()
        {
            // Clear existing keys
            foreach (var key in _allKeys)
            {
                if (key != null) Destroy(key);
            }
            _allKeys.Clear();
            _keyMap.Clear();

            // Destroy all row containers (Row_0, Row_1, etc.)
            if (_contentContainer != null)
            {
                // Collect children to destroy (can't destroy during iteration)
                var rowsToDestroy = new List<GameObject>();
                foreach (Transform child in _contentContainer)
                {
                    if (child.name.StartsWith("Row_"))
                    {
                        rowsToDestroy.Add(child.gameObject);
                    }
                }
                foreach (var row in rowsToDestroy)
                {
                    Destroy(row);
                }
            }

            // Rebuild based on layout
            CreateKeyRows();
            MarkDirty();
        }
        #endregion

        #region Row Creation
        private void CreateKeyRow(string[] keys, int rowIndex, float offset = 0, bool isLetterRow = false)
        {
            GameObject row = new GameObject($"Row_{rowIndex}");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, _keyHeight);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = _keyHeight;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _keySpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            if (offset > 0)
            {
                layout.padding = new RectOffset(Mathf.RoundToInt(offset), 0, 0, 0);
            }

            foreach (string key in keys)
            {
                CreateKey(row.transform, key, _keyWidth, _keyHeight, false, null, isLetterRow);
            }
        }

        private void CreateKeyRowWithHeight(string[] keys, int rowIndex, float height)
        {
            GameObject row = new GameObject($"Row_{rowIndex}");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, height);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _keySpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            foreach (string key in keys)
            {
                CreateKey(row.transform, key, _keyWidth, height, false);
            }
        }

        private void CreateBottomLetterRow()
        {
            GameObject row = new GameObject("Row_3");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, _keyHeight);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = _keyHeight;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _keySpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // Shift key - use text label like VRMobileKeyboard
            string shiftLabel = _isCapsLock ? "SHIFT" : (_isShiftActive ? "SHIFT" : "Shift");
            CreateKey(row.transform, shiftLabel, _keyWidth * 1.5f, _keyHeight, true, OnShiftPress);
            _shiftKey = _allKeys[_allKeys.Count - 1]; // Store reference to Shift key

            // Letter keys
            foreach (string key in LETTERS_ROW_3)
            {
                CreateKey(row.transform, key, _keyWidth, _keyHeight, false, null, true); // true = isLetter
            }

            // Backspace - use text label like VRMobileKeyboard
            CreateKey(row.transform, "Back", _keyWidth * 1.5f, _keyHeight, true, OnBackspace);
        }

        private void CreateSpaceRow()
        {
            float contentWidth = _logicalWidth - marginLeft - marginRight;
            float specialKeyWidth = _keyWidth * 1.5f;

            GameObject row = new GameObject("Row_4");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, _keyHeight);

            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = _keyHeight;

            // NO HLG on row - we position 3 groups manually

            // Left group: ?123, /
            float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
            GameObject leftGroup = new GameObject("LeftGroup");
            leftGroup.transform.SetParent(row.transform, false);

            RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
            leftRT.anchorMin = new Vector2(0, 0.5f);
            leftRT.anchorMax = new Vector2(0, 0.5f);
            leftRT.pivot = new Vector2(0, 0.5f);
            leftRT.anchoredPosition = Vector2.zero;
            leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

            HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
            leftHLG.spacing = _keySpacing;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.childControlWidth = false;
            leftHLG.childControlHeight = false;

            CreateKey(leftGroup.transform, "?123", specialKeyWidth, _keyHeight, true, OnLayoutToggle);
            CreateKey(leftGroup.transform, "/", _keyWidth, _keyHeight, true);

            // Right group: ., Enter
            float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
            GameObject rightGroup = new GameObject("RightGroup");
            rightGroup.transform.SetParent(row.transform, false);

            RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
            rightRT.anchorMin = new Vector2(1, 0.5f);
            rightRT.anchorMax = new Vector2(1, 0.5f);
            rightRT.pivot = new Vector2(1, 0.5f);
            rightRT.anchoredPosition = Vector2.zero;
            rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

            HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
            rightHLG.spacing = _keySpacing;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.childControlWidth = false;
            rightHLG.childControlHeight = false;

            CreateKey(rightGroup.transform, ".", _keyWidth, _keyHeight, true);
            CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

            // Space bar - fills space between left and right groups
            float spaceLeft = leftGroupWidth + _keySpacing + _keyWidth * _edgePadding;
            float spaceRight = contentWidth - rightGroupWidth - _keySpacing - _keyWidth * _edgePadding;
            float spaceWidth = spaceRight - spaceLeft;
            float spaceCenterX = (spaceLeft + spaceRight) / 2f;

            GameObject spaceKey = new GameObject("Key_Space");
            spaceKey.transform.SetParent(row.transform, false);

            RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
            spaceRT.anchorMin = new Vector2(0, 0.5f);
            spaceRT.anchorMax = new Vector2(0, 0.5f);
            spaceRT.pivot = new Vector2(0.5f, 0.5f);
            spaceRT.anchoredPosition = new Vector2(spaceCenterX, 0);
            spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

            // Create space key visuals with aspect-adjusted edge padding for wide key
            CreateSpaceKeyVisuals(spaceKey, spaceWidth, _keyHeight, () => OnKeyPress(" "));
        }

        private void CreateSymbolRow2()
        {
            // Row 2 uses taller height for 4-row layout
            float rowHeight = _topRowKeyHeight;

            GameObject row = new GameObject("Row_2");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, rowHeight);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _keySpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // =\< key (switch to more symbols)
            CreateKey(row.transform, "=\\<", _keyWidth * 1.5f, rowHeight, true, () => {
                _currentLayout = KeyboardLayout.MoreSymbols;
                RebuildKeyboard();
            });

            // Symbol keys
            foreach (string key in SYMBOLS_ROW_2)
            {
                CreateKey(row.transform, key, _keyWidth, rowHeight, false);
            }

            // Backspace
            CreateKey(row.transform, "Back", _keyWidth * 1.5f, rowHeight, true, OnBackspace);
        }

        private void CreateSymbolBottomRow()
        {
            float contentWidth = _logicalWidth - marginLeft - marginRight;
            float specialKeyWidth = _keyWidth * 1.5f;

            GameObject row = new GameObject("Row_3");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, _keyHeight);

            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = _keyHeight;

            // Left group: ABC, comma
            float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
            GameObject leftGroup = new GameObject("LeftGroup");
            leftGroup.transform.SetParent(row.transform, false);

            RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
            leftRT.anchorMin = new Vector2(0, 0.5f);
            leftRT.anchorMax = new Vector2(0, 0.5f);
            leftRT.pivot = new Vector2(0, 0.5f);
            leftRT.anchoredPosition = Vector2.zero;
            leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

            HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
            leftHLG.spacing = _keySpacing;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.childControlWidth = false;
            leftHLG.childControlHeight = false;

            CreateKey(leftGroup.transform, "ABC", specialKeyWidth, _keyHeight, true, () => {
                _currentLayout = KeyboardLayout.Letters;
                RebuildKeyboard();
            });
            CreateKey(leftGroup.transform, ",", _keyWidth, _keyHeight, true);

            // Right group: dot, Enter
            float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
            GameObject rightGroup = new GameObject("RightGroup");
            rightGroup.transform.SetParent(row.transform, false);

            RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
            rightRT.anchorMin = new Vector2(1, 0.5f);
            rightRT.anchorMax = new Vector2(1, 0.5f);
            rightRT.pivot = new Vector2(1, 0.5f);
            rightRT.anchoredPosition = Vector2.zero;
            rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

            HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
            rightHLG.spacing = _keySpacing;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.childControlWidth = false;
            rightHLG.childControlHeight = false;

            CreateKey(rightGroup.transform, ".", _keyWidth, _keyHeight, true);
            CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

            // Space bar - fills space between left and right groups
            float spaceLeft = leftGroupWidth + _keySpacing + _keyWidth * _edgePadding;
            float spaceRight = contentWidth - rightGroupWidth - _keySpacing - _keyWidth * _edgePadding;
            float spaceWidth = spaceRight - spaceLeft;
            float spaceCenterX = (spaceLeft + spaceRight) / 2f;

            GameObject spaceKey = new GameObject("Key_Space");
            spaceKey.transform.SetParent(row.transform, false);

            RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
            spaceRT.anchorMin = new Vector2(0, 0.5f);
            spaceRT.anchorMax = new Vector2(0, 0.5f);
            spaceRT.pivot = new Vector2(0.5f, 0.5f);
            spaceRT.anchoredPosition = new Vector2(spaceCenterX, 0);
            spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

            // Create space key visuals with aspect-adjusted edge padding for wide key
            CreateSpaceKeyVisuals(spaceKey, spaceWidth, _keyHeight, () => OnKeyPress(" "));
        }

        private void CreateMoreSymbolRow2()
        {
            // Row 2 uses taller height for 4-row layout
            float rowHeight = _topRowKeyHeight;

            GameObject row = new GameObject("Row_2");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, rowHeight);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _keySpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // ?123 key (back to symbols)
            CreateKey(row.transform, "?123", _keyWidth * 1.5f, rowHeight, true, () => {
                _currentLayout = KeyboardLayout.Symbols;
                RebuildKeyboard();
            });

            // Symbol keys
            foreach (string key in MORE_SYMBOLS_ROW_2)
            {
                CreateKey(row.transform, key, _keyWidth, rowHeight, false);
            }

            // Backspace
            CreateKey(row.transform, "Back", _keyWidth * 1.5f, rowHeight, true, OnBackspace);
        }

        private void CreateMoreSymbolBottomRow()
        {
            float contentWidth = _logicalWidth - marginLeft - marginRight;
            float specialKeyWidth = _keyWidth * 1.5f;

            GameObject row = new GameObject("Row_3");
            row.transform.SetParent(_contentContainer, false);

            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0, _keyHeight);

            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = _keyHeight;

            // Left group: ABC, <
            float leftGroupWidth = specialKeyWidth + _keySpacing + _keyWidth;
            GameObject leftGroup = new GameObject("LeftGroup");
            leftGroup.transform.SetParent(row.transform, false);

            RectTransform leftRT = leftGroup.AddComponent<RectTransform>();
            leftRT.anchorMin = new Vector2(0, 0.5f);
            leftRT.anchorMax = new Vector2(0, 0.5f);
            leftRT.pivot = new Vector2(0, 0.5f);
            leftRT.anchoredPosition = Vector2.zero;
            leftRT.sizeDelta = new Vector2(leftGroupWidth, _keyHeight);

            HorizontalLayoutGroup leftHLG = leftGroup.AddComponent<HorizontalLayoutGroup>();
            leftHLG.spacing = _keySpacing;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.childControlWidth = false;
            leftHLG.childControlHeight = false;

            CreateKey(leftGroup.transform, "ABC", specialKeyWidth, _keyHeight, true, () => {
                _currentLayout = KeyboardLayout.Letters;
                RebuildKeyboard();
            });
            CreateKey(leftGroup.transform, "<", _keyWidth, _keyHeight, true);

            // Right group: >, Enter
            float rightGroupWidth = _keyWidth + _keySpacing + specialKeyWidth;
            GameObject rightGroup = new GameObject("RightGroup");
            rightGroup.transform.SetParent(row.transform, false);

            RectTransform rightRT = rightGroup.AddComponent<RectTransform>();
            rightRT.anchorMin = new Vector2(1, 0.5f);
            rightRT.anchorMax = new Vector2(1, 0.5f);
            rightRT.pivot = new Vector2(1, 0.5f);
            rightRT.anchoredPosition = Vector2.zero;
            rightRT.sizeDelta = new Vector2(rightGroupWidth, _keyHeight);

            HorizontalLayoutGroup rightHLG = rightGroup.AddComponent<HorizontalLayoutGroup>();
            rightHLG.spacing = _keySpacing;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.childControlWidth = false;
            rightHLG.childControlHeight = false;

            CreateKey(rightGroup.transform, ">", _keyWidth, _keyHeight, true);
            CreateKey(rightGroup.transform, "Enter", specialKeyWidth, _keyHeight, true, OnEnter);

            // Space bar - fills space between left and right groups
            float spaceLeft = leftGroupWidth + _keySpacing + _keyWidth * _edgePadding;
            float spaceRight = contentWidth - rightGroupWidth - _keySpacing - _keyWidth * _edgePadding;
            float spaceWidth = spaceRight - spaceLeft;
            float spaceCenterX = (spaceLeft + spaceRight) / 2f;

            GameObject spaceKey = new GameObject("Key_Space");
            spaceKey.transform.SetParent(row.transform, false);

            RectTransform spaceRT = spaceKey.AddComponent<RectTransform>();
            spaceRT.anchorMin = new Vector2(0, 0.5f);
            spaceRT.anchorMax = new Vector2(0, 0.5f);
            spaceRT.pivot = new Vector2(0.5f, 0.5f);
            spaceRT.anchoredPosition = new Vector2(spaceCenterX, 0);
            spaceRT.sizeDelta = new Vector2(spaceWidth, _keyHeight);

            // Create space key visuals with aspect-adjusted edge padding for wide key
            CreateSpaceKeyVisuals(spaceKey, spaceWidth, _keyHeight, () => OnKeyPress(" "));
        }
        #endregion

        #region Key Visuals
        /// <summary>
        /// Creates Space key visuals using dedicated wide element shaders.
        /// These shaders handle aspect ratio correctly for wide buttons.
        /// </summary>
        private void CreateSpaceKeyVisuals(GameObject keyObj, float width, float height, Action onClick)
        {
            Color baseColor = keyColor;  // Space uses normal key color
            float aspect = width / height;

            // Background with rounded corners and glass effect - using WIDE shader
            Image bg = keyObj.AddComponent<Image>();
            bg.sprite = GetPixelSprite();
            bg.raycastTarget = true;

            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material glassMat = new Material(glassShader);
                glassMat.SetFloat("_CornerRadius", _cornerRadius);
                glassMat.SetFloat("_EdgePadding", _edgePadding);  // Use normal edgePadding - shader handles aspect correctly
                glassMat.SetFloat("_Aspect", aspect);

                Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 1f);
                Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 1f);
                glassMat.SetColor("_ColorA", colorA);
                glassMat.SetColor("_ColorB", colorB);
                glassMat.SetFloat("_GradientOffset", 0f);
                glassMat.SetFloat("_GradientAngle", -15f);
                glassMat.SetFloat("_GlassAlpha", 1f);

                bg.material = glassMat;
                bg.color = Color.white;
            }
            else
            {
                bg.color = baseColor;
            }

            // Border with glow effect - using WIDE shader
            GameObject borderObj = new GameObject("Border");
            borderObj.transform.SetParent(keyObj.transform, false);
            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = Vector2.zero;
            borderRT.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = GetPixelSprite();
            borderImg.raycastTarget = false;

            Shader borderShader = Shader.Find("Custom/GlowingWideElementBorder");
            if (borderShader != null)
            {
                Material borderMat = new Material(borderShader);
                borderMat.SetFloat("_Aspect", aspect);
                borderMat.SetFloat("_EdgePadding", _edgePadding);  // Use normal edgePadding - shader handles aspect correctly
                borderMat.SetFloat("_CornerRadius", _cornerRadius);

                Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
                borderMat.SetColor("_GlowColor", borderGlowCol);

                borderMat.SetFloat("_BorderWidth", 0.04f);
                borderMat.SetFloat("_GlowWidth", 0.03f);
                borderMat.SetFloat("_GlowIntensity", 1.5f);
                borderMat.SetFloat("_PulseEnabled", 0f);

                borderImg.material = borderMat;
            }

            // Button
            Button btn = keyObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            Transform capturedTransform = keyObj.transform;
            btn.onClick.AddListener(() =>
            {
                PlayKeyPressAnimation(capturedTransform);
                onClick?.Invoke();
                MarkDirty();
            });

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(keyObj.transform, false);

            TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = "Space";
            tmp.fontSize = keyFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (customFont != null) tmp.font = customFont;

            RectTransform labelRT = labelObj.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            _allKeys.Add(keyObj);

            // Add hover effect with wide shader support
            AddSpaceHoverEffect(keyObj, baseColor, _edgePadding, aspect);
        }

        /// <summary>
        /// Creates key visuals without LayoutElement (for manual positioned keys).
        /// </summary>
        private void CreateKeyVisuals(GameObject keyObj, string label, float width, float height, bool isSpecial, Action onClick)
        {
            Color baseColor = isSpecial ? specialKeyColor : keyColor;
            float aspect = width / height;

            // Background with rounded corners and glass effect
            Image bg = keyObj.AddComponent<Image>();
            bg.sprite = GetPixelSprite();
            bg.raycastTarget = true;

            // Use Wide shader for better Android GPU compatibility
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material glassMat = new Material(glassShader);
                glassMat.SetFloat("_CornerRadius", _cornerRadius);
                glassMat.SetFloat("_EdgePadding", _edgePadding);
                glassMat.SetFloat("_Aspect", aspect);

                Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 1f);
                Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 1f);
                glassMat.SetColor("_ColorA", colorA);
                glassMat.SetColor("_ColorB", colorB);
                glassMat.SetFloat("_GradientOffset", 0f);
                glassMat.SetFloat("_GradientAngle", -15f);
                glassMat.SetFloat("_GlassAlpha", 1f);

                bg.material = glassMat;
                bg.color = Color.white;
            }
            else
            {
                bg.color = baseColor;
            }

            // Border with glow effect
            GameObject borderObj = new GameObject("Border");
            borderObj.transform.SetParent(keyObj.transform, false);
            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = Vector2.zero;
            borderRT.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = GetPixelSprite();
            borderImg.raycastTarget = false;

            Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
            if (borderShader != null)
            {
                Material borderMat = new Material(borderShader);
                borderMat.SetFloat("_Aspect", aspect);
                borderMat.SetFloat("_EdgePadding", _edgePadding);
                borderMat.SetFloat("_CornerRadius", _cornerRadius);

                Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
                borderMat.SetColor("_GlowColor", borderGlowCol);

                borderMat.SetFloat("_BorderWidth", 0.02f);
                borderMat.SetFloat("_GlowWidth", 0.03f);
                borderMat.SetFloat("_GlowIntensity", 1.5f);
                borderMat.SetFloat("_PulseEnabled", 0f);

                borderImg.material = borderMat;
            }

            // Button
            Button btn = keyObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            Transform capturedTransform = keyObj.transform;
            btn.onClick.AddListener(() =>
            {
                PlayKeyPressAnimation(capturedTransform);
                onClick?.Invoke();
                MarkDirty();
            });

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(keyObj.transform, false);

            TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label == " " ? "Space" : label;
            tmp.fontSize = keyFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (customFont != null) tmp.font = customFont;

            RectTransform labelRT = labelObj.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            _allKeys.Add(keyObj);

            // Add hover effect
            AddHoverEffect(keyObj, baseColor);
        }

        /// <summary>
        /// Creates a key with manual positioning (no HLG).
        /// </summary>
        private void CreateKeyManual(Transform parent, string label, float xPos, float width, float height, bool isSpecial, Action onClick = null)
        {
            GameObject keyObj = new GameObject($"Key_{label}");
            keyObj.transform.SetParent(parent, false);

            RectTransform rt = keyObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(xPos, 0);
            rt.sizeDelta = new Vector2(width, height);

            // No LayoutElement needed for manual positioning

            Color baseColor = isSpecial ? specialKeyColor : keyColor;
            float aspect = width / height;

            // Background with rounded corners and glass effect
            Image bg = keyObj.AddComponent<Image>();
            bg.sprite = GetPixelSprite();
            bg.raycastTarget = true;

            // Use Wide shader for better Android GPU compatibility
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material glassMat = new Material(glassShader);
                glassMat.SetFloat("_CornerRadius", _cornerRadius);
                glassMat.SetFloat("_EdgePadding", _edgePadding);
                glassMat.SetFloat("_Aspect", aspect);

                Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 1f);
                Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 1f);
                glassMat.SetColor("_ColorA", colorA);
                glassMat.SetColor("_ColorB", colorB);
                glassMat.SetFloat("_GradientOffset", 0f);
                glassMat.SetFloat("_GradientAngle", -15f);
                glassMat.SetFloat("_GlassAlpha", 1f);

                bg.material = glassMat;
                bg.color = Color.white;
            }
            else
            {
                bg.color = baseColor;
            }

            // Border with glow effect
            GameObject borderObj = new GameObject("Border");
            borderObj.transform.SetParent(keyObj.transform, false);
            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = Vector2.zero;
            borderRT.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = GetPixelSprite();
            borderImg.raycastTarget = false;

            Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
            if (borderShader != null)
            {
                Material borderMat = new Material(borderShader);
                borderMat.SetFloat("_Aspect", aspect);
                borderMat.SetFloat("_EdgePadding", _edgePadding);
                borderMat.SetFloat("_CornerRadius", _cornerRadius);

                Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
                borderMat.SetColor("_GlowColor", borderGlowCol);

                // Keep same border width for all keys for visual consistency
                borderMat.SetFloat("_BorderWidth", 0.02f);
                borderMat.SetFloat("_GlowWidth", 0.03f);
                borderMat.SetFloat("_GlowIntensity", 1.5f);
                borderMat.SetFloat("_PulseEnabled", 0f);

                borderImg.material = borderMat;
            }

            // Button
            Button btn = keyObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            string capturedLabel = label;
            Transform capturedTransform = keyObj.transform;
            btn.onClick.AddListener(() =>
            {
                PlayKeyPressAnimation(capturedTransform);
                if (onClick != null)
                    onClick.Invoke();
                else
                    OnKeyPress(capturedLabel);
                MarkDirty();
            });

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(keyObj.transform, false);

            TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label == " " ? "Space" : label;
            tmp.fontSize = keyFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (customFont != null) tmp.font = customFont;

            RectTransform labelRT = labelObj.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            _allKeys.Add(keyObj);
            if (!string.IsNullOrEmpty(label) && label != " ")
            {
                _keyMap[label.ToLower()] = keyObj;
            }

            // Add hover effect
            AddHoverEffect(keyObj, baseColor);
        }

        private void CreateKey(Transform parent, string label, float width, float height, bool isSpecial, Action onClick = null, bool isLetter = false)
        {
            GameObject keyObj = new GameObject($"Key_{label}");
            keyObj.transform.SetParent(parent, false);

            RectTransform rt = keyObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, height);

            LayoutElement le = keyObj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            Color baseColor = isSpecial ? specialKeyColor : keyColor;
            float aspect = width / height;

            // Background with rounded corners and glass effect
            Image bg = keyObj.AddComponent<Image>();
            bg.sprite = GetPixelSprite();
            bg.raycastTarget = true;

            // Use Wide shader for better Android GPU compatibility
            Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
            if (glassShader != null)
            {
                Material glassMat = new Material(glassShader);
                glassMat.SetFloat("_CornerRadius", _cornerRadius);
                glassMat.SetFloat("_EdgePadding", _edgePadding);
                glassMat.SetFloat("_Aspect", aspect);

                // Gradient colors based on key color
                Color colorA = new Color(baseColor.r * 1.2f, baseColor.g * 1.2f, baseColor.b * 1.2f, 1f);
                Color colorB = new Color(baseColor.r * 0.8f, baseColor.g * 0.8f, baseColor.b * 0.8f, 1f);
                glassMat.SetColor("_ColorA", colorA);
                glassMat.SetColor("_ColorB", colorB);
                glassMat.SetFloat("_GradientOffset", 0f);
                glassMat.SetFloat("_GradientAngle", -15f);
                glassMat.SetFloat("_GlassAlpha", 1f);

                bg.material = glassMat;
                bg.color = Color.white;
            }
            else
            {
                bg.color = baseColor;
            }

            // Border with glow effect
            GameObject borderObj = new GameObject("Border");
            borderObj.transform.SetParent(keyObj.transform, false);
            RectTransform borderRT = borderObj.AddComponent<RectTransform>();
            borderRT.anchorMin = Vector2.zero;
            borderRT.anchorMax = Vector2.one;
            borderRT.offsetMin = Vector2.zero;
            borderRT.offsetMax = Vector2.zero;

            Image borderImg = borderObj.AddComponent<Image>();
            borderImg.sprite = GetPixelSprite();
            borderImg.raycastTarget = false;

            Shader borderShader = Shader.Find("Custom/GlowingElementBorder");
            if (borderShader != null)
            {
                Material borderMat = new Material(borderShader);
                borderMat.SetFloat("_Aspect", aspect);
                borderMat.SetFloat("_EdgePadding", _edgePadding);
                borderMat.SetFloat("_CornerRadius", _cornerRadius);

                Color borderGlowCol = Color.Lerp(baseColor, Color.white, 0.5f);
                borderMat.SetColor("_GlowColor", borderGlowCol);

                borderMat.SetFloat("_BorderWidth", 0.02f);
                borderMat.SetFloat("_GlowWidth", 0.03f);
                borderMat.SetFloat("_GlowIntensity", 1.5f);
                borderMat.SetFloat("_PulseEnabled", 0f);

                borderImg.material = borderMat;
            }

            // Button
            Button btn = keyObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1.2f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            string capturedLabel = label;
            Transform capturedTransform = keyObj.transform;
            btn.onClick.AddListener(() =>
            {
                PlayKeyPressAnimation(capturedTransform);
                if (onClick != null)
                    onClick.Invoke();
                else
                    OnKeyPress(capturedLabel);
                MarkDirty();
            });

            // Label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(keyObj.transform, false);

            TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
            // Show uppercase if shift is active and this is a letter key
            string displayLabel = label;
            if (isLetter && (_isShiftActive || _isCapsLock))
            {
                displayLabel = label.ToUpper();
            }
            tmp.text = displayLabel == " " ? "space" : displayLabel;
            tmp.fontSize = keyFontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (customFont != null) tmp.font = customFont;

            RectTransform labelRT = labelObj.GetComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            _allKeys.Add(keyObj);
            if (!string.IsNullOrEmpty(label) && label != " ")
            {
                _keyMap[label.ToLower()] = keyObj;
            }

            // Track letter keys for shift updates
            if (isLetter && label.Length == 1)
            {
                _letterLabels[label.ToLower()] = tmp;
            }

            // Add hover effect
            AddHoverEffect(keyObj, baseColor);
        }
        #endregion
    }
}
