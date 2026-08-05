using UnityEngine;
using System.Collections;
using VRWorkspace.Media.Data;
using VRWorkspace.Media.UI;
using VRWorkspace.Panel;

namespace VRWorkspace.Media.Projections
{
    /// <summary>
    /// Flat screen projection renderer for standard 2D video.
    /// Supports optional screen curvature for immersive viewing.
    /// </summary>
    public class FlatProjectionRenderer : MonoBehaviour, IProjectionRenderer
    {
        #region Constants
        private const string SHADER_NAME = "VRWorkspace/Media/VideoFlatProjection";
        private const string FALLBACK_SHADER = "Unlit/Texture";
        private const int CURVED_SEGMENTS = 32;
        #endregion

        #region Properties
        public VideoProjectionType Type => VideoProjectionType.Flat;
        public bool IsActive => _isActive;
        #endregion

        #region Private Fields
        private bool _isActive = false;
        private bool _isInitialized = false;

        private WorldPanelPlus _worldPanel;
        private Transform _parentTransform;
        private Vector2Int _resolution = new Vector2Int(1920, 1080);
        private DisplaySettings _currentSettings = DisplaySettings.Default;

        // Curvature tracking
        private float _currentCurvature = 0f;
        private Mesh _curvedMesh;

        // Recenter tracking
        private Quaternion _recenterRotation = Quaternion.identity;
        #endregion

        #region IProjectionRenderer Implementation
        public void Initialize(Transform parent)
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[FlatProjectionRenderer] Already initialized");
                return;
            }

