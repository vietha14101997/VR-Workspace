using UnityEngine;
using UnityEngine.Android;
using System.Collections;
using System.Collections.Generic;

namespace VRWorkspace.CameraUtils
{
    [RequireComponent(typeof(MeshRenderer))]
    public class CameraPassthrough : MonoBehaviour
    {
        public Camera cam;           // kéo Main Camera vào
        public float distance = 10f; // -1 = tự đặt gần far clip, 10f để tránh lỗi nhìn xa quá
        public float physicalCameraFOV = 60f; // FOV thật của cụm camera lồi
        public int requestedFPS = 30;

        WebCamTexture _tex;
        MeshRenderer _mr;

        void Awake() { _mr = GetComponent<MeshRenderer>(); if (!cam) cam = GetComponentInParent<Camera>(); }
        void OnEnable() { StartCam(); }
        void OnDisable() { StopCam(); }

        void LateUpdate()
        {
            if (!cam) return;
            float d = (distance > 0f) ? distance : (cam.farClipPlane - 1f);

            // aspect “cover” để không méo + xử lý xoay/mirror
            float texAspect = 1f;
            if (_tex != null && _tex.width > 16 && _tex.height > 16)
            {
                bool rot90 = (_tex.videoRotationAngle % 180) != 0;
                texAspect = rot90 ? (float)_tex.height / _tex.width : (float)_tex.width / _tex.height;
            }
            
            // Tính toán khung hình thật (1:1) thay vì căng theo FOV ảo của camera VR
            float targetH = 2f * d * Mathf.Tan(physicalCameraFOV * 0.5f * Mathf.Deg2Rad);
            float targetW = targetH * texAspect;

            float xFlip = (_tex != null && _tex.videoVerticallyMirrored) ? -1f : 1f;
            float zRot = (_tex != null) ? -_tex.videoRotationAngle : 0f;

            transform.localPosition = new Vector3(0, 0, d);
            transform.localRotation = Quaternion.Euler(0, 0, zRot);
            transform.localScale = new Vector3(targetW * xFlip, targetH, 1f);
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

            // Ensure we have a valid material
            if (_mr.material == null)
            {
                Debug.LogError("No material found on MeshRenderer");
                // Create a default material if none exists
                _mr.material = new Material(Shader.Find("Unlit/Texture"));
            }

            // Create and start the webcam texture with higher resolution
            _tex = new WebCamTexture(devs[idx].name, 1280, 720, requestedFPS);

            // Set the texture and ensure it's properly configured
            _mr.material.mainTexture = _tex;
            _mr.material.renderQueue = 1000; // Ensure it renders before other objects but after background

            if (_mr.material.HasProperty("_Glossiness"))
                _mr.material.SetFloat("_Glossiness", 0f);
            if (_mr.material.HasProperty("_Metallic"))
                _mr.material.SetFloat("_Metallic", 0f);

            // Ensure the material is not culled and is visible from both sides
            if (_mr.material.HasProperty("_Cull"))
                _mr.material.SetInt("_Cull", 0); // 0 = Off (double-sided)

            Debug.Log("Starting webcam texture: " + devs[idx].name + " at 1280x720");
            _tex.Play();

            // Wait a few frames to ensure the texture is initialized
            StartCoroutine(EnsureTextureStarted());
        }

        void StopCam()
        {
            Debug.Log("Stopping camera passthrough");
            if (_tex != null) 
            { 
                if (_tex.isPlaying) 
                {
                    Debug.Log("Stopping webcam texture");
                    _tex.Stop(); 
                }

                Debug.Log("Destroying webcam texture");
                Destroy(_tex); 
                _tex = null; 

                // Clear the texture reference in the material
                if (_mr != null && _mr.material != null)
                {
                    _mr.material.mainTexture = null;
                }
            }
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
                while (_tex != null && !_tex.didUpdateThisFrame && attempts < 30)
                {
                    yield return new WaitForEndOfFrame();
                    attempts++;

                    // Check if texture was destroyed during wait
                    if (_tex == null)
                    {
                        Debug.LogWarning("Webcam texture was destroyed during initialization");
                        yield break;
                    }
                }

                if (attempts >= 30)
                {
                    Debug.LogWarning("Webcam texture did not initialize properly. Retrying with default resolution/fps...");
                    string deviceName = _tex.deviceName;
                    StopCam();

                    _tex = new WebCamTexture(deviceName);
                    if (_mr != null && _mr.material != null) _mr.material.mainTexture = _tex;
                    _tex.Play();
                }
                else
                {
                    Debug.Log("Webcam texture initialized successfully after " + attempts + " frames");

                    // Force material update
                    if (_mr != null && _mr.material != null)
                    {
                        // Ensure the texture is properly set
                        _mr.material.mainTexture = _tex;

                        // Force material to update
                        _mr.material.SetFloat("_UpdateFlag", Time.time);
                    }
                }
            }
        }
    }

}
