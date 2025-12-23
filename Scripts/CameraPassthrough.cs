using UnityEngine;
using UnityEngine.Android;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer))]
public class CameraPassthrough : MonoBehaviour
{
    public Camera cam;           // kéo Main Camera vào
    public float distance = -1f; // -1 = tự đặt gần far clip
    public int requestedFPS = 60;

    WebCamTexture _tex;
    MeshRenderer _mr;
    Material _mat; // Cached material instance
    Color _baseColor = Color.white;
    float _currentAlpha = 1f;

    void Awake()
    {
        _mr = GetComponent<MeshRenderer>();
        if (!cam) cam = GetComponentInParent<Camera>();

        // Pre-initialize material for crossfade support
        InitializeMaterial();
    }

    void InitializeMaterial()
    {
        if (_mat != null) return;
        if (_mr == null) return;

        // Always use Standard shader for proper alpha support during crossfade
        Shader standardShader = Shader.Find("Standard");

        if (standardShader != null)
        {
            _mat = new Material(standardShader);
        }
        else
        {
            // Fallback - less ideal but might work
            Debug.LogWarning("[CameraPassthrough] Standard shader not found, using fallback");
            _mat = new Material(Shader.Find("Unlit/Texture"));
        }

        // Configure for transparency
        _mat.color = Color.white;
        _mat.SetFloat("_Mode", 3); // Transparent
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_ZWrite", 0);
        _mat.DisableKeyword("_ALPHATEST_ON");
        _mat.EnableKeyword("_ALPHABLEND_ON");
        _mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _mat.renderQueue = 3000;

        // Disable lighting effects for passthrough (looks more like raw camera feed)
        if (_mat.HasProperty("_Glossiness")) _mat.SetFloat("_Glossiness", 0f);
        if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0f);
        if (_mat.HasProperty("_SpecularHighlights"))
        {
            _mat.SetFloat("_SpecularHighlights", 0f);
            _mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }
        if (_mat.HasProperty("_GlossyReflections"))
        {
            _mat.SetFloat("_GlossyReflections", 0f);
            _mat.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
        }

        _mr.material = _mat;
        _baseColor = Color.white;

