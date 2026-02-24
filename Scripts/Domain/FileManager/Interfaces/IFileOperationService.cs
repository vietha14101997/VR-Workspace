using System;
using System.Threading.Tasks;

namespace VRWorkspace.Domain.FileManager
{
    /// <summary>
    /// File operation service contract. Extracted from FileOperationService.
    /// </summary>
    public interface IFileOperationService
    {
        Task<bool> CopyAsync(string sourcePath, string destinationPath, Action<float> onProgress = null);
        Task<bool> MoveAsync(string sourcePath, string destinationPath, Action<float> onProgress = null);
        Task<bool> DeleteAsync(string path);
        Task<bool> RenameAsync(string path, string newName);
        bool Exists(string path);
    }
}
