using System.IO;
using System.Text;
using UnityEngine;

namespace VRWorkspace.AI
{
    /// <summary>
    /// Ghi lại bộ dữ liệu (Dataset) để huấn luyện mô hình dự đoán Drift.
    /// Format: Time, GyroX, GyroY, GyroZ, AccelX, AccelY, AccelZ, IsStationary, LabelDriftYaw
    /// </summary>
    public class DriftDataCollector : MonoBehaviour
    {
        [Header("Data Collection")]
        public bool isRecording = false;
        public string fileName = "DriftDataset.csv";
        
        // Thư mục lưu trên thiết bị
        private string _filePath;
        private StreamWriter _writer;
        private StringBuilder _sb = new StringBuilder();

        // Ensure Permission
        private bool _permissionRequested = false;

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android 10+ (API 29+) Scoped Storage /Android/data is usually hidden.
            // Trỏ trực tiếp vào thư mục Downloads của Public Storage bằng Native Java Code
            try 
            {
                using (var environment = new AndroidJavaClass("android.os.Environment"))
                {
                    string dirDownloads = environment.GetStatic<string>("DIRECTORY_DOWNLOADS");
                    using (var downloadsDir = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory", dirDownloads))
                    {
                        var downloadsPath = downloadsDir.Call<string>("getAbsolutePath");
                        _filePath = Path.Combine(downloadsPath, fileName);
                    }
                }
            } 
            catch (System.Exception ex)
            {
                Debug.LogError("[DriftDataCollector] Fallback to standard path. Error: " + ex);
                _filePath = Path.Combine(Application.persistentDataPath, fileName);
            }
#else
            // PC hoặc Editor
            _filePath = Path.Combine(Application.dataPath, "..", fileName);
#endif
            Debug.Log($"[DriftDataCollector] Will save dataset to: {_filePath}");
        }

        private void RequestStoragePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_permissionRequested) return;

            try
            {
                using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdkInt = versionClass.GetStatic<int>("SDK_INT");
                    
                    if (sdkInt >= 30) // Android 11+
                    {
                        using (var environment = new AndroidJavaClass("android.os.Environment"))
                        {
                            bool isManager = environment.CallStatic<bool>("isExternalStorageManager");
                            if (!isManager)
                            {
                                // Phải mở màn hình cài đặt để xin quyền All Files Access
                                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                                {
                                    var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                                    var intent = new AndroidJavaObject("android.content.Intent", "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION");
                                    
                                    var uriClass = new AndroidJavaClass("android.net.Uri");
                                    var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier);
                                    intent.Call<AndroidJavaObject>("setData", uri);
                                    
                                    currentActivity.Call("startActivity", intent);
                                    Debug.Log("[DriftDataCollector] Xin quyền MANAGE_EXTERNAL_STORAGE");
                                }
                            }
                        }
                    }
                    else // Android 10 trở xuống
                    {
                        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.ExternalStorageWrite))
                        {
                            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.ExternalStorageWrite);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[DriftDataCollector] Lỗi khi xin quyền: " + ex.Message);
            }
            _permissionRequested = true;
#endif
        }

        public void StartRecording()
        {
            if (isRecording) return;
            
            RequestStoragePermission();
            
            isRecording = true;

            try
            {
                bool fileExists = File.Exists(_filePath);
                _writer = new StreamWriter(_filePath, true, Encoding.UTF8);
                _writer.AutoFlush = true; // Rất quan trọng: Bắt buộc ghi liền xuống thẻ nhớ

                // Write Header nếu file mới
                if (!fileExists)
                {
                    _writer.WriteLine("Timestamp,GyroX,GyroY,GyroZ,AccelX,AccelY,AccelZ,MagX,MagY,MagZ,IsStationary,TargetYawDrift");
                }
                
                Debug.Log($"[DriftDataCollector] Started recording dataset. File: {_filePath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DriftDataCollector] Error opening file at {_filePath}: {ex.Message}");
                // Fallback cứu hộ: Lưu tạm ra thư mục Temp
                _filePath = Path.Combine(Application.temporaryCachePath, fileName);
                Debug.LogError($"[DriftDataCollector] FALLBACK: Trying temporary path instead: {_filePath}");
                try
                {
                     _writer = new StreamWriter(_filePath, true, Encoding.UTF8);
                     _writer.AutoFlush = true;
                     if (!File.Exists(_filePath))
                        _writer.WriteLine("Timestamp,GyroX,GyroY,GyroZ,AccelX,AccelY,AccelZ,MagX,MagY,MagZ,IsStationary,TargetYawDrift");
                }
                catch { } // Give up
                
                isRecording = false;
            }
        }

        public void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;

            if (_writer != null)
            {
                _writer.Flush();
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }
            
            Debug.Log($"[DriftDataCollector] Stopped recording. Saved to {_filePath}");
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        /// <summary>
        /// Gọi hàm này từ VRGazeReticle vào mỗi frame khi đang thu thập dữ liệu (Lúc La bàn đạt chất lượng tốt).
        /// </summary>
        public void RecordFrame(Vector3 gyro, Vector3 accel, Vector3 mag, bool isStationary, float targetYawDrift)
        {
            if (!isRecording || _writer == null) return;

            _sb.Clear();
            _sb.Append(Time.time).Append(",");
            
            // Raw Inputs (Gyro, Accel, Mag)
            _sb.Append(gyro.x).Append(",").Append(gyro.y).Append(",").Append(gyro.z).Append(",");
            _sb.Append(accel.x).Append(",").Append(accel.y).Append(",").Append(accel.z).Append(",");
            _sb.Append(mag.x).Append(",").Append(mag.y).Append(",").Append(mag.z).Append(",");
            
            // State (0 or 1)
            _sb.Append(isStationary ? 1 : 0).Append(",");
            
            // Lable: The ground-truth drift calculated by Madgwick Teacher
            _sb.Append(targetYawDrift);

            _writer.WriteLine(_sb.ToString());
        }
    }
}
