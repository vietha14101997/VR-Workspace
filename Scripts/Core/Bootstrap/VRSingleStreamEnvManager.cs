using UnityEngine;
using VRWorkspace.Media.Core;

namespace VRWorkspace.Core
{
    /// <summary>
    /// Programmatically sets up a pitch black environment with a white grid floor
    /// and forces the main camera background to black.
    /// Runs automatically after the scene loads using Unity's RuntimeInitializeOnLoadMethod.
    /// </summary>
    public class VRSingleStreamEnvManager : MonoBehaviour
    {
        private static VRSingleStreamEnvManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnSceneLoaded()
        {
            if (_instance != null) return;

            var go = new GameObject("VRSingleStreamEnvManager");
            _instance = go.AddComponent<VRSingleStreamEnvManager>();
            DontDestroyOnLoad(go);
            Debug.Log("[VRSingleStreamEnvManager] Created auto-initializing environment manager");
        }

        private void Start()
        {
            SetupEnvironment();
            SpawnGridFloor();
        }

        private void Update()
        {
            // Keep enforcing camera settings to prevent other scripts from resetting it
            EnforceCameraSettings();
        }

        private void SetupEnvironment()
        {
            // 1. Initialize and Hide Media Environment
            var env = MediaEnvironmentController.Instance;
            if (env != null)
            {
                env.Initialize();
                env.HideEnvironment();
                env.SetLightsEnabled(false, updateVisibility: true);
            }

            // 2. Clear Skybox
            RenderSettings.skybox = null;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientLight = Color.black;

            EnforceCameraSettings();
        }

        private void EnforceCameraSettings()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                if (cam.clearFlags != CameraClearFlags.SolidColor)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                }
                if (cam.backgroundColor != Color.black)
                {
                    cam.backgroundColor = Color.black;
                }
            }
        }

        private void SpawnGridFloor()
        {
            // Check if grid floor already exists
            if (GameObject.Find("VRSingleStream_GridFloor") != null) return;

            // Create Quad Floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "VRSingleStream_GridFloor";
            floor.transform.position = new Vector3(0f, -1.6f, 0f);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(100f, 100f, 1f);

            // Remove default collider so it doesn't block raycasts/teleportation
            var col = floor.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            // Generate procedural grid texture (white lines on black background)
            int texSize = 512;
            Texture2D gridTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, true);
            Color[] pixels = new Color[texSize * texSize];
            Color black = new Color(0f, 0f, 0f, 1f);
            Color white = new Color(1f, 1f, 1f, 0.4f); // Slightly transparent white lines for neon grid look

            int gridSpacing = 32; 
            int lineWidth = 2;

            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    bool isGridLine = (x % gridSpacing < lineWidth) || (y % gridSpacing < lineWidth);
                    pixels[y * texSize + x] = isGridLine ? white : black;
                }
            }

            gridTex.SetPixels(pixels);
            gridTex.filterMode = FilterMode.Bilinear;
            gridTex.wrapMode = TextureWrapMode.Repeat;
            gridTex.Apply();

            // Set unlit material
            var renderer = floor.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                
                var mat = new Material(shader);
                mat.mainTexture = gridTex;
                // Repeat tiling based on scale
                mat.mainTextureScale = new Vector2(100f, 100f);
                renderer.material = mat;
            }

            Debug.Log("[VRSingleStreamEnvManager] Spawned procedural grid floor at y = -1.6");
        }
    }
}
