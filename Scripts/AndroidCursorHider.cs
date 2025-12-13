using UnityEngine;
using System;

public class AndroidCursorHider : MonoBehaviour
{
    AndroidJavaObject _prevIcon = null;

    void OnEnable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
            {
                // Lưu icon hiện tại để có thể khôi phục
                _prevIcon = decor.Call<AndroidJavaObject>("getPointerIcon");
                
                // Reference _prevIcon to avoid warning (used in OnDisable)
                if (_prevIcon == null) 
                    Debug.Log("[AndroidCursorHider] No previous icon detected");

                // Tạo bitmap trong suốt 1x1
                using (var bitmapCls = new AndroidJavaClass("android.graphics.Bitmap"))
                using (var configCls = new AndroidJavaClass("android.graphics.Bitmap$Config"))
                {
                    var cfg = configCls.GetStatic<AndroidJavaObject>("ARGB_8888");
                    var bmp = bitmapCls.CallStatic<AndroidJavaObject>("createBitmap", 1, 1, cfg);

                    // PointerIcon.create(Bitmap, hotSpotX, hotSpotY)
                    using (var pointerIconCls = new AndroidJavaClass("android.view.PointerIcon"))
                    {
                        var transparentIcon = pointerIconCls.CallStatic<AndroidJavaObject>("create", bmp, 0f, 0f);
                        decor.Call<AndroidJavaObject>("setPointerIcon", transparentIcon);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AndroidCursorHider] Không ẩn được con trỏ hệ thống: " + e.Message);
        }
#endif
        Cursor.visible = false;                         // Ẩn cursor cấp Unity
        Cursor.lockState = CursorLockMode.Confined;     // Giữ tọa độ trong màn hình
    }

    void OnDisable()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
            using (var pointerIconCls = new AndroidJavaClass("android.view.PointerIcon"))
            {
                // Khôi phục icon mũi tên mặc định nếu không có cái cũ
                var defaultIcon = pointerIconCls.CallStatic<AndroidJavaObject>("getSystemIcon", activity, 1000 /*TYPE_ARROW*/);
                decor.Call<AndroidJavaObject>("setPointerIcon", _prevIcon != null ? _prevIcon : defaultIcon);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AndroidCursorHider] Không khôi phục được con trỏ hệ thống: " + e.Message);
        }
#endif
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}
