using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Camera))]
public class WorldTransitionManager : MonoBehaviour
{
    public Camera realWorldCamera;
    public Camera virtualWorldCamera;
    public float transitionDuration = 1f;
    public float edgeSmoothness = 0.1f;

    private Material transitionMaterial;
    private RenderTexture realWorldRT;
    private RenderTexture virtualWorldRT;
    private bool isTransitioning = false;
    private ViewMode currentMode = ViewMode.VirtualSpace;

    void Start()
    {
        // Create render textures with higher resolution and proper format
        realWorldRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        realWorldRT.antiAliasing = 2; // Add anti-aliasing for smoother edges
        realWorldRT.Create();
        
        virtualWorldRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        virtualWorldRT.antiAliasing = 2;
        virtualWorldRT.Create();

        Debug.Log("Created render textures: " + realWorldRT.width + "x" + realWorldRT.height);

        // Create material
        var shader = Shader.Find("Custom/WorldBlendTransition");
        if (shader != null)
        {
            transitionMaterial = new Material(shader);
            transitionMaterial.SetFloat("_FadeEdgeSmooth", edgeSmoothness);
            transitionMaterial.SetFloat("_BlendFactor", 0f); // Start in virtual world
            Debug.Log("Created transition material with shader: " + shader.name);
        }
        else
        {
            Debug.LogError("Could not find Custom/WorldBlendTransition shader");
            return; // Prevent further execution if shader is missing
        }

        // Validate cameras
        if (realWorldCamera == null || virtualWorldCamera == null)
        {
            Debug.LogError("One or both cameras are missing in WorldTransitionManager");
            return;
        }

        // Configure the real world camera with CameraPassthrough
        var passthrough = realWorldCamera.GetComponent<CameraPassthrough>();
        if (passthrough == null)
        {
            Debug.Log("Adding CameraPassthrough component to real world camera");
            passthrough = realWorldCamera.gameObject.AddComponent<CameraPassthrough>();
            passthrough.cam = realWorldCamera; // Explicitly set the camera reference
        }
        else
        {
            Debug.Log("Found existing CameraPassthrough component on real world camera");
            // Ensure the camera reference is set correctly
            passthrough.cam = realWorldCamera;
        }

        // Initial setup - don't set target textures yet, we'll do this in DelayedInitialization
        // to ensure cameras are properly initialized first
        
        // Ensure both cameras are enabled at start for proper initialization
        realWorldCamera.enabled = true;
        virtualWorldCamera.enabled = true;

        // Set initial state after a short delay to ensure cameras are initialized
        StartCoroutine(DelayedInitialization());
    }