            _parentTransform = parent;
            CreateScreenObject();
            _isInitialized = true;
            Hide(); // Start hidden
        }

        public void SetTexture(Texture texture)
        {
            if (_worldPanel == null) return;

            _worldPanel.contentTexture = texture;
            _worldPanel.Apply();
            
            // Update resolution for aspect ratio calculations
            if (texture != null)
            {
                _resolution = new Vector2Int(texture.width, texture.height);
                UpdateScreenAspect();
            }
        }

        public void SetTextureNV12(Texture2D yPlane, Texture2D uvPlane)
        {
            // WorldPanelPlus standard shader doesn't support NV12 out of the box with edge feathering.
            // For now, we fallback to setting the Y plane as content (grayscale) or would need a custom shader.
            // Assuming Windows platform where direct texture is mostly used.
            // If NV12 is strict requirement, we would need to swap the WorldPanelPlus material shader here.

            if (_worldPanel == null) return;

            _worldPanel.contentTexture = yPlane; // Fallback
            _worldPanel.Apply();

            if (yPlane != null)
            {
                _resolution = new Vector2Int(yPlane.width, yPlane.height);
                UpdateScreenAspect();
            }
        }

        public void SetStereoMode(StereoMode mode)
        {
            if (_worldPanel == null) return;

            _worldPanel.stereoMode = mode;
            _worldPanel.Apply();
        }

        public void SetStereoStrength(float strength)
        {
            if (_worldPanel == null) return;
            var rend = _worldPanel.board != null ? _worldPanel.board.GetComponent<Renderer>() : null;
            if (rend != null && rend.material.HasProperty("_StereoStrength"))
            {
                rend.material.SetFloat("_StereoStrength", Mathf.Clamp01(strength));
            }
        }

        private Coroutine _stereoTransitionCoroutine;
        private const float STEREO_TRANSITION_DURATION = 0.2f;

        /// <summary>
        /// Switch stereo mode with scale animation (shrink → switch → grow).
        /// </summary>
        public void SetStereoModeAnimated(StereoMode mode)
        {
            if (_worldPanel == null) return;
            if (_stereoTransitionCoroutine != null)
                StopCoroutine(_stereoTransitionCoroutine);
            _stereoTransitionCoroutine = StartCoroutine(StereoTransitionCoroutine(mode));
        }

        private IEnumerator StereoTransitionCoroutine(StereoMode mode)
        {
            // 1. Tính trước kích thước đích
            Vector3 targetScale = CalculateBoardScale(mode);
            float origW = _worldPanel.width;
            float origH = _worldPanel.height;
            float targetW = targetScale.x;
            float targetH = targetScale.y;

            // 2. Chuyển hình chiếu sang dạng đích trước (width/height giữ nguyên → board scale không đổi)
            _worldPanel.stereoMode = mode;
            _worldPanel.Apply();

            // 3. Animation kích thước (EaseOut: nhanh đầu, chậm cuối)
            float elapsed = 0f;
            float duration = STEREO_TRANSITION_DURATION * 2f; // 0.4s total
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float linear = Mathf.Clamp01(elapsed / duration);
                float t = 1f - (1f - linear) * (1f - linear) * (1f - linear); // EaseOutCubic
                _worldPanel.width = Mathf.Lerp(origW, targetW, t);
                _worldPanel.height = Mathf.Lerp(origH, targetH, t);
                _worldPanel.Apply();
                yield return null;
            }

            // 4. Gắn kích thước đích
            _worldPanel.width = targetW;
            _worldPanel.height = targetH;
            _worldPanel.Apply();

            _stereoTransitionCoroutine = null;
        }

        /// <summary>
        /// Calculate board scale for a given stereo mode without modifying any state.
        /// Mirrors the logic in UpdateScreenAspect().
        /// </summary>
        private Vector3 CalculateBoardScale(StereoMode mode)
        {
            float resX = _resolution.x;
            float resY = _resolution.y;

            if (mode == StereoMode.SideBySide) resX /= 2f;
            else if (mode == StereoMode.OverUnder) resY /= 2f;

            float aspect = resY > 0 ? resX / resY : 16f / 9f;
            float baseHeight = 1.0f * _currentSettings.Scale;
            float baseWidth = baseHeight * aspect;

            return new Vector3(baseWidth, baseHeight, 1f);
        }

        public void UpdateDisplay(DisplaySettings settings)
        {
            _currentSettings = settings;

            if (_worldPanel == null) return;

            // Position screen at local origin — parent transform (projection root)
            // is already placed at Distance from camera by VRVideoProjectionSystem.
            Vector3 localPos = new Vector3(
                settings.PositionOffset.x,
                settings.PositionOffset.y,
                0f
            );
            _worldPanel.transform.localPosition = localPos;

            // Keep facing away from parent origin (toward viewer)
            _worldPanel.transform.localRotation = settings.RotationOffset;

            // Initial curvature and mesh update
            float targetCurvature = settings.Curvature;
            if (Mathf.Abs(targetCurvature - _currentCurvature) > 0.001f)
            {
                _currentCurvature = targetCurvature;
                RebuildMesh();
            }

            // Update size based on scale and aspect ratio
            UpdateScreenAspect();
        }

        private void Update()
        {
            if (!_isActive || _worldPanel == null) return;

            // Dynamic curvature check: if curvature > 0, track actual world distance to camera
            if (_currentCurvature > 0.001f)
            {
                float actualDistance = 2.0f;
                if (Camera.main != null)
                {
                    actualDistance = Vector3.ProjectOnPlane(_worldPanel.transform.position - Camera.main.transform.position, Camera.main.transform.up).magnitude;
                }

                // Detect if distance changed significantly enough to rebuild mesh
                // We use a slightly larger threshold here for performance
                float trackedDistance = _currentSettings.Distance > 0.001f ? _currentSettings.Distance : actualDistance;

                // If settings.Distance is NOT being manually adjusted, we can follow world distance
                // Actually, let's just track world distance for curvature R if it's dynamic

                // For now, let's stick to theApproved logic: R matches distance (settings OR world)
                // If the user zooms the WHOLE parent, settings.Distance is constant. 
                // So we MUST track world distance for it to be "dynamic".

                if (Mathf.Abs(actualDistance - _lastTrackedWorldDistance) > 0.01f)
                {
                    _lastTrackedWorldDistance = actualDistance;
                    RebuildMesh();
                }
            }
        }

        private float _lastTrackedWorldDistance = -1f;

        public void Show()
        {
            if (_worldPanel != null)
            {
                _worldPanel.gameObject.SetActive(true);
                UpdateDisplay(_currentSettings); // Ensure position is updated

                // Initial distance tracking
                if (Camera.main != null)
                {
                    _lastTrackedWorldDistance = Vector3.ProjectOnPlane(_worldPanel.transform.position - Camera.main.transform.position, Camera.main.transform.up).magnitude;
                }
            }
            _isActive = true;
        }

        public void Hide()
        {
            if (_worldPanel != null)
            {
                _worldPanel.gameObject.SetActive(false);
            }
            _isActive = false;
            _lastTrackedWorldDistance = -1f;
        }

        public void SetBoardAlpha(float alpha)
        {
            if (_worldPanel == null) return;
            _worldPanel.boardAlpha = Mathf.Clamp01(alpha);
            _worldPanel.Apply();
        }

        /// <summary>
        /// Set a shader float property on the board material (WorldPanelBoard shader).
        /// Uses sharedMaterial because _panelMat is already a unique instance.
        /// Also syncs WorldPanelPlus fields for properties managed by UpdateBoardShaderProperties,
        /// so Apply() doesn't revert the value to the old field value.
        /// </summary>
        public void SetBoardShaderFloat(string property, float value)
        {
            if (_worldPanel == null || _worldPanel.board == null) return;

            // Sync WorldPanelPlus fields so Apply() won't overwrite
            switch (property)
            {
                case "_Sharpness": _worldPanel.sharpnessStrength = value; break;
                case "_ChromaSharpness": _worldPanel.chromaSharpness = value; break;
                case "_MipMapBias": _worldPanel.mipMapBias = value; break;
                case "_MaxMipLevel": _worldPanel.maxMipLevel = value; break;
            }

            var rend = _worldPanel.board.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(property))
                rend.sharedMaterial.SetFloat(property, value);
        }

        public float GetBoardShaderFloat(string property, float defaultVal = 0f)
        {
            if (_worldPanel == null || _worldPanel.board == null) return defaultVal;
            var rend = _worldPanel.board.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(property))
                return rend.sharedMaterial.GetFloat(property);
            return defaultVal;
        }

        /// <summary>
        /// Set aspect ratio override. Pass "default" to use video native aspect.
        /// </summary>
        private float _aspectRatioOverride = 0f;
        private string _aspectRatioString = "default";
        public string AspectRatioOverrideString => _aspectRatioString;

        public void SetAspectRatioOverride(string ratio)
        {
            _aspectRatioString = ratio ?? "default";
            switch (ratio)
            {
                case "3:2": _aspectRatioOverride = 3f / 2f; break;
                case "4:3": _aspectRatioOverride = 4f / 3f; break;
                case "9:16": _aspectRatioOverride = 9f / 16f; break;
                case "16:9": _aspectRatioOverride = 16f / 9f; break;
                case "21:9": _aspectRatioOverride = 21f / 9f; break;
                default: _aspectRatioOverride = 0f; break; // "default" = use native
            }
            UpdateScreenAspect();
        }

        public void RecenterView()
        {
            if (Camera.main != null)
            {
                // Store current camera forward as the new center
                Vector3 forward = Camera.main.transform.forward;
                forward.y = 0; // Keep horizontal
                if (forward.sqrMagnitude > 0.001f)
                {
                    _recenterRotation = Quaternion.LookRotation(forward.normalized);
                }
            }
            else
            {
                _recenterRotation = Quaternion.identity;
            }

            UpdateDisplay(_currentSettings);
        }

        public void Dispose()
        {
            if (_curvedMesh != null)
            {
                Destroy(_curvedMesh);
                _curvedMesh = null;
            }

            if (_worldPanel != null)
            {
                Destroy(_worldPanel.gameObject);
                _worldPanel = null;
            }

            _isInitialized = false;
            _isActive = false;
        }
        #endregion

        #region Private Methods
        private void CreateScreenObject()
        {
            GameObject go = new GameObject("FlatVideoScreen");
            go.transform.SetParent(_parentTransform, false);

            // Set layer - use Default if VirtualObjects doesn't exist
            int layer = LayerMask.NameToLayer("VirtualObjects");
            if (layer < 0) layer = 0;
            go.layer = layer;

            // Add WorldPanelPlus
            _worldPanel = go.AddComponent<WorldPanelPlus>();

            // Configure WorldPanelPlus defaults — preserve original video quality,
            // disable ALL enhancement passes (they alter source data without benefit
            // on a head-on viewed video panel):
            //   stableAA=false  → 5-tap tex2Dlod path → 1-tap tex2Dgrad (5× less texture bandwidth)
            //   shimmerBlend=0  → no extra trilinear blend
            //   temporalSmooth=0 → no previous-frame dependency (cache-friendly)
            //   maxMipLevel=0   → no mip chain lookups (we already disabled mips)
            _worldPanel.useBoardEdgeFeather = true;
            _worldPanel.boardEdgeWidthUV = 0.02f;
            _worldPanel.boardCornerRadius = 0.03f;
            _worldPanel.boardEdgeColor = Color.black;
            _worldPanel.panelTint = Color.white;
            _worldPanel.enableSharpening = false;
            _worldPanel.sharpnessStrength = 0f;
            _worldPanel.anisoLevel = 1;
            _worldPanel.mipMapBias = 0f;
            _worldPanel.maxMipLevel = 0f;
            _worldPanel.stableAA = false;
            _worldPanel.shimmerBlend = 0f;
            _worldPanel.temporalSmooth = 0f;
            _worldPanel.cursorEnable = false;

            // Initialize
            _worldPanel.Rebuild();

            // Remove BoxCollider — video screen is a projection, not an interactable entity.
            // This prevents the board from blocking raycasts to the controls panel behind it.
            if (_worldPanel.board != null)
            {
                var col = _worldPanel.board.GetComponent<BoxCollider>();
                if (col != null) Destroy(col);
            }
        }

        private void UpdateScreenAspect()
        {
            if (_worldPanel == null) return;

            float resX = _resolution.x;
            float resY = _resolution.y;

            // Adjust resolution for aspect ratio calculation based on stereo mode
            if (_worldPanel.stereoMode == StereoMode.SideBySide)
            {
                resX /= 2f;
            }
            else if (_worldPanel.stereoMode == StereoMode.OverUnder)
            {
                resY /= 2f;
            }

            float aspect = resY > 0 ? resX / resY : 16f / 9f;

            // Apply aspect ratio override if set
            if (_aspectRatioOverride > 0f)
                aspect = _aspectRatioOverride;

            // Base height of 1 meter, adjust width by aspect ratio
            float baseHeight = 1.0f * _currentSettings.Scale;
            float baseWidth = baseHeight * aspect;

            bool changed = false;
            if (Mathf.Abs(_worldPanel.width - baseWidth) > 0.001f)
            {
                _worldPanel.width = baseWidth;
                changed = true;
            }
            if (Mathf.Abs(_worldPanel.height - baseHeight) > 0.001f)
            {
                _worldPanel.height = baseHeight;
                changed = true;
            }

            if (changed)
            {
                // Apply scale changes (WorldPanelPlus Apply handles localScale based on width/height)
                _worldPanel.Apply();

                // Regenerate curved mesh if active (arc radius depends on width)
                if (_currentCurvature > 0.001f)
                {
                    ApplyCurvedMesh();
                }
            }
        }

        /// <summary>
        /// Rebuild mesh based on current curvature setting.
        /// Curvature 0 = flat quad, curvature > 0 = curved cylindrical arc.
        /// </summary>
        private void RebuildMesh()
        {
            if (_worldPanel == null || _worldPanel.board == null) return;

            if (_currentCurvature <= 0.001f)
            {
                // Flat mode: restore standard quad
                ApplyFlatQuad();
            }
            else
            {
                // Curved mode: generate cylindrical arc mesh
                ApplyCurvedMesh();
            }

            Debug.Log($"[FlatProjectionRenderer] RebuildMesh: curvature={_currentCurvature:F3}");
        }

        /// <summary>
        /// Restore WorldPanelPlus board to standard flat quad.
        /// </summary>
        private void ApplyFlatQuad()
        {
            if (_curvedMesh != null)
            {
                Destroy(_curvedMesh);
                _curvedMesh = null;
            }

            // Restore quad mesh on the board
            MeshFilter mf = _worldPanel.board.GetComponent<MeshFilter>();
            if (mf != null)
            {
                // Unity's built-in quad mesh
                var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mf.sharedMesh = quadGo.GetComponent<MeshFilter>().sharedMesh;
                Destroy(quadGo);
            }
        }

        /// <summary>
        /// Generate and apply a curved cylindrical arc mesh.
        /// Uses the same algorithm as CurvedClusterMeshGenerator:
        /// vertices at (sin(θ)*R, y, cos(θ)*R - R).
        /// </summary>
        private void ApplyCurvedMesh()
        {
            float width = _worldPanel.width;
            float height = _worldPanel.height;

            if (width <= 0 || height <= 0) return;

            // Calculate arc radius
            // R matches actual viewing distance, capped at 2.0m
            // If _currentCurvature > 0, we use this dynamic radius logic.
            float arcRadius = 2.0f; // Default cap

            // Prefer tracked world distance for dynamic curvature updates
            float effectiveDistance = (_lastTrackedWorldDistance > 0) ? _lastTrackedWorldDistance : _currentSettings.Distance;

            if (effectiveDistance > 0)
            {
                arcRadius = Mathf.Min(effectiveDistance, 2.0f);
            }

            // Ensure radius is not smaller than half-width to avoid invalid Atan
            arcRadius = Mathf.Max(arcRadius, width * 0.51f); 

            Debug.Log($"[FlatProjectionRenderer] RebuildMesh: width={width:F2}, effectiveDist={effectiveDistance:F2}, R={arcRadius:F2}");

            // Arc angle: 2 * atan(width / 2 / radius) - matches CurvedClusterMeshGenerator
            float totalArcAngleRad = 2f * Mathf.Atan(width / 2f / arcRadius);

            int segX = CURVED_SEGMENTS;
            int segY = 2; // Vertical segments (minimal needed)
            int vertexCountX = segX + 1;
            int vertexCountY = segY + 1;
            int vertexCount = vertexCountX * vertexCountY;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            for (int y = 0; y <= segY; y++)
            {
                float vt = (float)y / segY;
                float yPos = (vt - 0.5f); // -0.5 to 0.5 (unit mesh, scaled by board localScale)

                for (int x = 0; x <= segX; x++)
                {
                    float ut = (float)x / segX;

                    // Arc angle centered: ut=0 -> left edge, ut=1 -> right edge
                    float angle = (ut - 0.5f) * totalArcAngleRad;

                    // Cylindrical coordinates (same as CurvedClusterMeshGenerator)
                    // But normalized to unit mesh (-0.5 to 0.5 range) since WorldPanelPlus
                    // applies width/height via board localScale
                    float xPos = Mathf.Sin(angle) * arcRadius / width;
                    float zPos = (Mathf.Cos(angle) * arcRadius - arcRadius) / width;

                    int idx = y * vertexCountX + x;
                    vertices[idx] = new Vector3(xPos, yPos, zPos);

                    // Normal points toward arc center (viewer)
                    normals[idx] = new Vector3(-Mathf.Sin(angle), 0f, -Mathf.Cos(angle));

                    // Standard UV mapping 0-1
                    uvs[idx] = new Vector2(ut, vt);
                }
            }

            // Generate triangles
            int quadCount = segX * segY;
            int[] triangles = new int[quadCount * 6];
            int triIdx = 0;

            for (int y = 0; y < segY; y++)
            {
                for (int x = 0; x < segX; x++)
                {
                    int bl = y * vertexCountX + x;
                    int br = bl + 1;
                    int tl = bl + vertexCountX;
                    int tr = tl + 1;

                    // First triangle
                    triangles[triIdx++] = bl;
                    triangles[triIdx++] = tl;
                    triangles[triIdx++] = tr;

                    // Second triangle
                    triangles[triIdx++] = bl;
                    triangles[triIdx++] = tr;
                    triangles[triIdx++] = br;
                }
            }

            // Create or update mesh
            if (_curvedMesh == null)
            {
                _curvedMesh = new Mesh();
                _curvedMesh.name = "CurvedVideoScreen";
            }
            else
            {
                _curvedMesh.Clear();
            }

            _curvedMesh.vertices = vertices;
            _curvedMesh.normals = normals;
            _curvedMesh.uv = uvs;
            _curvedMesh.triangles = triangles;
            _curvedMesh.RecalculateBounds();
            _curvedMesh.RecalculateTangents();

            // Apply to board MeshFilter
            MeshFilter mf = _worldPanel.board.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mf.sharedMesh = _curvedMesh;
            }
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
