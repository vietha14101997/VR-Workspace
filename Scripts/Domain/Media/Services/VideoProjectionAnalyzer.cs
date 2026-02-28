using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;
using System;
using System.Collections;
using Unity.Collections;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.UI;

namespace VRWorkspace.Media.Utils
{
    /// <summary>
    /// Analyzes video frames to detect projection type and stereo mode.
    /// Used as fallback when video metadata (sv3d/st3d/XMP) is unavailable.
    ///
    /// Detection methods:
    /// - Stereo (SBS/OU): Normalized Cross-Correlation between frame halves
    /// - Equirectangular: Pole compression (low variance at top/bottom rows)
    /// - 360 vs 180: Edge wrap continuity (left edge ≈ right edge for 360)
    ///
    /// Captures 3 frames at different timestamps (10%, 35%, 70%) and uses majority voting.
    /// </summary>
    public static class VideoProjectionAnalyzer
    {
        // Analysis resolution (small for speed, preserves aspect ratio)
        private const int ANALYSIS_MAX_DIM = 256;

        // Stereo detection: NCC threshold for left/right or top/bottom halves
        // Stereo pairs have similar content with slight parallax → NCC typically 0.65-0.95
        // Non-stereo halves show different content → NCC typically < 0.4
        private const float STEREO_NCC_THRESHOLD = 0.6f;

        // Equirectangular: pole variance must be < this fraction of middle variance
        private const float EQUIRECT_POLE_RATIO_THRESHOLD = 0.35f;

        // Minimum middle variance to reliably detect equirectangular (avoids false positives on uniform content)
        private const float MIN_MIDDLE_VARIANCE = 0.003f;

        // Maximum pole absolute variance for equirectangular (poles should be nearly uniform)
        private const float MAX_POLE_ABS_VARIANCE = 0.012f;

        // Timeouts
        private const float PREPARE_TIMEOUT = 8f;
        private const float FRAME_CAPTURE_TIMEOUT = 5f;

        // Cancellation support
        private static int _analyzeSequenceId = 0;
        private static GameObject _tempGO;
        private static RenderTexture _tempRT;

        /// <summary>
        /// Cancel any pending analysis and clean up resources.
        /// Call before starting a new analysis or when switching videos.
        /// </summary>
        public static void CancelPendingAnalysis()
        {
            _analyzeSequenceId++;
            CleanupTemp();
        }

        private static void CleanupTemp()
        {
            if (_tempGO != null)
            {
                var vp = _tempGO.GetComponent<VideoPlayer>();
                if (vp != null)
                {
                    vp.sendFrameReadyEvents = false;
                    vp.Stop();
                    vp.targetTexture = null;
                }
                UnityEngine.Object.Destroy(_tempGO);
                _tempGO = null;
            }

            if (_tempRT != null)
            {
                _tempRT.Release();
                UnityEngine.Object.Destroy(_tempRT);
                _tempRT = null;
            }
        }