    private IEnumerator DelayedInitialization()
    {
        // Wait for a few frames to ensure cameras are properly initialized
        for (int i = 0; i < 3; i++)
        {
            yield return new WaitForEndOfFrame();
        }
        
        Debug.Log("Performing delayed initialization");
        
        // Ensure render textures are created and valid
        if (realWorldRT == null || !realWorldRT.IsCreated())
        {
            Debug.LogWarning("Real world render texture not created, recreating");
            if (realWorldRT != null) Destroy(realWorldRT);
            realWorldRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            realWorldRT.antiAliasing = 2;
            realWorldRT.Create();
        }
        
        if (virtualWorldRT == null || !virtualWorldRT.IsCreated())
        {
            Debug.LogWarning("Virtual world render texture not created, recreating");
            if (virtualWorldRT != null) Destroy(virtualWorldRT);
            virtualWorldRT = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            virtualWorldRT.antiAliasing = 2;
            virtualWorldRT.Create();
        }
        
        // Now set the target textures
        if (realWorldCamera != null)
        {
            realWorldCamera.targetTexture = realWorldRT;
            Debug.Log("Set real world camera target texture");
            
            // Ensure CameraPassthrough is properly configured
            var passthrough = realWorldCamera.GetComponent<CameraPassthrough>();
            if (passthrough != null)
            {
                // Make sure it's enabled for initial setup
                passthrough.enabled = true;
                
                // Force a render to ensure the passthrough texture is updated
                realWorldCamera.Render();
                Debug.Log("Forced initial render of real world camera");
            }
        }
        
        if (virtualWorldCamera != null)
        {
            virtualWorldCamera.targetTexture = virtualWorldRT;
            Debug.Log("Set virtual world camera target texture");
            
            // Force a render to ensure the virtual world texture is updated
            virtualWorldCamera.Render();
            Debug.Log("Forced initial render of virtual world camera");
        }
        
        // Wait another frame to ensure renders are complete
        yield return new WaitForEndOfFrame();
        
        // Set initial state
        SetWorldMode(currentMode, true);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (transitionMaterial != null && realWorldRT != null && virtualWorldRT != null)
        {
            // Force both cameras to render to their render textures
            if (realWorldCamera && realWorldRT.IsCreated())
            {
                // Make sure the real world camera is enabled during rendering
                bool wasEnabled = realWorldCamera.enabled;
                realWorldCamera.enabled = true;
                
                // Ensure the camera is targeting the correct render texture
                realWorldCamera.targetTexture = realWorldRT;
                
                // Check if CameraPassthrough is needed and enabled
                if (currentMode == ViewMode.RealWorld || isTransitioning)
                {
                    var passthrough = realWorldCamera.GetComponent<CameraPassthrough>();
                    if (passthrough != null && !passthrough.enabled)
                    {
                        passthrough.enabled = true;
                    }
                }
                
                realWorldCamera.Render();
                realWorldCamera.enabled = wasEnabled;
            }
            
            if (virtualWorldCamera && virtualWorldRT.IsCreated())
            {
                // Make sure the virtual world camera is enabled during rendering
                bool wasEnabled = virtualWorldCamera.enabled;
                virtualWorldCamera.enabled = true;
                
                // Ensure the camera is targeting the correct render texture
                virtualWorldCamera.targetTexture = virtualWorldRT;
                
                virtualWorldCamera.Render();
                virtualWorldCamera.enabled = wasEnabled;
            }
            
            // Set textures and blend
            transitionMaterial.SetTexture("_RealWorldTex", realWorldRT);
            transitionMaterial.SetTexture("_VirtualWorldTex", virtualWorldRT);
            
            // Only log state changes, not every frame
            float blendFactor = transitionMaterial.GetFloat("_BlendFactor");
            if (blendFactor > 0.99f && currentMode != ViewMode.RealWorld) // Almost fully in real world
            {
                Debug.Log("Rendering in Real World mode: " + blendFactor);
            }
            else if (blendFactor < 0.01f && currentMode != ViewMode.VirtualSpace) // Almost fully in virtual world
            {
                Debug.Log("Rendering in Virtual World mode: " + blendFactor);
            }
            
            Graphics.Blit(null, dest, transitionMaterial);
        }
        else
        {
            // Fallback if materials or textures aren't ready
            Debug.LogWarning("Using fallback rendering path in WorldTransitionManager");
            Graphics.Blit(src, dest);
        }
    }

    public void SetWorldMode(ViewMode newMode, bool immediate = false)
    {
        // Validate that we have necessary components
        if (transitionMaterial == null)
        {
            Debug.LogError("Cannot set world mode: transition material is null");
            return;
        }

        if (realWorldCamera == null || virtualWorldCamera == null)
        {
            Debug.LogError("Cannot set world mode: one or both cameras are missing");
            return;
        }

        // Stop any ongoing transitions
        if (isTransitioning && !immediate)
        {
            StopAllCoroutines();
            isTransitioning = false;
        }

        // Start new transition
        StartCoroutine(TransitionToMode(newMode, immediate));
    }

