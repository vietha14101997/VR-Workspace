using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Domain.FileManager
{
    /// <summary>
    /// File metadata service contract. Extracted from FileMetadataService.
    /// </summary>
    public interface IFileMetadataService
    {
        Task<Texture2D> GetThumbnailAsync(string filePath, int width = 128, int height = 128);
        string GetFileCategory(string filePath);
        long GetFileSize(string filePath);
    }
}
