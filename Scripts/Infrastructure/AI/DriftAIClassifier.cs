using UnityEngine;

using System.Collections.Generic;

namespace VRWorkspace.AI
{
    public class DriftAIClassifier : MonoBehaviour
    {
        [Header("AI Model Configuration")]
        [Tooltip("Kéo file DriftPredictor.onnx từ thư mục Resources vào đây")]
        public Unity.InferenceEngine.ModelAsset onnxModelAsset;
        
        private Unity.InferenceEngine.Worker _worker;
        private Unity.InferenceEngine.Tensor<float> _inputTensor;

        // === THÔNG SỐ CHUẨN HOÁ DỮ LIỆU TỪ PYTHON ===
        // Được sinh ra từ class StandardScaler của thư viện sklearn
        // Python Code: [round(x, 6) for x in scaler.mean_]
        private readonly float[] _mean = new float[] {
            0.155933f, 2.859887f, -0.947116f, 
            -0.010336f, -0.989538f, -0.003807f, 
            0.0f, 0.0f, 0.0f
        };
        
        // Python Code: [round(x, 6) for x in scaler.scale_]
        private readonly float[] _scale = new float[] {
            8.908807f, 35.044593f, 1.983328f, 
            0.089976f, 0.037367f, 0.105832f, 
            1.0f, 1.0f, 1.0f
        };

        private bool _isInitialized = false;

        void Start()
        {
            InitializeModel();
        }

        public void InitializeModel()
        {
            if (onnxModelAsset == null)
            {
                // Thử load trực tiếp từ Resources
                onnxModelAsset = Resources.Load<Unity.InferenceEngine.ModelAsset>("DriftPredictor");
            }

            if (onnxModelAsset == null)
            {
                Debug.LogWarning("[DriftAIClassifier] Chưa tìm thấy DriftPredictor.onnx trong Resources. Vui lòng Copy file từ thư mục Python vào.");
                return;
            }

            // Biên dịch (Compile) model để chạy trên CPU/GPU của nền tảng đích
            Unity.InferenceEngine.Model runtimeModel = Unity.InferenceEngine.ModelLoader.Load(onnxModelAsset);
            
            // Khởi tạo Worker Sentis để thực thi Inference (Dùng Backend tuỳ ý, CPU hoặc GPUCompute)
            _worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.CPU);
            
            // Chuẩn bị Tensor cho Input (BatchSize = 1, Features = 9)
            _inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 9));
            
            _isInitialized = true;
            Debug.Log("[DriftAIClassifier] Sentis AI Model đã sẵn sàng!");
        }

        /// <summary>
        /// Dự đoán lượng Yaw Drift dựa trên cảm biến.
        /// </summary>
        public float PredictYawDrift(Vector3 gyro, Vector3 accel, Vector3 mag)
        {
            if (!_isInitialized || _worker == null) return 0f;

            // 1. Chuẩn bị mảng Input 9 chiều
            float[] rawInputs = new float[9] {
                gyro.x, gyro.y, gyro.z,
                accel.x, accel.y, accel.z,
                mag.x, mag.y, mag.z
            };

            // 2. Chuẩn hoá dữ liệu (Z-Score Normalization) giống hệt cách Python đã làm
            for (int i = 0; i < 9; i++)
            {
                float val = rawInputs[i];
                val = (val - _mean[i]) / _scale[i];
                _inputTensor[i] = val;
            }

            // 3. Thực thi mạng Nơ-ron (Inference)
            _worker.Schedule(_inputTensor);

            // 4. Đọc Output từ Model (Đầu ra là Tensor độ dài 1)
            var outputTensor = _worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
            
            // Tải kết quả về CPU Memory
            var cpuTensor = outputTensor.ReadbackAndClone();
            
            float predictedDrift = cpuTensor[0]; // Output is Yaw Drift (Euler Degree)
            
            // Cleanup cpu clone
            cpuTensor.Dispose();

            return predictedDrift;
        }

        void OnDestroy()
        {
            if (_worker != null)
            {
                _worker.Dispose();
            }
            if (_inputTensor != null)
            {
                _inputTensor.Dispose();
            }
        }
    }
}
