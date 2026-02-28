using UnityEngine;

namespace VRWorkspace.UI.Utilities
{
    /// <summary>
    /// Utility class for creating and caching commonly used sprites.
    /// Centralizes sprite generation to avoid code duplication across factories.
    /// </summary>
    public static class SpriteUtility
    {
        #region Cached Sprites

        private static Sprite _pixelSprite;
        private static Sprite _arrowSprite;
        private static Sprite _checkmarkSprite;

        #endregion

        #region Pixel Sprite

        /// <summary>
        /// Gets or creates a simple 2x2 white pixel sprite.
        /// Used as a base sprite for shader-based UI elements.
        /// </summary>
        public static Sprite GetPixelSprite()
        {
            if (_pixelSprite != null) return _pixelSprite;

            Texture2D tex = new Texture2D(2, 2);
            tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            tex.filterMode = FilterMode.Point;

            _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            _pixelSprite.name = "PixelSprite";

            return _pixelSprite;
        }

        #endregion

        #region Arrow Sprite

        /// <summary>
        /// Gets or creates a simple downward arrow sprite for dropdowns.
        /// </summary>
        public static Sprite GetArrowSprite()
        {
            if (_arrowSprite != null) return _arrowSprite;

            // Create a simple arrow texture
            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);

            // Fill with transparent
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            // Draw downward arrow
            int centerX = size / 2;
            int arrowWidth = size / 2;
            int arrowHeight = size / 3;
            int startY = size / 2 + arrowHeight / 2;

            for (int y = 0; y < arrowHeight; y++)
            {
                int lineWidth = arrowWidth - (y * arrowWidth / arrowHeight);
                int startX = centerX - lineWidth / 2;
                int endX = centerX + lineWidth / 2;

                for (int x = startX; x <= endX; x++)
                {
                    if (x >= 0 && x < size && (startY - y) >= 0 && (startY - y) < size)
                    {
                        pixels[(startY - y) * size + x] = Color.white;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            _arrowSprite.name = "ArrowSprite";

            return _arrowSprite;
        }

        #endregion

        #region Checkmark Sprite

        /// <summary>
        /// Gets or creates a simple checkmark sprite for selections.
        /// </summary>
        public static Sprite GetCheckmarkSprite()
        {
            if (_checkmarkSprite != null) return _checkmarkSprite;

            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);

            // Fill with transparent
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            // Draw checkmark (simple L-shape)
            // Short line going down-left
            for (int i = 0; i < 8; i++)
            {
                int x = 8 + i;
                int y = 16 - i;
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    pixels[y * size + x] = Color.white;
                    if (y > 0) pixels[(y - 1) * size + x] = Color.white;
                    if (y < size - 1) pixels[(y + 1) * size + x] = Color.white;
                }
            }

            // Long line going up-right
            for (int i = 0; i < 12; i++)
            {
                int x = 16 + i;
                int y = 8 + i;
                if (x >= 0 && x < size && y >= 0 && y < size)
                {
                    pixels[y * size + x] = Color.white;
                    if (y > 0) pixels[(y - 1) * size + x] = Color.white;
                    if (y < size - 1) pixels[(y + 1) * size + x] = Color.white;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            _checkmarkSprite = Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f);
            _checkmarkSprite.name = "CheckmarkSprite";

            return _checkmarkSprite;
        }

        #endregion

        #region Resource Loading

        /// <summary>
        /// Loads a sprite from Resources folder by path.
        /// </summary>
        /// <param name="resourcePath">Path relative to Resources folder (without extension)</param>
        /// <returns>Loaded sprite or null if not found</returns>
        public static Sprite LoadFromResources(string resourcePath)
        {
            return Resources.Load<Sprite>(resourcePath);
        }

        /// <summary>
        /// Loads a sprite from Resources folder, with fallback to a generated sprite.
        /// </summary>
        public static Sprite LoadOrFallback(string resourcePath, Sprite fallback)
        {
            Sprite sprite = LoadFromResources(resourcePath);
            return sprite != null ? sprite : fallback;
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clears all cached sprites.
        /// Call this when unloading the application or switching scenes.
        /// </summary>
        public static void ClearCache()
        {
            if (_pixelSprite != null)
            {
                if (_pixelSprite.texture != null)
                    Object.Destroy(_pixelSprite.texture);
                Object.Destroy(_pixelSprite);
                _pixelSprite = null;
            }

            if (_arrowSprite != null)
            {
                if (_arrowSprite.texture != null)
                    Object.Destroy(_arrowSprite.texture);
                Object.Destroy(_arrowSprite);
                _arrowSprite = null;
            }

            if (_checkmarkSprite != null)
            {
                if (_checkmarkSprite.texture != null)
                    Object.Destroy(_checkmarkSprite.texture);
                Object.Destroy(_checkmarkSprite);
                _checkmarkSprite = null;
            }
        }

        #endregion
    }
}
