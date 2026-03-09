import os
import pandas as pd
import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
import onnx

# 1. Định nghĩa DataLoader
class DriftDataset(Dataset):
    def __init__(self, X, y):
        self.X = torch.tensor(X, dtype=torch.float32)
        self.y = torch.tensor(y, dtype=torch.float32).unsqueeze(1)
        
    def __len__(self):
        return len(self.X)
    
    def __getitem__(self, idx):
        return self.X[idx], self.y[idx]

# 2. Định nghĩa Model siêu nhẹ (MLP/Dense Neural Network) 
# Input: 9 value (Gyro X,Y,Z + Accel X,Y,Z + Mag X,Y,Z)
# Hidden Layers: 16 -> 8
# Output: 1 value (Target Yaw Drift in degrees)
class DriftPredictor(nn.Module):
    def __init__(self, input_size=9):
        super(DriftPredictor, self).__init__()
        self.network = nn.Sequential(
            nn.Linear(input_size, 16),
            nn.ReLU(),
            nn.Linear(16, 8),
            nn.ReLU(),
            nn.Linear(8, 1) # Output = Error Yaw Angle
        )
        
    def forward(self, x):
        return self.network(x)

def main():
    print("=== VR Camera Drift Prediction Training ===")
    
    # K đường dẫn file CSV
    # Lấy đường dẫn base từ thư mục chứa file py => lùi lại 1 bậc tìm logs/
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    csv_path = os.path.join(base_dir, "logs", "DriftDataset.csv")
    
    if not os.path.exists(csv_path):
        print(f"[Error] Không tìm thấy file dữ liệu tại {csv_path}")
        return

    # Load data
    print("Loading data...")
    df = pd.read_csv(csv_path)
    # Loại bỏ các dòng lỗi thiếu dữ liệu
    df.dropna(inplace=True) 

    # Xem trước một số dòng
    print(f"Tổng số frame dữ liệu: {len(df)}")
    
    # Tính năng (Features): Gyro(3), Accel(3), Mag(3)
    feature_cols = ['GyroX', 'GyroY', 'GyroZ', 'AccelX', 'AccelY', 'AccelZ', 'MagX', 'MagY', 'MagZ']
    X = df[feature_cols].values
    
    # Nhãn (Label): TargetYawDrift 
    y = df['TargetYawDrift'].values

    # Chuẩn hoá dữ liệu (Z-score Normalization)
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Chia tập train/test (80/20)
    X_train, X_test, y_train, y_test = train_test_split(X_scaled, y, test_size=0.2, random_state=42)

    # Khởi tạo Pytorch Dataset
    train_dataset = DriftDataset(X_train, y_train)
    test_dataset = DriftDataset(X_test, y_test)

    train_loader = DataLoader(train_dataset, batch_size=64, shuffle=True)
    test_loader = DataLoader(test_dataset, batch_size=64, shuffle=False)

    # Model, Loss_Fn (MSE) và Optimizer
    model = DriftPredictor(input_size=9)
    criterion = nn.MSELoss() 
    optimizer = optim.Adam(model.parameters(), lr=0.005)

    epochs = 50
    print(f"\nBắt đầu Training (Epochs = {epochs})...")
    
    for epoch in range(epochs):
        model.train()
        train_loss = 0.0
        for batch_X, batch_y in train_loader:
            optimizer.zero_grad()
            outputs = model(batch_X)
            loss = criterion(outputs, batch_y)
            loss.backward()
            optimizer.step()
            train_loss += loss.item() * batch_X.size(0)
            
        train_loss = train_loss / len(train_loader.dataset)
        
        # Test Validation
        model.eval()
        test_loss = 0.0
        with torch.no_grad():
            for batch_X, batch_y in test_loader:
                outputs = model(batch_X)
                loss = criterion(outputs, batch_y)
                test_loss += loss.item() * batch_X.size(0)
        test_loss = test_loss / len(test_loader.dataset)

        if (epoch+1) % 10 == 0 or epoch == 0:
            print(f"Epoch {epoch+1:02d}/{epochs} | Train Loss (MSE): {train_loss:.4f} | Test Loss: {test_loss:.4f}")

    # Xuất mô hình ONNX ra cho Unity Sentis
    print("\n--- Training hoàn tất ---")
    onnx_path = os.path.join(base_dir, "Resources", "DriftPredictor.onnx")
    
    # Ensure Thư mục Resources tồn tại
    os.makedirs(os.path.dirname(onnx_path), exist_ok=True)
    
    print(f"Đang xuất Model ra định dạng ONNX tới: {onnx_path}")
    
    # Tạo dummy input với kích thước (BatchSize=1, Features=9)
    dummy_input = torch.randn(1, 9, dtype=torch.float32)

    torch.onnx.export(
        model, 
        dummy_input, 
        onnx_path, 
        export_params=True,
        opset_version=15,          # Tương thích với Barracuda hoặc Sentis
        do_constant_folding=True,
        input_names=['input_1'],   # Tên input (Unity sẽ đọc tên này)
        output_names=['output_1'], # Tên output
        dynamic_axes={'input_1' : {0 : 'batch_size'}, 'output_1' : {0 : 'batch_size'}}
    )
    
    print("\n[Thành công] Mô hình đã sẵn sàng cho Unity!")
    
    # LƯU Ý: Lưu luôn cả 9 tham số scaling (Mean và Variance) để lúc ở C# còn scale lại dữ liệu Gyro thực
    # C# không có StandardScaler nên ta phải in ra terminal và hardcode vào C#
    print("\n--> LƯU Ý RẤT QUAN TRỌNG CHO C# <--")
    print("Dữ liệu cần chuẩn hoá trước khi thả vào ONNX Model.")
    print(f"Mean của 9 trục: {scaler.mean_.tolist()}")
    print(f"Scale (Std) của 9 trục: {scaler.scale_.tolist()}")

if __name__ == "__main__":
    main()
