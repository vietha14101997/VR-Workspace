using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using VRWorkspace.Streaming;

namespace VRWorkspace.Panel
{
    [ExecuteAlways]
    public class WorldPanelCursor : MonoBehaviour
    {
        [Header("Visual")]
        public Texture2D cursorTexture;          // auto nạp từ Resources nếu để trống
        public float sizeMeters = 0.035f;        // kích thước con trỏ (m)
        public float zOffset = 0.001f;           // nổi lên khỏi mặt board một chút

        [Header("Behavior")]
        public bool faceCamera = false;  // User request: disable camera dependency
        public bool invertNormal = true; // Standard Quad front is -Z

        [Header("State (readonly)")]
        [Range(0, 1)] public float u = 0.5f;      // UV.x trong [0..1]
        [Range(0, 1)] public float v = 0.5f;      // UV.y trong [0..1]
        public bool visible = false;

        [Header("Cursor State")]
        public CursorType currentCursorType = CursorType.Arrow;
        public long currentCursorId = 0;

        MeshRenderer _mr;
        MeshFilter _mf;
        Material _mat;
        public Material cursorMaterial;
        Transform _board;        // transform của quad Board
        float _boardW = 1f, _boardH = 1f;

        public Action<float, float> onMovedUV;    // (u,v)
        public Action onClickDown;
        public Action onClickUp;

        // ==================== Static Cursor Cache ====================

        /// <summary>
        /// Cache entry for cursor textures received from server.
        /// </summary>
        public struct CursorCacheEntry
        {
            public Texture2D texture;
            public int hotspotX;
            public int hotspotY;
            public long lastUsedTicks;
        }

        /// <summary>
        /// Static cache for all cursor textures received from server.
        /// Key is cursorId (server's hCursor handle as Int64).
        /// </summary>
        private static Dictionary<long, CursorCacheEntry> _cursorCache = new Dictionary<long, CursorCacheEntry>();

        private const int MAX_CACHE_SIZE = 100;

        /// <summary>
        /// Cache a cursor texture received from server.
        /// </summary>
        public static void CacheCursor(long cursorId, Texture2D texture, int hotspotX, int hotspotY)
        {
            // LRU eviction if cache exceeds limit
            if (_cursorCache.Count >= MAX_CACHE_SIZE && !_cursorCache.ContainsKey(cursorId))
            {
                // Find and remove oldest entry
                var oldest = _cursorCache.OrderBy(kv => kv.Value.lastUsedTicks).First();
                if (oldest.Value.texture != null)
                    Destroy(oldest.Value.texture);
                _cursorCache.Remove(oldest.Key);
            }

            _cursorCache[cursorId] = new CursorCacheEntry
            {
                texture = texture,
                hotspotX = hotspotX,
                hotspotY = hotspotY,
                lastUsedTicks = DateTime.Now.Ticks
            };
        }

        /// <summary>
        /// Check if a cursor is in the cache.
        /// </summary>
        public static bool HasCursor(long cursorId)
        {
            return _cursorCache.ContainsKey(cursorId);
        }

