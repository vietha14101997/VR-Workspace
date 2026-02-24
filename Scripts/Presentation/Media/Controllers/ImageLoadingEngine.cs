using UnityEngine;
using System;
using System.Collections;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.Core
{
    /// <summary>
    /// Engine for loading and managing image textures in VR.
    /// Supports async loading, memory management, and various image formats.
    /// </summary>
    public class ImageLoadingEngine : MonoBehaviour
    {
        #region Events
        public event Action<Texture2D> OnImageLoaded;
        public event Action<float> OnLoadProgress;
        public event Action<string> OnError;
        #endregion

        #region Properties
        public bool IsLoading { get; private set; }
        public Texture2D CurrentTexture { get; private set; }
        public MediaImageInfo? CurrentImage { get; private set; }
        #endregion

        #region Constants
        private const int THUMBNAIL_MAX_SIZE = 512;
        private const int PREVIEW_MAX_SIZE = 2048;
        private const int MAX_TEXTURE_MEMORY_MB = 256;
        #endregion

        #region Private Fields
        private Coroutine _loadCoroutine;
        private long _currentTextureMemory;
        #endregion

        #region Public API
        /// <summary>
        /// Load image from MediaImageInfo
        /// </summary>
        public void LoadImage(MediaImageInfo imageInfo, bool fullResolution = true)
        {
            if (IsLoading)
            {
                StopLoading();
            }

            CurrentImage = imageInfo;
            _loadCoroutine = StartCoroutine(LoadImageCoroutine(imageInfo.Path, fullResolution));
        }

        /// <summary>
        /// Load image from path
        /// </summary>
        public void LoadImage(string path, bool fullResolution = true)
        {
            if (IsLoading)
            {
                StopLoading();
            }

            CurrentImage = MediaImageInfo.FromPath(path);
            _loadCoroutine = StartCoroutine(LoadImageCoroutine(path, fullResolution));
        }

        /// <summary>
        /// Load thumbnail version of image
        /// </summary>
        public void LoadThumbnail(string path, Action<Texture2D> callback)
        {
            StartCoroutine(LoadThumbnailCoroutine(path, callback));
        }

        /// <summary>
        /// Stop current loading operation
        /// </summary>
        public void StopLoading()
        {
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }
            IsLoading = false;
        }

        /// <summary>
        /// Unload current texture to free memory
        /// </summary>
        public void UnloadCurrent()
        {
            if (CurrentTexture != null)
            {
                _currentTextureMemory -= GetTextureMemory(CurrentTexture);
                Destroy(CurrentTexture);
                CurrentTexture = null;
            }
            CurrentImage = null;
        }

        /// <summary>
        /// Dispose all resources
        /// </summary>
        public void Dispose()
        {
            StopLoading();
            UnloadCurrent();
        }
        #endregion

        #region Loading Coroutines
        private IEnumerator LoadImageCoroutine(string path, bool fullResolution)
        {
            IsLoading = true;
            OnLoadProgress?.Invoke(0f);

            // Check if file exists
            if (!System.IO.File.Exists(path))
            {
                OnError?.Invoke($"File not found: {path}");
                IsLoading = false;
                yield break;
            }

            OnLoadProgress?.Invoke(0.1f);

            // Determine max size based on resolution request
            int maxSize = fullResolution ? 0 : PREVIEW_MAX_SIZE;

            // Load using UnityWebRequest for better memory management
            string fileUrl = "file:///" + path.Replace("\\", "/");

            using (var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(fileUrl, true))
            {
                var asyncOp = request.SendWebRequest();

                while (!asyncOp.isDone)
                {
                    OnLoadProgress?.Invoke(0.1f + asyncOp.progress * 0.8f);
                    yield return null;
                }

                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    OnError?.Invoke($"Failed to load image: {request.error}");
                    IsLoading = false;
                    yield break;
                }

                OnLoadProgress?.Invoke(0.9f);

                // Get texture
                Texture2D loadedTexture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);

                if (loadedTexture == null)
                {
                    OnError?.Invoke("Failed to decode image");
                    IsLoading = false;
                    yield break;
                }

                // Check memory and resize if needed
                CheckMemoryAndResize(ref loadedTexture, maxSize);

                // Unload previous texture
                UnloadCurrent();

                // Set new texture
                CurrentTexture = loadedTexture;
                _currentTextureMemory += GetTextureMemory(loadedTexture);

                OnLoadProgress?.Invoke(1f);
                OnImageLoaded?.Invoke(loadedTexture);

                Debug.Log($"[ImageLoadingEngine] Loaded: {path} ({loadedTexture.width}x{loadedTexture.height})");
            }

            IsLoading = false;
        }

        private IEnumerator LoadThumbnailCoroutine(string path, Action<Texture2D> callback)
        {
            if (!System.IO.File.Exists(path))
            {
                callback?.Invoke(null);
                yield break;
            }

            string fileUrl = "file:///" + path.Replace("\\", "/");

            using (var request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(fileUrl, true))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null);
                    yield break;
                }

                Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);

                if (texture != null)
                {
                    // Resize to thumbnail if needed
                    if (texture.width > THUMBNAIL_MAX_SIZE || texture.height > THUMBNAIL_MAX_SIZE)
                    {
                        texture = ResizeTexture(texture, THUMBNAIL_MAX_SIZE);
                    }
                }

                callback?.Invoke(texture);
            }
        }
        #endregion

        #region Memory Management
        private void CheckMemoryAndResize(ref Texture2D texture, int maxSize)
        {
            if (texture == null) return;

            long textureMemory = GetTextureMemory(texture);
            long totalAfterLoad = _currentTextureMemory + textureMemory;

            // If we would exceed memory limit, resize
            if (totalAfterLoad > MAX_TEXTURE_MEMORY_MB * 1024 * 1024)
            {
                int targetSize = maxSize > 0 ? maxSize : PREVIEW_MAX_SIZE;
                if (texture.width > targetSize || texture.height > targetSize)
                {
                    texture = ResizeTexture(texture, targetSize);
                    Debug.Log($"[ImageLoadingEngine] Resized texture to {texture.width}x{texture.height} due to memory limit");
                }
            }
            else if (maxSize > 0 && (texture.width > maxSize || texture.height > maxSize))
            {
                texture = ResizeTexture(texture, maxSize);
            }
        }

        private Texture2D ResizeTexture(Texture2D source, int maxSize)
        {
            float scale = Mathf.Min((float)maxSize / source.width, (float)maxSize / source.height);
            if (scale >= 1f) return source;

            int newWidth = Mathf.RoundToInt(source.width * scale);
            int newHeight = Mathf.RoundToInt(source.height * scale);

            // Create render texture for resizing
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture.active = rt;
            Graphics.Blit(source, rt);

            Texture2D resized = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            // Destroy original
            Destroy(source);

            return resized;
        }

        private long GetTextureMemory(Texture2D texture)
        {
            if (texture == null) return 0;
            // Approximate: width * height * bytes per pixel
            int bpp = texture.format == TextureFormat.RGBA32 ? 4 : 3;
            return (long)texture.width * texture.height * bpp;
        }
        #endregion

        #region Unity Lifecycle
        private void OnDestroy()
        {
            Dispose();
        }
        #endregion
    }

}