        Debug.Log($"[CameraPassthrough] Material initialized with shader: {_mat.shader.name}");
    }

    void OnEnable()
    {
        // Pooling: reuse existing texture if available
        if (_tex != null)
        {
            Debug.Log("Resuming camera passthrough (pooled)");
            _tex.Play();
            if (_mr != null) _mr.enabled = true;
            StartCoroutine(EnsureTextureStarted());
        }
        else
        {
            StartCam();
        }
    }

    void OnDisable()
    {
        // Pooling: only stop, don't destroy
        if (_tex != null && _tex.isPlaying)
        {
            Debug.Log("Pausing camera passthrough (pooled)");
            _tex.Stop();
        }
        if (_mr != null) _mr.enabled = false;
    }

    void OnDestroy()
    {
        // Cleanup only when object is destroyed
        if (_tex != null)
        {
            Debug.Log("Destroying webcam texture (cleanup)");
            if (_tex.isPlaying) _tex.Stop();
            Destroy(_tex);
            _tex = null;
        }
        if (_mat != null)
        {
            Destroy(_mat);
            _mat = null;
        }
    }

    void LateUpdate()
    {
        if (!cam) return;
        float d = (distance > 0f) ? distance : (cam.farClipPlane - 1f);
        float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;

        // aspect "cover" để không méo + xử lý xoay/mirror
        float texAspect = 1f;
        if (_tex != null && _tex.width > 16 && _tex.height > 16)
        {
            bool rot90 = (_tex.videoRotationAngle % 180) != 0;
            texAspect = rot90 ? (float)_tex.height / _tex.width : (float)_tex.width / _tex.height;
        }
        float frustumAspect = w / h;
        float targetW, targetH;
        if (texAspect > frustumAspect) { targetH = h; targetW = targetH * texAspect; }
        else { targetW = w; targetH = targetW / texAspect; }

        float xFlip = (_tex != null && _tex.videoVerticallyMirrored) ? -1f : 1f;
        float zRot = (_tex != null) ? -_tex.videoRotationAngle : 0f;

        transform.localPosition = new Vector3(0, 0, d);
        transform.localRotation = Quaternion.Euler(0, 0, zRot);
        transform.localScale = new Vector3(targetW * xFlip, targetH, 1f);
    }

    /// <summary>
    /// Set the alpha of the passthrough quad for crossfade transitions.
    /// </summary>
    /// <param name="alpha">Alpha value from 0 (transparent) to 1 (opaque)</param>
    public void SetAlpha(float alpha)
    {
        _currentAlpha = Mathf.Clamp01(alpha);

        // Ensure material is initialized
        if (_mat == null)
        {
            InitializeMaterial();
        }

        if (_mat != null)
        {
            // For Unlit/Transparent shader, we need to set the color with alpha
            Color c = new Color(1f, 1f, 1f, _currentAlpha);

            // Try setting color through different properties
            if (_mat.HasProperty("_Color"))
            {
                _mat.SetColor("_Color", c);
            }
            if (_mat.HasProperty("_TintColor"))
            {
                _mat.SetColor("_TintColor", c);
            }

            // Also set the main color
            _mat.color = c;

            // Configure transparency mode
            if (_currentAlpha < 1f)
            {
                SetMaterialTransparent(_mat, true);
            }
            else
            {
                SetMaterialTransparent(_mat, false);
            }
        }
    }

    /// <summary>
    /// Get the current alpha value.
    /// </summary>
    public float GetAlpha() => _currentAlpha;

    void SetMaterialTransparent(Material mat, bool transparent)
    {
        if (transparent)
        {
            mat.SetFloat("_Mode", 3); // Transparent mode for Standard shader
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
        else
        {
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 1000;
        }
    }

    void StartCam()
    {
        Debug.Log("Starting camera passthrough");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Requesting camera permission");
            Permission.RequestUserPermission(Permission.Camera);
        }
#endif
        var devs = WebCamTexture.devices;
        if (devs.Length == 0)
        {
            Debug.LogWarning("No camera devices found");
            return;
        }

        Debug.Log("Found " + devs.Length + " camera devices");
        for (int i = 0; i < devs.Length; i++)
        {
            Debug.Log("Camera " + i + ": " + devs[i].name + " (front facing: " + devs[i].isFrontFacing + ")");
        }

        // ưu tiên camera sau
        int idx = 0;
        for (int i = 0; i < devs.Length; i++)
        {
            if (!devs[i].isFrontFacing)
            {
                idx = i;
                Debug.Log("Selected back camera: " + devs[i].name);
                break;
            }
        }

        // Ensure we have a valid material renderer
        if (_mr == null)
        {
            _mr = GetComponent<MeshRenderer>();
            if (_mr == null)
            {
                Debug.LogError("No MeshRenderer found on CameraPassthrough object");
                return;
            }
        }

        // Ensure material is initialized
        InitializeMaterial();

        // Create and start the webcam texture with higher resolution
        _tex = new WebCamTexture(devs[idx].name, 1280, 720, requestedFPS);

        // Set the texture
        _mat.mainTexture = _tex;

        // Ensure the material is not culled and is visible from both sides
        _mat.SetInt("_Cull", 0); // 0 = Off (double-sided)

        Debug.Log("Starting webcam texture: " + devs[idx].name + " at 1280x720");
        _tex.Play();
        _mr.enabled = true;

        // Wait a few frames to ensure the texture is initialized
        StartCoroutine(EnsureTextureStarted());
    }

    IEnumerator EnsureTextureStarted()
    {
        // Wait for a few frames to ensure the texture is properly initialized
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForEndOfFrame();
        }

        // Check if texture is valid and playing
        if (_tex != null)
        {
            if (!_tex.isPlaying)
            {
                Debug.Log("Restarting webcam texture as it was not playing");
                _tex.Play();
            }

            // Wait for texture to be ready
            int attempts = 0;
            while (!_tex.didUpdateThisFrame && attempts < 30)
            {
                yield return new WaitForEndOfFrame();
                attempts++;
            }

            if (attempts >= 30)
            {
                Debug.LogWarning("Webcam texture did not initialize properly after 30 frames");
            }
            else
            {
                Debug.Log("Webcam texture initialized successfully after " + attempts + " frames");

                // Force material update
                if (_mat != null)
                {
                    // Ensure the texture is properly set
                    _mat.mainTexture = _tex;

                    // Force material to update
                    _mat.SetFloat("_UpdateFlag", Time.time);
                }
            }
        }
    }
}
