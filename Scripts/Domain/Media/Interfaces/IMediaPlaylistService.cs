using System;
using System.Collections.Generic;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Domain.Media
{
    /// <summary>
    /// Playlist management service contract. Extracted from MediaPlaylistService.
    /// </summary>
    public interface IMediaPlaylistService
    {
        List<MediaPlaylist> GetPlaylists();
        MediaPlaylist GetPlaylist(string id);
        MediaPlaylist CreatePlaylist(string name);
        void DeletePlaylist(string id);
        void AddToPlaylist(string playlistId, string videoPath);
        void RemoveFromPlaylist(string playlistId, string videoPath);

        event Action OnPlaylistsChanged;
    }
}
