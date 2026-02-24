using UnityEngine;
using TMPro;
using System.Collections;
using VRWorkspace.UI.HoverEffects;

namespace VRWorkspace.UI.RTT.Components
{
    public partial class RTTMobileKeyboard
    {
        #region Key Actions
        private void OnKeyPress(string key)
        {
            if (_targetInputField == null) return;

            string insertKey = key;
            bool wasShiftActive = _isShiftActive;
            if (_isShiftActive || _isCapsLock)
            {
                insertKey = key.ToUpper();
                if (_isShiftActive && !_isCapsLock)
                {
                    _isShiftActive = false;
                }
            }

            // Insert at current caret position (not necessarily at end)
            int caretPos = _caretPosition;
            _targetInputField.text = _targetInputField.text.Insert(caretPos, insertKey);
            _caretPosition = caretPos + insertKey.Length;
            _targetInputField.caretPosition = _caretPosition;

            // Update visuals if shift was just deactivated
            if (wasShiftActive && !_isShiftActive)
            {
                UpdateShiftVisuals();
            }

            OnKeyPressed?.Invoke(insertKey);
            UpdatePreview();
            MarkDirty();
        }

        private void OnBackspace()
        {
            if (_targetInputField == null) return;

            // Delete character before current caret position
            int caretPos = _caretPosition;
            if (caretPos > 0)
            {
                _targetInputField.text = _targetInputField.text.Remove(caretPos - 1, 1);
                _caretPosition = caretPos - 1;
                _targetInputField.caretPosition = _caretPosition;
            }

            OnBackspacePressed?.Invoke();
            UpdatePreview();
            MarkDirty();
        }

        private void OnEnter()
        {
            OnEnterPressed?.Invoke();
            Hide();
        }

        private void OnShiftPress()
        {
            // 3 states cycle:
            // State 1: default (_isShiftActive=false, _isCapsLock=false) -> click -> State 2
            // State 2: shift active (_isShiftActive=true, _isCapsLock=false) -> click -> State 3
            // State 3: caps lock (_isShiftActive=false, _isCapsLock=true) -> click -> State 1

            if (!_isShiftActive && !_isCapsLock)
            {
                // State 1 -> State 2: Enable shift
                _isShiftActive = true;
                _isCapsLock = false;
            }
            else if (_isShiftActive && !_isCapsLock)
            {
                // State 2 -> State 3: Enable caps lock
                _isShiftActive = false;
                _isCapsLock = true;
            }
            else if (_isCapsLock)
            {
                // State 3 -> State 1: Disable all
                _isShiftActive = false;
                _isCapsLock = false;
            }

            UpdateShiftVisuals();
            MarkDirty();
        }

        private void UpdateShiftVisuals()
        {
            bool isUpper = _isShiftActive || _isCapsLock;

            // Update all letter key labels
            foreach (var kvp in _letterLabels)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.text = isUpper ? kvp.Key.ToUpper() : kvp.Key.ToLower();
                }
            }

            // Update Shift key label based on state:
            // State 1 (default): "Shift"
            // State 2 (shift active, auto-release): "Shift" (same visual but active)
            // State 3 (caps lock): "SHIFT"
            if (_shiftKey != null)
            {
                var shiftLabel = _shiftKey.GetComponentInChildren<TextMeshProUGUI>();
                if (shiftLabel != null)
                {
                    shiftLabel.text = _isCapsLock ? "SHIFT" : "Shift";
                }
            }
        }

        private void OnLayoutToggle()
        {
            _currentLayout = _currentLayout == KeyboardLayout.Letters
                ? KeyboardLayout.Symbols
                : KeyboardLayout.Letters;
            RebuildKeyboard();
        }
        #endregion

        #region Hover Effects
        private void AddHoverEffect(GameObject keyObj, Color baseColor)
        {
            // Use HoverEffectController with GlowBorderHoverEffect
            var hoverController = keyObj.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = keyObj.transform;

            // Add glow border effect with enhanced intensity for keyboard keys
            hoverController.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithGlowIntensity(5f)
                .WithBorderMultiplier(1.5f)
                .WithGlowColor(themeColor));

            // Subscribe to hover state changes for RTT re-render
            hoverController.OnHoverStateChanged += (isHovered) => MarkDirty();
        }

        private void AddSpaceHoverEffect(GameObject keyObj, Color baseColor, float edgePadding, float aspect)
        {
            // Use HoverEffectController with GlowBorderHoverEffect for space key
            var hoverController = keyObj.AddComponent<HoverEffectController>();
            hoverController.TargetVisuals = keyObj.transform;

            // Add glow border effect with enhanced intensity for space key
            hoverController.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithGlowIntensity(5f)
                .WithBorderMultiplier(1.5f)
                .WithGlowColor(themeColor));

            // Subscribe to hover state changes for RTT re-render
            hoverController.OnHoverStateChanged += (isHovered) => MarkDirty();
        }
        #endregion

        #region Key Press Animation
        /// <summary>
        /// Play a key press animation - scales the key down and back up to simulate pressing.
        /// </summary>
        private void PlayKeyPressAnimation(Transform keyTransform)
        {
            if (keyTransform == null) return;
            StartCoroutine(KeyPressAnimationCoroutine(keyTransform));
        }

        private IEnumerator KeyPressAnimationCoroutine(Transform keyTransform)
        {
            Vector3 originalScale = keyTransform.localScale;
            Vector3 pressedScale = originalScale * KEY_PRESS_SCALE;
            float halfDuration = KEY_PRESS_DURATION * 0.5f;

            // Scale down (press)
            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / halfDuration;
                // Use smooth easing for natural feel
                t = t * t * (3f - 2f * t); // Smoothstep
                keyTransform.localScale = Vector3.Lerp(originalScale, pressedScale, t);
                MarkDirty();
                yield return null;
            }
            keyTransform.localScale = pressedScale;

            // Scale back up (release)
            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / halfDuration;
                t = t * t * (3f - 2f * t); // Smoothstep
                keyTransform.localScale = Vector3.Lerp(pressedScale, originalScale, t);
                MarkDirty();
                yield return null;
            }
            keyTransform.localScale = originalScale;
            MarkDirty();
        }
        #endregion
    }
}