        /// <summary>
        /// Analyze video frames to detect projection and stereo mode.
        /// Creates a temporary VideoPlayer, captures frames at 3 timestamps, analyzes content.
        /// Call via StartCoroutine() from a MonoBehaviour.
        /// </summary>
        /// <param name="videoPath">Full path to the video file</param>
        /// <param name="videoWidth">Native video width (from playback engine)</param>
        /// <param name="videoHeight">Native video height (from playback engine)</param>
        /// <param name="onResult">Callback with detected projection type and stereo mode</param>
        public static IEnumerator Analyze(
            string videoPath, int videoWidth, int videoHeight,
            Action<VideoProjectionType, StereoMode> onResult)
        {
            // Cancel any previous analysis
            CancelPendingAnalysis();
            int mySequence = _analyzeSequenceId;

            var resultProjection = VideoProjectionType.Flat;
            var resultStereo = StereoMode.Mono;

            // --- Setup temporary VideoPlayer ---
            _tempGO = new GameObject("_VideoProjectionAnalyzer");
            var vp = _tempGO.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.skipOnDrop = true;
            vp.isLooping = false;
            vp.aspectRatio = VideoAspectRatio.NoScaling;

            bool frameReady = false;
            vp.frameReady += (source, idx) => { frameReady = true; };

            // --- Prepare video ---
            vp.url = "file://" + videoPath;
            vp.Prepare();

            float elapsed = 0f;
            while (!vp.isPrepared && elapsed < PREPARE_TIMEOUT)
            {
                if (_analyzeSequenceId != mySequence) { CleanupTemp(); yield break; }
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!vp.isPrepared)
            {
                Debug.LogWarning("[VideoProjectionAnalyzer] Prepare timeout");
                CleanupTemp();
                onResult?.Invoke(resultProjection, resultStereo);
                yield break;
            }

            // --- Calculate analysis resolution ---
            int natW = (int)vp.width;
            int natH = (int)vp.height;

            if (natW <= 0 || natH <= 0)
            {
                CleanupTemp();
                onResult?.Invoke(resultProjection, resultStereo);
                yield break;
            }

            // Use native dimensions if provided are zero (e.g. from PlayVideoSimple)
            if (videoWidth <= 0) videoWidth = natW;
            if (videoHeight <= 0) videoHeight = natH;

            float aspect = (float)natW / natH;
            int analysisWidth, analysisHeight;

            if (natW >= natH)
            {
                analysisWidth = Mathf.Min(natW, ANALYSIS_MAX_DIM);
                analysisHeight = Mathf.Max(2, Mathf.RoundToInt(analysisWidth / aspect));
            }
            else
            {
                analysisHeight = Mathf.Min(natH, ANALYSIS_MAX_DIM);
                analysisWidth = Mathf.Max(2, Mathf.RoundToInt(analysisHeight * aspect));
            }

            // Ensure even dimensions for clean half-splitting
            analysisWidth = (analysisWidth / 2) * 2;
            analysisHeight = (analysisHeight / 2) * 2;
            if (analysisWidth < 4) analysisWidth = 4;
            if (analysisHeight < 4) analysisHeight = 4;

            _tempRT = new RenderTexture(analysisWidth, analysisHeight, 0, RenderTextureFormat.ARGB32);
            _tempRT.Create();
            vp.targetTexture = _tempRT;

            // --- Determine seek timestamps ---
            double duration = vp.length;
            double[] timestamps;

            if (duration >= 5.0)
            {
                timestamps = new double[] { duration * 0.10, duration * 0.35, duration * 0.70 };
            }
            else if (duration >= 1.0)
            {
                timestamps = new double[] { 0.1, duration * 0.5, duration * 0.9 };
            }
            else
            {
                // Very short video: just capture one frame
                timestamps = new double[] { 0.0 };
            }

            // --- Capture and analyze frames ---
            int sbsVotes = 0, ouVotes = 0, equirectVotes = 0, wrapVotes = 0;
            int totalFrames = 0;

            vp.sendFrameReadyEvents = true;

            for (int i = 0; i < timestamps.Length; i++)
            {
                if (_analyzeSequenceId != mySequence) { CleanupTemp(); yield break; }

                frameReady = false;
                vp.time = timestamps[i];
                vp.Play();

                // Wait for frame to render
                elapsed = 0f;
                while (!frameReady && elapsed < FRAME_CAPTURE_TIMEOUT)
                {
                    if (_analyzeSequenceId != mySequence) { CleanupTemp(); yield break; }
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (!frameReady)
                {
                    vp.Pause();
                    continue;
                }

                // Extra frame for GPU to finalize render-to-texture
                yield return null;
                vp.Pause();

                if (_analyzeSequenceId != mySequence) { CleanupTemp(); yield break; }

                // --- Read pixels via AsyncGPUReadback ---
                Color32[] pixels = null;
                bool readDone = false;

                AsyncGPUReadback.Request(_tempRT, 0, TextureFormat.RGBA32, (request) =>
                {
                    if (!request.hasError)
                    {
                        NativeArray<Color32> data = request.GetData<Color32>();
                        pixels = data.ToArray();
                    }
                    readDone = true;
                });

                while (!readDone)
                {
                    if (_analyzeSequenceId != mySequence) { CleanupTemp(); yield break; }
                    yield return null;
                }

                if (pixels == null || pixels.Length != analysisWidth * analysisHeight)
                    continue;

                totalFrames++;

                // --- Analyze this frame ---

                // 1. Stereo detection
                float sbsNCC = ComputeHalfNCC(pixels, analysisWidth, analysisHeight, horizontal: true);
                float ouNCC = ComputeHalfNCC(pixels, analysisWidth, analysisHeight, horizontal: false);

                bool isSBS = sbsNCC > STEREO_NCC_THRESHOLD;
                bool isOU = ouNCC > STEREO_NCC_THRESHOLD;

                // If both detected, prefer the one with higher NCC
                if (isSBS && isOU)
                {
                    if (sbsNCC >= ouNCC) isOU = false;
                    else isSBS = false;
                }

                if (isSBS) sbsVotes++;
                if (isOU) ouVotes++;

                // 2. Equirectangular detection (on the relevant region)
                Color32[] equirectRegion;
                int eqW, eqH;

                if (isSBS)
                {
                    // Analyze left half only (one eye's view)
                    eqW = analysisWidth / 2;
                    eqH = analysisHeight;
                    equirectRegion = ExtractHalf(pixels, analysisWidth, analysisHeight, leftOrTop: true, horizontal: true);
                }
                else if (isOU)
                {
                    // Analyze top half only
                    eqW = analysisWidth;
                    eqH = analysisHeight / 2;
                    equirectRegion = ExtractHalf(pixels, analysisWidth, analysisHeight, leftOrTop: true, horizontal: false);
                }
                else
                {
                    // Analyze full frame
                    eqW = analysisWidth;
                    eqH = analysisHeight;
                    equirectRegion = pixels;
                }

                bool isEquirect = DetectEquirectangular(equirectRegion, eqW, eqH);
                if (isEquirect) equirectVotes++;

                // 3. Edge wrap detection (360 vs 180) — only meaningful if equirectangular
                bool isWrap = false;
                if (isEquirect)
                {
                    isWrap = DetectEdgeWrap(equirectRegion, eqW, eqH);
                    if (isWrap) wrapVotes++;
                }

                Debug.Log($"[VideoProjectionAnalyzer] Frame {i} @{timestamps[i]:F1}s: " +
                          $"SBS_NCC={sbsNCC:F3}, OU_NCC={ouNCC:F3}, " +
                          $"Equirect={isEquirect}, Wrap={isWrap}");
            }

            // --- Cleanup ---
            vp.sendFrameReadyEvents = false;
            CleanupTemp();

            if (totalFrames == 0)
            {
                Debug.LogWarning("[VideoProjectionAnalyzer] No frames captured, defaulting to Flat/Mono");
                onResult?.Invoke(VideoProjectionType.Flat, StereoMode.Mono);
                yield break;
            }

            // --- Majority voting ---
            int majority = (totalFrames + 1) / 2;

            // Stereo decision
            if (sbsVotes >= majority)
                resultStereo = StereoMode.SideBySide;
            else if (ouVotes >= majority)
                resultStereo = StereoMode.OverUnder;
            else
                resultStereo = StereoMode.Mono;

            // Projection decision
            bool isEquirectangular = equirectVotes >= majority;
            bool is360 = wrapVotes >= majority;
            float totalAspect = (float)natW / natH;

            if (isEquirectangular)
            {
                resultProjection = DetermineEquirectProjection(resultStereo, totalAspect, is360);
            }
            else
            {
                // Flat content
                switch (resultStereo)
                {
                    case StereoMode.SideBySide:
                        resultProjection = VideoProjectionType.SideBySide3D;
                        break;
                    case StereoMode.OverUnder:
                        resultProjection = VideoProjectionType.OverUnder3D;
                        break;
                    default:
                        resultProjection = VideoProjectionType.Flat;
                        break;
                }
            }

            Debug.Log($"[VideoProjectionAnalyzer] Result: {resultProjection}, {resultStereo} " +
                      $"(frames={totalFrames}, SBS={sbsVotes}, OU={ouVotes}, " +
                      $"Equirect={equirectVotes}, Wrap={wrapVotes}, aspect={totalAspect:F2})");

            onResult?.Invoke(resultProjection, resultStereo);
        }

        /// <summary>
        /// Determine the specific equirectangular projection type based on stereo, aspect ratio, and edge wrap.
        /// </summary>
        private static VideoProjectionType DetermineEquirectProjection(StereoMode stereo, float totalAspect, bool edgeWrap)
        {
            switch (stereo)
            {
                case StereoMode.SideBySide:
                    // 180 SBS: total 2:1 (each eye 1:1, 180° equirect)
                    // 360 SBS: total 1:1 (each eye 2:1 squeezed to 1:2)
                    if (totalAspect >= 1.7f) return VideoProjectionType.Dome180;
                    if (totalAspect <= 1.2f) return VideoProjectionType.Sphere360;
                    return edgeWrap ? VideoProjectionType.Sphere360 : VideoProjectionType.Dome180;

                case StereoMode.OverUnder:
                    // 180 OU: total 1:1 (each eye 1:1 stacked vertically)
                    // 360 OU: total 1:1 (each eye 2:1 squeezed to 1:2, stacked → total 1:1)
                    if (totalAspect <= 1.2f) return VideoProjectionType.Dome180;
                    if (totalAspect >= 1.7f) return VideoProjectionType.Sphere360;
                    return edgeWrap ? VideoProjectionType.Sphere360 : VideoProjectionType.Dome180;

                default: // Mono
                    // 360 Mono: 2:1 (standard equirectangular)
                    // 180 Mono: 1:1 (hemisphere)
                    if (totalAspect >= 1.7f) return VideoProjectionType.Sphere360;
                    if (totalAspect <= 1.2f) return VideoProjectionType.Dome180;
                    return edgeWrap ? VideoProjectionType.Sphere360 : VideoProjectionType.Dome180;
            }
        }

        #region Pixel Analysis

        /// <summary>
        /// Compute Normalized Cross-Correlation between two halves of the frame.
        /// horizontal=true: left vs right halves (SBS detection)
        /// horizontal=false: top vs bottom halves (OU detection)
        /// </summary>
        private static float ComputeHalfNCC(Color32[] pixels, int width, int height, bool horizontal)
        {
            Color32[] halfA, halfB;
            int halfW, halfH;

            if (horizontal)
            {
                halfW = width / 2;
                halfH = height;
                halfA = ExtractHalf(pixels, width, height, leftOrTop: true, horizontal: true);
                halfB = ExtractHalf(pixels, width, height, leftOrTop: false, horizontal: true);
            }
            else
            {
                halfW = width;
                halfH = height / 2;
                halfA = ExtractHalf(pixels, width, height, leftOrTop: true, horizontal: false);
                halfB = ExtractHalf(pixels, width, height, leftOrTop: false, horizontal: false);
            }

            // Downscale to 32x32 for fast NCC computation
            const int DS = 32;
            float[] lumA = DownscaleToLuminance(halfA, halfW, halfH, DS, DS);
            float[] lumB = DownscaleToLuminance(halfB, halfW, halfH, DS, DS);

            return NCC(lumA, lumB);
        }

        /// <summary>
        /// Extract one half of the pixel array (left/right or top/bottom).
        /// </summary>
        private static Color32[] ExtractHalf(Color32[] pixels, int width, int height, bool leftOrTop, bool horizontal)
        {
            Color32[] result;

            if (horizontal)
            {
                int halfW = width / 2;
                result = new Color32[halfW * height];
                int startX = leftOrTop ? 0 : halfW;

                for (int y = 0; y < height; y++)
                {
                    Array.Copy(pixels, y * width + startX, result, y * halfW, halfW);
                }
            }
            else
            {
                int halfH = height / 2;
                result = new Color32[width * halfH];
                int startY = leftOrTop ? 0 : halfH;

                Array.Copy(pixels, startY * width, result, 0, width * halfH);
            }

            return result;
        }

        /// <summary>
        /// Downscale pixel data to luminance values at target resolution using nearest-neighbor sampling.
        /// </summary>
        private static float[] DownscaleToLuminance(Color32[] pixels, int srcW, int srcH, int dstW, int dstH)
        {
            float[] result = new float[dstW * dstH];
            float xScale = (float)srcW / dstW;
            float yScale = (float)srcH / dstH;

            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = Mathf.Min(Mathf.FloorToInt(dy * yScale), srcH - 1);
                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = Mathf.Min(Mathf.FloorToInt(dx * xScale), srcW - 1);
                    Color32 c = pixels[sy * srcW + sx];
                    // ITU-R BT.709 luminance
                    result[dy * dstW + dx] = c.r * 0.2126f / 255f + c.g * 0.7152f / 255f + c.b * 0.0722f / 255f;
                }
            }