        /// <summary>
        /// Try to get a cursor from cache.
        /// </summary>
        public static bool TryGetCursor(long cursorId, out CursorCacheEntry entry)
        {
            if (_cursorCache.TryGetValue(cursorId, out entry))
            {
                // Update last used time
                entry.lastUsedTicks = DateTime.Now.Ticks;
                _cursorCache[cursorId] = entry;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clear all cached cursors.
        /// </summary>
        public static void ClearCache()
        {
            foreach (var entry in _cursorCache.Values)
            {
                if (entry.texture != null)
                    Destroy(entry.texture);
            }
            _cursorCache.Clear();
            Debug.Log("[WorldPanelCursor] Cursor cache cleared");
        }

        /// <summary>
        /// Set cursor by ID from cache.
        /// Returns true if cursor was found and applied.
        /// </summary>
        public bool SetCursorById(long cursorId)
        {
            currentCursorId = cursorId;

            if (TryGetCursor(cursorId, out var entry))
            {
                if (entry.texture != null && entry.texture != cursorTexture)
                {
                    cursorTexture = entry.texture;
                    ApplyCursorTexture();
                }
                return true;
            }
            // Cursor not in cache yet - wait for cursor_image message
            return false;
        }

        /// <summary>
        /// Apply current cursor texture to material.
        /// </summary>
        private void ApplyCursorTexture()
        {
            EnsureBuilt();
            if (_mr?.sharedMaterial != null && cursorTexture != null)
            {
                _mr.sharedMaterial.SetTexture("_MainTex", cursorTexture);
            }
        }

        void EnsureBuilt()
        {
            if (!_mf)
            {
                _mf = gameObject.GetComponent<MeshFilter>();
                if (!_mf) _mf = gameObject.AddComponent<MeshFilter>();
            }
            if (!_mr)
            {
                _mr = gameObject.GetComponent<MeshRenderer>();
                if (!_mr) _mr = gameObject.AddComponent<MeshRenderer>();
            }
            if (_mf.sharedMesh == null)
            {
                var m = new Mesh();
                // Shifted so top-left (Tip) matches origin (0,0)
                m.vertices = new[] { 
                    new Vector3(0f, -1f, 0),   // BL
                    new Vector3(1f, -1f, 0),   // BR
                    new Vector3(1f, 0f, 0),    // TR
                    new Vector3(0f, 0f, 0)     // TL (Tip)
                };
                m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                m.RecalculateBounds();
                _mf.sharedMesh = m;
            }
            if (_mat == null)
            {
                if (!cursorMaterial)
                    cursorMaterial = Resources.Load<Material>("WorldPanelCursor"); // Assets/Resources/WorldPanelCursor.mat
                if (cursorMaterial) _mat = new Material(cursorMaterial); // instance riêng
                else
                {
                    var sh = Shader.Find("WP/Cursor");
                    if (sh == null) sh = Shader.Find("Unlit/Transparent");
                    _mat = new Material(sh);
                }
            }
            if (_mr.sharedMaterial != _mat) _mr.sharedMaterial = _mat;

            if (cursorTexture == null)
                cursorTexture = Resources.Load<Texture2D>("icon_cursor"); // đặt file vào Assets/Resources/icon_cursor.png

            _mr.sharedMaterial.SetTexture("_MainTex", cursorTexture);
            _mr.sharedMaterial.SetColor("_Color", Color.white);

            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mr.sharedMaterial.renderQueue = 5000;

            if (_mr.sharedMaterial.HasProperty("_ZWrite"))
                _mr.sharedMaterial.SetInt("_ZWrite", 0);

            if (_mr.sharedMaterial.HasProperty("_ZTest"))
                _mr.sharedMaterial.SetInt("_ZTest", 8);
        }

        public void AttachToBoard(Transform board, float boardWidth, float boardHeight)
        {
            EnsureBuilt();
            _board = board; _boardW = Mathf.Max(1e-5f, boardWidth); _boardH = Mathf.Max(1e-5f, boardHeight);
            transform.SetParent(board, false);
            transform.localRotation = Quaternion.identity;
            UpdateScaleMeters();              // <— thay cho transform.localScale = Vector3.one * sizeMeters;
            ApplyPoseFromUV(true);
        }

        public void SetVisible(bool on)
        {
            visible = on;
            // Ensure cursor is always hidden by disabling GameObject
            // This handles cases where _mr might not be initialized yet
            if (_mr) _mr.enabled = on;
            gameObject.SetActive(on);
        }

        public void SetUV(float uu, float vv, bool silent = false)
        {
            u = Mathf.Clamp01(uu);
            v = Mathf.Clamp01(vv);
            ApplyPoseFromUV(false);
            if (!silent) onMovedUV?.Invoke(u, v);
        }

        public void NudgeUV(float du, float dv) => SetUV(u + du, v + dv);

        public void ClickDown() => onClickDown?.Invoke();
        public void ClickUp() => onClickUp?.Invoke();

        void ApplyPoseFromUV(bool forceRebuild)
        {
            if (forceRebuild) EnsureBuilt();
            if (_board == null) return;

            Vector3 worldPoint = Vector3.zero;
            Vector3 n = Vector3.forward;

            // Try to get cluster rig for curved mapping
            WorldPanelClusterRig clusterRig = null;
            WorldPanelPlus panel = null;
            int panelIndex = -1;

            // Traverse hierarchy: Cursor -> Board -> WorldPanelPlus -> WorldPanelClusterRig
            Transform panelTransform = _board.parent;
            if (panelTransform != null)
            {
                panel = panelTransform.GetComponent<WorldPanelPlus>();
                Transform clusterTransform = panelTransform.parent;
                if (clusterTransform != null)
                {
                    clusterRig = clusterTransform.GetComponent<WorldPanelClusterRig>();
                    if (clusterRig != null && panel != null)
                    {
                        // Find panel index in panels list
                        for (int i = 0; i < clusterRig.panels.Count; i++)
                        {
                            if (clusterRig.panels[i] == panel)
                            {
                                panelIndex = i;
                                break;
                            }
                        }
                    }
                }
            }

            // Determine mapping mode:
            // 1. CurvedSurround (useCurvedVisual=true): continuous curved arc, use arc-based mapping
            // 2. FlatPlanar (!useCurvedVisual but _flatPlanarVisual exists): flat panels on arc with yaw rotation
            // 3. Board-only: use original board.TransformPoint
            bool useCurvedMapping = clusterRig != null && panelIndex >= 0 && clusterRig.IsUsingUnifiedVisual;
            bool useFlatPlanarMapping = clusterRig != null && panelIndex >= 0 && clusterRig.IsUsingFlatPlanarVisual;
            bool specialMappingApplied = false;

            if (useCurvedMapping)
            {
                // === CURVED SURROUND MAPPING ===
                // Calculate position on curved surface using EXACT same formula as CurvedClusterMeshGenerator

                var enabledIndices = clusterRig.GetEnabledPanelIndices();
                int enabledCount = enabledIndices.Count;
                int enabledPanelIndex = enabledIndices.IndexOf(panelIndex);

                if (enabledPanelIndex >= 0 && enabledCount > 0)
                {
                    float arcRadius = clusterRig.ArcRadius;
                    float panelWidth = panel.width;
                    float panelHeight = panel.height;

                    // Use EXACT same parameters as CurvedClusterMeshGenerator
                    float boardWidth = panelWidth;
                    float gapMeters = clusterRig.edgeGapMeters;
                    float overlapMeters = clusterRig.panelOverlap;

                    float boardAngleRad = 2f * Mathf.Atan(boardWidth / 2f / arcRadius);
                    float gapAngleRad = 2f * Mathf.Atan(gapMeters / 2f / arcRadius);
                    float overlapAngleRad = 2f * Mathf.Atan(overlapMeters / 2f / arcRadius);
                    float anglePerPanelRad = boardAngleRad + gapAngleRad - overlapAngleRad;
                    float totalArcAngleRad = enabledCount * anglePerPanelRad;

                    float globalU = (enabledPanelIndex + u) / enabledCount;
                    float angle = (globalU - 0.5f) * totalArcAngleRad;

                    float xPos = Mathf.Sin(angle) * arcRadius;
                    float zPos = Mathf.Cos(angle) * arcRadius - arcRadius;
                    float yPos = (v - 0.5f) * panelHeight;

                    Vector3 contentLocalPos = clusterRig.GetUnifiedContentLocalPosition();
                    Vector3 localPos = new Vector3(xPos + contentLocalPos.x, yPos, zPos + contentLocalPos.z);
                    worldPoint = clusterRig.transform.TransformPoint(localPos);

                    Vector3 localNormal = new Vector3(-Mathf.Sin(angle), 0f, -Mathf.Cos(angle));
                    n = clusterRig.transform.TransformDirection(localNormal);

                    specialMappingApplied = true;
                }
            }
            else if (useFlatPlanarMapping)
            {
                // === FLAT PLANAR MAPPING ===
                // Each panel is a FLAT quad positioned on arc with yaw rotation.
                // Boards are positioned linearly (z=0, no rotation), but mesh panels are on arc.
                // Use FlatPlanarMeshGenerator formulas for cursor positioning.

                var enabledIndices = clusterRig.GetEnabledPanelIndices();
                int enabledCount = enabledIndices.Count;
                int enabledPanelIndex = enabledIndices.IndexOf(panelIndex);

                if (enabledPanelIndex >= 0 && enabledCount > 0)
                {
                    // Get panel transform matching FlatPlanarMeshGenerator
                    clusterRig.GetFlatPlanarPanelTransform(enabledPanelIndex, enabledCount,
                        out Vector3 panelCenter, out Vector3 panelRight, out Vector3 panelNormal);

                    // Get board dimensions
                    clusterRig.GetFlatPlanarBoardDimensions(out float boardWidth, out float boardHeight);

                    // Calculate local position on panel surface
                    // u,v are 0-1 within this panel
                    float localX = (u - 0.5f) * boardWidth;  // -halfWidth to +halfWidth
                    float localY = (v - 0.5f) * boardHeight; // -halfHeight to +halfHeight

                    // Position in ClusterRig local space
                    // panelCenter + right * localX + up * localY
                    Vector3 localPos = panelCenter + panelRight * localX + Vector3.up * localY;

                    // Add content mesh offset (to match where mesh is rendered)
                    Vector3 contentLocalPos = clusterRig.GetUnifiedContentLocalPosition();
                    localPos += contentLocalPos;

                    worldPoint = clusterRig.transform.TransformPoint(localPos);
                    n = clusterRig.transform.TransformDirection(panelNormal);

                    specialMappingApplied = true;
                }
            }

            if (!specialMappingApplied)
            {
                // === BOARD MAPPING (original behavior) ===
                float lx = u - 0.5f;
                float ly = v - 0.5f;
                worldPoint = _board.TransformPoint(new Vector3(lx, ly, 0f));
                n = _board.forward;
                if (invertNormal) n = -n;
            }

            // Ensure normal points toward camera (if enabled)
            if (faceCamera)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - worldPoint;
                    if (Vector3.Dot(n, toCam) < 0f) n = -n;
                }
            }

            // Reparent cursor for special mapping modes
            if (specialMappingApplied && clusterRig != null && transform.parent != clusterRig.transform)
            {
                transform.SetParent(clusterRig.transform, true);
                UpdateScaleMeters();
            }
            else if (!specialMappingApplied && _board != null && transform.parent != _board)
            {
                transform.SetParent(_board, true);
                UpdateScaleMeters();
            }

            // Set position and rotation
            transform.position = worldPoint + n * Mathf.Max(1e-4f, zOffset);
            transform.rotation = (specialMappingApplied && clusterRig != null)
                ? Quaternion.LookRotation(-n, clusterRig.transform.up)
                : _board.rotation;
        }

        void UpdateScaleMeters()
        {
            // Get parent's lossy scale to calculate local scale for target world size
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;

            // Calculate local scale to achieve sizeMeters world size
            float sx = sizeMeters / Mathf.Max(1e-6f, parentScale.x);
            float sy = sizeMeters / Mathf.Max(1e-6f, parentScale.y);
            transform.localScale = new Vector3(sx, sy, 1f);
        }
    }
}
