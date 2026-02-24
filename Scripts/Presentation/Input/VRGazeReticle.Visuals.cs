using UnityEngine;
using UnityEngine.UI;
using VRWorkspace.UI.Components;

namespace VRWorkspace.VRInput
{
    /// <summary>
    /// Reticle rendering, animation, visual feedback, dot/ring creation, color changes.
    /// </summary>
    public partial class VRGazeReticle
    {
        #region Custom Cursor Fields
        private Color _defaultColor;
        private float _customCursorScaleMultiplier = 1f;
        private const float CUSTOM_CURSOR_SCALE = 3f; // Scale multiplier for custom cursors
        #endregion

        #region Reticle Creation

        void CreateReticle()
        {
            // 1. Tạo Canvas con
            GameObject canvasObj = new GameObject("GazeReticleCanvas");
            canvasObj.transform.SetParent(_cam.transform, false);
            // Important: Keep layer same as Cam to be visible
            canvasObj.layer = _cam.gameObject.layer;

            Canvas c = canvasObj.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.sortingOrder = 30000;

            // Lưu RectTransform để di chuyển depth
            _canvasRT = canvasObj.GetComponent<RectTransform>();
            _canvasRT.sizeDelta = Vector2.zero;
            _canvasRT.localScale = Vector3.one;
            _canvasRT.localPosition = Vector3.zero;
            _canvasRT.localRotation = Quaternion.identity;

            // 2. Tạo Chấm (Standard Reticle)
            GameObject imgObj = new GameObject("Dot");
            imgObj.transform.SetParent(canvasObj.transform, false);
            imgObj.layer = _cam.gameObject.layer;

            _reticleImage = imgObj.AddComponent<Image>();
            _defaultSprite = GetCircleSprite();
            _reticleImage.sprite = _defaultSprite;
            _reticleImage.color = colorInteract;
            _reticleImage.raycastTarget = false;

            _reticleImage.enabled = false;

            // Giữ ZTest Always để không bị xuyên tường
            Material zTestMat = new Material(Shader.Find("UI/Default"));
            zTestMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _reticleImage.material = zTestMat;

            RectTransform imgRT = imgObj.GetComponent<RectTransform>();
            imgRT.sizeDelta = new Vector2(100, 100);
            imgRT.localScale = Vector3.one;
            imgRT.anchoredPosition = Vector3.zero;

            // 3. Tạo Dwell Progress Ring (around the reticle dot)
            CreateDwellRing(canvasObj, zTestMat);

            // 4. Tạo Recenter UI (Hidden by default)
            CreateRecenterUI(canvasObj, zTestMat);
        }

        void CreateDwellRing(GameObject parentCanvas, Material overlayMat)
        {
            GameObject ringObj = new GameObject("DwellRing");
            ringObj.transform.SetParent(parentCanvas.transform, false);
            ringObj.layer = parentCanvas.layer;

            _dwellRing = ringObj.AddComponent<Image>();
            _dwellRing.sprite = GetRingSprite();
            _dwellRing.type = Image.Type.Filled;
            _dwellRing.fillMethod = Image.FillMethod.Radial360;
            _dwellRing.fillOrigin = (int)Image.Origin360.Top;
            _dwellRing.fillClockwise = true;
            _dwellRing.color = colorInteract; // Cùng màu với reticle khi va chạm
            _dwellRing.fillAmount = 0f;
            _dwellRing.material = overlayMat;
            _dwellRing.raycastTarget = false;

            RectTransform ringRT = ringObj.GetComponent<RectTransform>();
            ringRT.sizeDelta = new Vector2(300, 300); // Larger than the dot
            ringRT.localScale = Vector3.one;
            ringRT.anchoredPosition = Vector3.zero;

            _dwellRing.enabled = false;
        }

