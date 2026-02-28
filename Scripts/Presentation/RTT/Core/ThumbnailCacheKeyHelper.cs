using System.Security.Cryptography;
using System.Text;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Helper class to generate consistent cache keys for thumbnails.
    /// </summary>
    public static class ThumbnailCacheKeyHelper
    {
        /// <summary>
        /// Generate a cache key for a file path.
        /// Uses MD5 hash of the path for a compact, consistent key.
        /// </summary>
        public static string GetCacheKey(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(filePath.ToLowerInvariant());
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Generate a cache key with file modification time for invalidation.
        /// </summary>
        public static string GetCacheKeyWithTime(string filePath, long fileModifiedTicks)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string combined = $"{filePath.ToLowerInvariant()}_{fileModifiedTicks}";

            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(combined);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
