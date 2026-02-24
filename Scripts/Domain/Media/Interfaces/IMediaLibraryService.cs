using System;
using System.Collections.Generic;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Domain.Media
{
    /// <summary>
    /// Media library service contract. Extracted from MediaLibraryService singleton.
    /// </summary>
    public interface IMediaLibraryService
    {
        List<MediaVideoInfo> AllVideos { get; }
        List<MediaAudioInfo> AllAudio { get; }
        List<MediaImageInfo> AllImages { get; }
        bool IsScanning { get; }
        bool IsLibraryLoaded { get; }

        void StartScan();
        void StopScan();
        List<MediaVideoInfo> GetFavorites();
        void ToggleFavorite(string path);
        bool IsFavorite(string path);
        List<MediaVideoInfo> Search(string query);

        event Action OnLibraryUpdated;
        event Action<float> OnScanProgress;
    }
}