        void CreateRecenterUI(GameObject parentCanvas, Material overlayMat)
        {
            _recenterGroup = new GameObject("RecenterGroup");
            _recenterGroup.transform.SetParent(parentCanvas.transform, false);
            _recenterGroup.layer = parentCanvas.layer;

            RectTransform grpRT = _recenterGroup.AddComponent<RectTransform>();
            grpRT.anchorMin = Vector2.zero; grpRT.anchorMax = Vector2.zero;
            // Size referencing roughly 20-30cm in world space when scaled properly
            grpRT.sizeDelta = new Vector2(256, 256);
            grpRT.anchoredPosition = Vector3.zero;

            // A. Background (Dark Circle)
            GameObject bgObj = new GameObject("Bg");
            bgObj.transform.SetParent(_recenterGroup.transform, false);
            _recenterBg = bgObj.AddComponent<Image>();
            _recenterBg.sprite = GetCircleSprite();
            _recenterBg.color = new Color(0, 0, 0, 0.4f); // Semi-transparent dark
            _recenterBg.material = overlayMat;
            RectTransform bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.sizeDelta = Vector2.zero;

            // B. Icon (White, Center)
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(_recenterGroup.transform, false);
            _recenterIcon = iconObj.AddComponent<Image>();
            _recenterIcon.preserveAspect = true;
            _recenterIcon.color = Color.white;
            _recenterIcon.material = overlayMat;
            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            // Make icon smaller than background
            iconRT.anchorMin = Vector2.zero; iconRT.anchorMax = Vector2.one;
            iconRT.sizeDelta = new Vector2(-100, -100);
            iconRT.anchoredPosition = Vector2.zero;

            // C. Progress Ring
            GameObject ringObj = new GameObject("Ring");
            ringObj.transform.SetParent(_recenterGroup.transform, false);
            _recenterRing = ringObj.AddComponent<Image>();
            _recenterRing.sprite = GetRingSprite(4.2f); // Mỏng hơn 1/5 so với dwellRing
            _recenterRing.type = Image.Type.Filled;
            _recenterRing.fillMethod = Image.FillMethod.Radial360;
            _recenterRing.fillOrigin = (int)Image.Origin360.Top;
            _recenterRing.fillClockwise = true; // "Vẽ từ từ" - usually clockwise
            _recenterRing.color = new Color(1f, 0f, 0.4f, 1f); // Pink/Reddish color from user reference or just White?
            // User image shows a Pink/Magenta ring. I'll use a bright color, or default White.
            // Let's use magenta to match the "neon" vibe.
            _recenterRing.color = new Color(1f, 0.2f, 0.6f);
            _recenterRing.fillAmount = 0f;
            _recenterRing.material = overlayMat;

            RectTransform ringRT = ringObj.GetComponent<RectTransform>();
            ringRT.anchorMin = Vector2.zero; ringRT.anchorMax = Vector2.one;
            ringRT.sizeDelta = Vector2.zero;

            _recenterGroup.SetActive(false);
        }

        #endregion

        #region Recenter Visual State

        public void EnterRecenterMode(Sprite icon)
        {
            _isRecentering = true;
            // Hide standard reticle
            if (_reticleImage != null) _reticleImage.enabled = false;

            // Show Recenter UI
            if (_recenterGroup != null)
            {
                _recenterGroup.SetActive(true);
                if (icon != null) _recenterIcon.sprite = icon;
                _recenterRing.fillAmount = 0f;
            }
        }

        public void UpdateRecenterProgress(float progress)
        {
            if (_recenterRing != null)
                _recenterRing.fillAmount = progress;
        }

        public void ExitRecenterMode()
        {
            _isRecentering = false;
            if (_recenterGroup != null) _recenterGroup.SetActive(false);

            // Reset stabilization after recenter to sync with new orientation
            ResetStabilization();
        }

        void UpdateRecenterPosition()
        {
            // Position fixed distance from camera
            if (_canvasRT == null || _cam == null) return;

            // We reset the canvas local position relative to camera parent
            // However, CreateReticle sets parent to _cam.
            // So localPosition (0,0, dist) puts it at center of view.
            // We want it to stay there regardless of raycast hits.

            _canvasRT.localPosition = new Vector3(0, 0, _recenterDistance);
            _canvasRT.localRotation = Quaternion.identity;

            // Constant size on screen
            // "Reticle Size" logic:
            // scale = (reticleSize / 100f) * dist; -> at 1m, scale is reticleSize/100.
            // For Recenter UI (256px), we want it to look like maybe 20cm?
            // Let's scale it so it's readable.

            float scale = 0.0004f * _recenterDistance;
            _canvasRT.localScale = Vector3.one * scale;
        }

        #endregion

        #region Ripple Visual Feedback

