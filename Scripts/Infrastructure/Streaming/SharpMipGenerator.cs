using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Generates sharp mipmaps using Lanczos-2 inspired downsampling.
    ///
    /// Unity's default GenerateMips() uses a box filter (2x2 average) which
    /// produces excessively blurry mipmaps. This is particularly bad for
    /// text/desktop content where edge sharpness is critical.
    ///
    /// This generator uses a 4x4 Lanczos-like kernel with negative lobes
    /// that preserve edge detail during downscaling. The result is mipmaps
    /// that are properly anti-aliased (no shimmer) but significantly sharper
    /// than box-filter mipmaps (less blur).
    ///
    /// Usage:
    ///   SharpMipGenerator.Generate(renderTexture, sharpness: 0.1f);
    ///
    /// Sharpness values:
    ///   0.0    = box filter (same as GenerateMips)
    ///   0.0625 = Lanczos-2 standard
    ///   0.10   = recommended for desktop/text streaming
    ///   0.15   = aggressive, good for text-heavy content
    ///   0.20   = very aggressive (may introduce ringing on some content)
    /// </summary>
    public static class SharpMipGenerator
    {
        private static Material _downsampleMaterial;
        private static readonly int SharpnessId = Shader.PropertyToID("_Sharpness");

        /// <summary>
        /// Get or create the sharp downsample material (lazy init, cached).
        /// </summary>
        private static Material GetMaterial()
        {
            if (_downsampleMaterial == null)
            {
                var shader = Shader.Find("Hidden/SharpDownsample");
                if (shader == null)
                {
                    Debug.LogError("[SharpMipGen] Shader 'Hidden/SharpDownsample' not found! Falling back to GenerateMips.");
                    return null;
                }
                _downsampleMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return _downsampleMaterial;
        }

        /// <summary>
        /// Generate sharp mipmaps for a RenderTexture.
        /// The RT must have useMipMap=true and autoGenerateMips=false.
        /// Mip level 0 must already contain the source image (via Graphics.Blit or similar).
        /// </summary>
        /// <param name="rt">Target RenderTexture with mipmaps enabled</param>
        /// <param name="sharpness">Sharpness of the downsample filter (0 = box, 0.1 = recommended)</param>
        /// <param name="maxMipLevels">Maximum number of mip levels to generate (0 = all)</param>
        public static void Generate(RenderTexture rt, float sharpness = 0.1f, int maxMipLevels = 0)
        {
            if (rt == null || !rt.useMipMap)
            {
                return;
            }

            var mat = GetMaterial();
            if (mat == null)
            {
                // Fallback: use Unity's default
                rt.GenerateMips();
                return;
            }

            mat.SetFloat(SharpnessId, sharpness);

            int totalMips = maxMipLevels > 0
                ? Mathf.Min(maxMipLevels, GetMipCount(rt.width, rt.height))
                : GetMipCount(rt.width, rt.height);

            // Temporarily force Bilinear on the source RT to prevent trilinear
            // from blending mip 0 (initialized) with mip 1+ (uninitialized) during
            // the first iteration. This is the root cause of alpha < 1 at higher mips.
            var prevFilter = rt.filterMode;
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture prev = null;

            for (int mip = 1; mip <= totalMips; mip++)
            {
                int w = Mathf.Max(1, rt.width >> mip);
                int h = Mathf.Max(1, rt.height >> mip);

                // Create temp RT at this mip's resolution
                var temp = RenderTexture.GetTemporary(w, h, 0, rt.format);
                temp.filterMode = FilterMode.Bilinear;

                // Source: mip 0 of rt (first iteration) or previous level's temp
                Texture source = prev != null ? (Texture)prev : rt;
                Graphics.Blit(source, temp, mat);

                // Copy the sharp-downsampled result to the specific mip level of rt
                Graphics.CopyTexture(temp, 0, 0, 0, 0, w, h, rt, 0, mip, 0, 0);

                // Release previous temp, keep current for next iteration
                if (prev != null)
                    RenderTexture.ReleaseTemporary(prev);
                prev = temp;
            }

            // Release the last temp
            if (prev != null)
                RenderTexture.ReleaseTemporary(prev);

            // Restore original filter mode
            rt.filterMode = prevFilter;
        }

        /// <summary>
        /// Calculate number of mip levels for given dimensions.
        /// </summary>
        private static int GetMipCount(int width, int height)
        {
            int size = Mathf.Max(width, height);
            int count = 0;
            while (size > 1)
            {
                size >>= 1;
                count++;
            }
            return count;
        }
    }
}