            return result;
        }

        /// <summary>
        /// Normalized Cross-Correlation between two luminance arrays.
        /// Returns 1.0 for identical, 0.0 for uncorrelated, -1.0 for inverse.
        /// </summary>
        private static float NCC(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0f;

            float meanA = 0f, meanB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                meanA += a[i];
                meanB += b[i];
            }
            meanA /= a.Length;
            meanB /= b.Length;

            float sumAB = 0f, sumAA = 0f, sumBB = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float da = a[i] - meanA;
                float db = b[i] - meanB;
                sumAB += da * db;
                sumAA += da * da;
                sumBB += db * db;
            }

            float denom = Mathf.Sqrt(sumAA * sumBB);
            if (denom < 1e-8f) return 0f;

            return sumAB / denom;
        }

        /// <summary>
        /// Detect equirectangular projection by checking pole compression.
        /// In equirectangular mapping, the poles (top/bottom of image) are stretched
        /// into horizontal bands with very low horizontal variance.
        /// </summary>
        private static bool DetectEquirectangular(Color32[] pixels, int width, int height)
        {
            if (height < 20 || width < 20) return false;

            // Use top/bottom 8% of rows for pole check
            int poleRows = Mathf.Max(2, height * 8 / 100);

            float topVariance = ComputeRowsVariance(pixels, width, height, 0, poleRows);
            float bottomVariance = ComputeRowsVariance(pixels, width, height, height - poleRows, height);
            float middleVariance = ComputeRowsVariance(pixels, width, height, height / 3, height * 2 / 3);

            // Content too uniform to reliably distinguish (e.g. all-black, loading screen)
            if (middleVariance < MIN_MIDDLE_VARIANCE) return false;

            // Both poles must show compression relative to middle
            bool topPoleOK = topVariance < middleVariance * EQUIRECT_POLE_RATIO_THRESHOLD;
            bool bottomPoleOK = bottomVariance < middleVariance * EQUIRECT_POLE_RATIO_THRESHOLD;

            // Poles must also have low absolute variance (nearly uniform color)
            float avgPoleVariance = (topVariance + bottomVariance) / 2f;
            bool poleAbsOK = avgPoleVariance < MAX_POLE_ABS_VARIANCE;

            return topPoleOK && bottomPoleOK && poleAbsOK;
        }

        /// <summary>
        /// Compute average horizontal luminance variance across a range of rows.
        /// Higher variance = more detail/variation in the horizontal direction.
        /// </summary>
        private static float ComputeRowsVariance(Color32[] pixels, int width, int height, int startRow, int endRow)
        {
            float totalVariance = 0f;
            int rowCount = 0;

            for (int y = startRow; y < endRow && y < height; y++)
            {
                // Compute mean luminance for this row
                float mean = 0f;
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[y * width + x];
                    mean += c.r * 0.2126f / 255f + c.g * 0.7152f / 255f + c.b * 0.0722f / 255f;
                }
                mean /= width;

                // Compute variance
                float variance = 0f;
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[y * width + x];
                    float lum = c.r * 0.2126f / 255f + c.g * 0.7152f / 255f + c.b * 0.0722f / 255f;
                    float diff = lum - mean;
                    variance += diff * diff;
                }
                variance /= width;

                totalVariance += variance;
                rowCount++;
            }

            return rowCount > 0 ? totalVariance / rowCount : 0f;
        }

        /// <summary>
        /// Detect 360° edge wrapping by comparing leftmost and rightmost columns.
        /// 360° equirectangular images wrap around, so left and right edges are continuous.
        /// 180° images have discontinuous or black edges.
        /// </summary>
        private static bool DetectEdgeWrap(Color32[] pixels, int width, int height)
        {
            if (width < 10 || height < 10) return false;

            // Compare luminance of left 3 columns vs right 3 columns
            int edgeCols = 3;
            int sampleRows = Mathf.Min(height, 64);
            float rowStep = (float)height / sampleRows;

            float sumDiff = 0f;
            float sumLeftLum = 0f;
            float sumRightLum = 0f;

            for (int i = 0; i < sampleRows; i++)
            {
                int y = Mathf.Min(Mathf.FloorToInt(i * rowStep), height - 1);

                float leftLum = 0f;
                float rightLum = 0f;

                for (int c = 0; c < edgeCols; c++)
                {
                    Color32 lc = pixels[y * width + c];
                    Color32 rc = pixels[y * width + (width - 1 - c)];

                    leftLum += lc.r * 0.2126f / 255f + lc.g * 0.7152f / 255f + lc.b * 0.0722f / 255f;
                    rightLum += rc.r * 0.2126f / 255f + rc.g * 0.7152f / 255f + rc.b * 0.0722f / 255f;
                }

                leftLum /= edgeCols;
                rightLum /= edgeCols;

                sumDiff += Mathf.Abs(leftLum - rightLum);
                sumLeftLum += leftLum;
                sumRightLum += rightLum;
            }

            float avgDiff = sumDiff / sampleRows;
            float avgLeftLum = sumLeftLum / sampleRows;
            float avgRightLum = sumRightLum / sampleRows;
            float avgLum = (avgLeftLum + avgRightLum) / 2f;

            // Black edges = not wrapping (common in 180° content)
            if (avgLeftLum < 0.05f || avgRightLum < 0.05f)
                return false;

            // Too dark overall to judge
            if (avgLum < 0.02f) return false;

            // Low relative difference = edges match = wrapping (360°)
            float relDiff = avgDiff / Mathf.Max(avgLum, 0.01f);
            return relDiff < 0.3f;
        }

        #endregion
    }

}