    private IEnumerator TransitionToMode(ViewMode newMode, bool immediate)
    {
        // Safety check
        if (transitionMaterial == null)
        {
            Debug.LogError("Cannot transition: transition material is null");
            yield break;
        }

        Debug.Log("Starting transition to mode: " + newMode);
        isTransitioning = true;
        float startTime = Time.time;
        float initialBlend = transitionMaterial.GetFloat("_BlendFactor");
        float targetBlend = (newMode == ViewMode.RealWorld) ? 1f : 0f;

        Debug.Log("Transition from blend " + initialBlend + " to " + targetBlend);

        // Ensure both cameras are enabled and properly configured before transition
        if (realWorldCamera != null) 
        {
            // Ensure the real world camera is enabled and rendering to its texture
            realWorldCamera.enabled = true;
            realWorldCamera.targetTexture = realWorldRT;
            Debug.Log("Real world camera enabled and configured for transition");
            
            // For real world mode, ensure the CameraPassthrough component is active
            if (newMode == ViewMode.RealWorld)
            {
                var passthrough = realWorldCamera.GetComponent<CameraPassthrough>();
                if (passthrough != null)
                {
                    // Ensure passthrough is enabled before transition starts
                    passthrough.enabled = true;
                    
                    // Force a render to ensure the passthrough texture is updated
                    realWorldCamera.Render();
                    Debug.Log("CameraPassthrough component enabled and camera rendered");
                }
                else
                {
                    Debug.LogWarning("CameraPassthrough component not found on real world camera");
                }
            }
        }
        
        if (virtualWorldCamera != null) 
        {
            // Ensure the virtual world camera is enabled and rendering to its texture
            virtualWorldCamera.enabled = true;
            virtualWorldCamera.targetTexture = virtualWorldRT;
            Debug.Log("Virtual world camera enabled and configured for transition");
            
            // Force a render to ensure the virtual world texture is updated
            virtualWorldCamera.Render();
        }

        // Wait a frame to ensure both cameras have rendered at least once
        yield return null;

        if (!immediate)
        {
            // Use a smoother easing function for the transition
            while (Time.time - startTime < transitionDuration)
            {
                float t = (Time.time - startTime) / transitionDuration;
                
                // Apply smoothstep for more natural easing
                t = t * t * (3f - 2f * t); // Smoothstep formula
                
                float blend = Mathf.Lerp(initialBlend, targetBlend, t);
                transitionMaterial.SetFloat("_BlendFactor", blend);
                
                // Update edge smoothness based on transition progress
                float dynamicEdgeSmooth = Mathf.Lerp(edgeSmoothness * 0.5f, edgeSmoothness, t);
                transitionMaterial.SetFloat("_FadeEdgeSmooth", dynamicEdgeSmooth);
                
                yield return null;
            }
        }

        // Ensure we set the final blend value
        transitionMaterial.SetFloat("_BlendFactor", targetBlend);
        Debug.Log("Final blend factor set to: " + targetBlend);

        // After transition, we keep both cameras enabled but set their target textures correctly
        // This ensures that OnRenderImage can still render both cameras
        if (realWorldCamera != null) realWorldCamera.enabled = true;
        if (virtualWorldCamera != null) virtualWorldCamera.enabled = true;

        currentMode = newMode;
        isTransitioning = false;
        Debug.Log("Transition completed to mode: " + newMode);
    }

    void OnDestroy()
    {
        // Clean up render textures
        if (realWorldRT != null)
        {
            realWorldRT.Release();
            Destroy(realWorldRT);
            realWorldRT = null;
        }
        
        if (virtualWorldRT != null)
        {
            virtualWorldRT.Release();
            Destroy(virtualWorldRT);
            virtualWorldRT = null;
        }
        
        // Clean up material
        if (transitionMaterial != null)
        {
            Destroy(transitionMaterial);
            transitionMaterial = null;
        }
        
        // Reset camera target textures if they still exist
        if (realWorldCamera != null)
        {
            realWorldCamera.targetTexture = null;
        }
        
        if (virtualWorldCamera != null)
        {
            virtualWorldCamera.targetTexture = null;
        }
    }
}