        void TriggerRippleEffect(GameObject obj, Vector2 normalizedHitPoint)
        {
            if (obj == null) return;

            // Tìm tất cả VRButtonRipple trong hierarchy của button
            VRButtonRipple[] ripples = null;

            // Strategy 1: Tìm từ parent gốc của button (bao gồm tất cả children)
            Transform buttonRoot = obj.transform;

            // Đi lên để tìm root của button (thường là object có Button component)
            UnityEngine.UI.Button btn = obj.GetComponentInParent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                buttonRoot = btn.transform;
            }

            // Lấy tất cả VRButtonRipple trong button
            ripples = buttonRoot.GetComponentsInChildren<VRButtonRipple>(true);

            if (ripples != null && ripples.Length > 0)
            {
                Debug.Log($"[VRGazeReticle] Found {ripples.Length} VRButtonRipple(s) on {buttonRoot.name}");
                // Trigger tất cả ripple effects
                foreach (var ripple in ripples)
                {
                    Debug.Log($"[VRGazeReticle] Triggering flash on {ripple.gameObject.name}");
                    ripple.TriggerRipple(normalizedHitPoint);
                }
            }
            else
            {
                Debug.LogWarning($"[VRGazeReticle] No VRButtonRipple found for {obj.name}, buttonRoot: {buttonRoot.name}");
            }
        }

        #endregion

        #region Sprite Generators

        Sprite GetCircleSprite()
        {
            int res = 64;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color[] c = new Color[res * res];
            float radius = res / 2f;
            Vector2 center = new Vector2(radius, radius);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(radius - d);
                    alpha = Mathf.Pow(alpha, 2f);
                    c[y * res + x] = new Color(1, 1, 1, alpha);
                }
            }
            tex.SetPixels(c);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        }

        Sprite GetRingSprite(float thickness = 21f)
        {
            int res = 128;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color[] c = new Color[res * res];
            float radius = res / 2f;
            Vector2 center = new Vector2(radius, radius);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    if (d > radius) { c[y * res + x] = Color.clear; continue; }

                    float edgeAlpha = Mathf.Clamp01(radius - d);
                    float innerAlpha = Mathf.Clamp01(d - (radius - thickness));

                    float alpha = edgeAlpha * innerAlpha;
                    alpha = Mathf.Pow(alpha, 0.5f);

                    c[y * res + x] = new Color(1, 1, 1, alpha);
                }
            }
            tex.SetPixels(c);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f));
        }

        #endregion

        #region Custom Cursor API

        /// <summary>
        /// Set a custom sprite for the reticle cursor.
        /// Call ResetCursorSprite() to restore the default circle.
        /// </summary>
        public void SetCursorSprite(Sprite sprite)
        {
            if (sprite == null) return;
            _currentCustomSprite = sprite;
            if (_reticleImage != null)
            {
                // Save defaults on first custom cursor
                if (_defaultColor == default)
                    _defaultColor = _reticleImage.color;

                _reticleImage.sprite = sprite;
                _reticleImage.preserveAspect = true;
                _reticleImage.color = Color.white; // White color for custom cursors

                // Set scale multiplier (will be applied in CheckGaze)
                _customCursorScaleMultiplier = CUSTOM_CURSOR_SCALE;
            }
        }

        /// <summary>
        /// Set cursor sprite by loading from Resources folder.
        /// </summary>
        public void SetCursorSprite(string resourceName)
        {
            Sprite sprite = Resources.Load<Sprite>(resourceName);
            if (sprite != null)
            {
                SetCursorSprite(sprite);
            }
            else
            {
                // Try loading as Texture2D and convert to Sprite
                Texture2D tex = Resources.Load<Texture2D>(resourceName);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    SetCursorSprite(sprite);
                }
            }
        }

        /// <summary>
        /// Reset cursor to default circle sprite.
        /// </summary>
        public void ResetCursorSprite()
        {
            _currentCustomSprite = null;
            _customCursorScaleMultiplier = 1f; // Reset scale multiplier

            if (_reticleImage != null && _defaultSprite != null)
            {
                _reticleImage.sprite = _defaultSprite;
                _reticleImage.preserveAspect = false;

                // Restore default color
                if (_defaultColor != default)
                    _reticleImage.color = _defaultColor;
                else
                    _reticleImage.color = colorInteract;
            }
        }

        /// <summary>
        /// Check if currently using a custom cursor sprite.
        /// </summary>
        public bool IsUsingCustomCursor => _currentCustomSprite != null;

        #endregion
    }
}
