using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Service for managing media playlists.
/// Handles playlist CRUD operations and persistence.
/// </summary>
public class MediaPlaylistService : MonoBehaviour
{
    #region Singleton
    private static MediaPlaylistService _instance;
    public static MediaPlaylistService Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("MediaPlaylistService");
                _instance = go.AddComponent<MediaPlaylistService>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    #endregion

    #region Events
    public event Action<MediaPlaylist> OnPlaylistCreated;
    public event Action<MediaPlaylist> OnPlaylistUpdated;
    public event Action<string> OnPlaylistDeleted;
    public event Action OnPlaylistsChanged;
    #endregion

    #region Constants
    private const string PLAYLISTS_KEY = "VRWorkspace_Playlists";
    private const string PLAYLISTS_DATA_KEY = "VRWorkspace_PlaylistsData";
    #endregion

    #region Private Fields
    private List<MediaPlaylist> _playlists = new List<MediaPlaylist>();
    private bool _isLoaded = false;
    #endregion

    #region Initialization
    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPlaylists();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region Public Methods - CRUD
    /// <summary>
    /// Get all playlists.
    /// </summary>
    public List<MediaPlaylist> GetAllPlaylists()
    {
        EnsureLoaded();
        return new List<MediaPlaylist>(_playlists);
    }

    /// <summary>
    /// Get playlist by ID. Returns default if not found.
    /// </summary>
    public MediaPlaylist GetPlaylist(string id)
    {
        EnsureLoaded();
        var playlist = _playlists.Find(p => p.Id == id);
        return playlist;
    }

    /// <summary>
    /// Create a new playlist.
    /// </summary>
    public MediaPlaylist CreatePlaylist(string name)
    {
        EnsureLoaded();

        var playlist = new MediaPlaylist
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Created = DateTime.Now,
            Modified = DateTime.Now,
            VideoPaths = new List<string>(),
            Settings = new PlaylistSettings()
        };

        _playlists.Add(playlist);
        SavePlaylists();

        OnPlaylistCreated?.Invoke(playlist);
        OnPlaylistsChanged?.Invoke();

        Debug.Log($"[MediaPlaylistService] Created playlist: {name}");
        return playlist;
    }

    /// <summary>
    /// Update playlist metadata.
    /// </summary>
    public void UpdatePlaylist(string id, string name = null, PlaylistSettings settings = null)
    {
        EnsureLoaded();

        int index = _playlists.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {id}");
            return;
        }

        var playlist = _playlists[index];

        if (name != null)
            playlist.Name = name;
        if (settings != null)
            playlist.Settings = settings;

        playlist.Modified = DateTime.Now;
        _playlists[index] = playlist;

        SavePlaylists();
        OnPlaylistUpdated?.Invoke(playlist);
        OnPlaylistsChanged?.Invoke();
    }

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    public void DeletePlaylist(string id)
    {
        EnsureLoaded();

        int removed = _playlists.RemoveAll(p => p.Id == id);
        if (removed > 0)
        {
            SavePlaylists();
            OnPlaylistDeleted?.Invoke(id);
            OnPlaylistsChanged?.Invoke();
            Debug.Log($"[MediaPlaylistService] Deleted playlist: {id}");
        }
    }
    #endregion

    #region Public Methods - Video Operations
    /// <summary>
    /// Add video to playlist.
    /// </summary>
    public void AddVideoToPlaylist(string playlistId, string videoPath)
    {
        EnsureLoaded();

        int index = _playlists.FindIndex(p => p.Id == playlistId);
        if (index < 0)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {playlistId}");
            return;
        }

        var playlist = _playlists[index];

        if (!playlist.VideoPaths.Contains(videoPath))
        {
            playlist.VideoPaths.Add(videoPath);
            playlist.Modified = DateTime.Now;
            _playlists[index] = playlist;

            SavePlaylists();
            OnPlaylistUpdated?.Invoke(playlist);
            OnPlaylistsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Add multiple videos to playlist.
    /// </summary>
    public void AddVideosToPlaylist(string playlistId, IEnumerable<string> videoPaths)
    {
        EnsureLoaded();

        int index = _playlists.FindIndex(p => p.Id == playlistId);
        if (index < 0)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {playlistId}");
            return;
        }

        var playlist = _playlists[index];
        bool changed = false;

        foreach (string path in videoPaths)
        {
            if (!playlist.VideoPaths.Contains(path))
            {
                playlist.VideoPaths.Add(path);
                changed = true;
            }
        }

        if (changed)
        {
            playlist.Modified = DateTime.Now;
            _playlists[index] = playlist;

            SavePlaylists();
            OnPlaylistUpdated?.Invoke(playlist);
            OnPlaylistsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Remove video from playlist.
    /// </summary>
    public void RemoveVideoFromPlaylist(string playlistId, string videoPath)
    {
        EnsureLoaded();

        int index = _playlists.FindIndex(p => p.Id == playlistId);
        if (index < 0)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {playlistId}");
            return;
        }

        var playlist = _playlists[index];

        if (playlist.VideoPaths.Remove(videoPath))
        {
            playlist.Modified = DateTime.Now;
            _playlists[index] = playlist;

            SavePlaylists();
            OnPlaylistUpdated?.Invoke(playlist);
            OnPlaylistsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Reorder videos in playlist.
    /// </summary>
    public void ReorderVideos(string playlistId, int fromIndex, int toIndex)
    {
        EnsureLoaded();

        int index = _playlists.FindIndex(p => p.Id == playlistId);
        if (index < 0)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {playlistId}");
            return;
        }

        var playlist = _playlists[index];

        if (fromIndex < 0 || fromIndex >= playlist.VideoPaths.Count ||
            toIndex < 0 || toIndex >= playlist.VideoPaths.Count)
        {
            Debug.LogWarning("[MediaPlaylistService] Invalid reorder indices");
            return;
        }

        string video = playlist.VideoPaths[fromIndex];
        playlist.VideoPaths.RemoveAt(fromIndex);
        playlist.VideoPaths.Insert(toIndex, video);
        playlist.Modified = DateTime.Now;
        _playlists[index] = playlist;

        SavePlaylists();
        OnPlaylistUpdated?.Invoke(playlist);
        OnPlaylistsChanged?.Invoke();
    }

    /// <summary>
    /// Get videos in playlist.
    /// </summary>
    public List<string> GetPlaylistVideos(string playlistId)
    {
        var playlist = GetPlaylist(playlistId);
        return playlist != null ? new List<string>(playlist.VideoPaths) : new List<string>();
    }

    /// <summary>
    /// Get playlists containing a video.
    /// </summary>
    public List<MediaPlaylist> GetPlaylistsContainingVideo(string videoPath)
    {
        EnsureLoaded();
        return _playlists.Where(p => p.VideoPaths.Contains(videoPath)).ToList();
    }

    /// <summary>
    /// Check if video is in any playlist.
    /// </summary>
    public bool IsVideoInAnyPlaylist(string videoPath)
    {
        EnsureLoaded();
        return _playlists.Any(p => p.VideoPaths.Contains(videoPath));
    }
    #endregion

    #region Playback Queue
    private List<string> _playbackQueue = new List<string>();
    private int _currentQueueIndex = -1;
    private bool _shuffleEnabled = false;
    private RepeatMode _repeatMode = RepeatMode.None;

    /// <summary>
    /// Set playlist for playback.
    /// </summary>
    public void SetPlaybackPlaylist(string playlistId, bool shuffle = false)
    {
        var playlist = GetPlaylist(playlistId);
        if (playlist == null)
        {
            Debug.LogWarning($"[MediaPlaylistService] Playlist not found: {playlistId}");
            return;
        }

        _playbackQueue = new List<string>(playlist.VideoPaths);
        _shuffleEnabled = shuffle;
        _repeatMode = playlist.Settings.Repeat;

        if (shuffle)
        {
            ShuffleQueue();
        }

        _currentQueueIndex = -1;
    }

    /// <summary>
    /// Get next video in queue.
    /// </summary>
    public string GetNextVideo()
    {
        if (_playbackQueue.Count == 0)
            return null;

        _currentQueueIndex++;

        if (_currentQueueIndex >= _playbackQueue.Count)
        {
            switch (_repeatMode)
            {
                case RepeatMode.RepeatAll:
                    _currentQueueIndex = 0;
                    if (_shuffleEnabled)
                        ShuffleQueue();
                    break;
                case RepeatMode.RepeatOne:
                    _currentQueueIndex = Mathf.Max(0, _currentQueueIndex - 1);
                    break;
                default:
                    _currentQueueIndex = _playbackQueue.Count;  // End of queue
                    return null;
            }
        }

        return _currentQueueIndex < _playbackQueue.Count ? _playbackQueue[_currentQueueIndex] : null;
    }

    /// <summary>
    /// Get previous video in queue.
    /// </summary>
    public string GetPreviousVideo()
    {
        if (_playbackQueue.Count == 0)
            return null;

        _currentQueueIndex--;

        if (_currentQueueIndex < 0)
        {
            _currentQueueIndex = _repeatMode == RepeatMode.RepeatAll ? _playbackQueue.Count - 1 : 0;
        }

        return _playbackQueue[_currentQueueIndex];
    }

    /// <summary>
    /// Get current video in queue.
    /// </summary>
    public string GetCurrentVideo()
    {
        if (_playbackQueue.Count == 0 || _currentQueueIndex < 0 || _currentQueueIndex >= _playbackQueue.Count)
            return null;

        return _playbackQueue[_currentQueueIndex];
    }

    /// <summary>
    /// Set shuffle mode.
    /// </summary>
    public void SetShuffle(bool enabled)
    {
        _shuffleEnabled = enabled;
        if (enabled && _playbackQueue.Count > 0)
        {
            string current = GetCurrentVideo();
            ShuffleQueue();
            // Move current video to front
            if (!string.IsNullOrEmpty(current))
            {
                _playbackQueue.Remove(current);
                _playbackQueue.Insert(0, current);
                _currentQueueIndex = 0;
            }
        }
    }

    /// <summary>
    /// Set repeat mode.
    /// </summary>
    public void SetRepeatMode(RepeatMode mode)
    {
        _repeatMode = mode;
    }

    private void ShuffleQueue()
    {
        System.Random rng = new System.Random();
        int n = _playbackQueue.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            string temp = _playbackQueue[k];
            _playbackQueue[k] = _playbackQueue[n];
            _playbackQueue[n] = temp;
        }
    }

    /// <summary>
    /// Get the current playback queue.
    /// </summary>
    public List<string> GetPlaybackQueue()
    {
        return new List<string>(_playbackQueue);
    }

    /// <summary>Current queue index</summary>
    public int CurrentQueueIndex => _currentQueueIndex;

    /// <summary>
    /// Set playback queue directly from a list of video paths.
    /// Used when playing from library (not from a saved playlist).
    /// </summary>
    public void SetPlaybackQueueDirect(List<string> paths, int startIndex = 0)
    {
        _playbackQueue = new List<string>(paths);
        _currentQueueIndex = startIndex;
        _shuffleEnabled = false;
        _repeatMode = RepeatMode.None;
    }

    /// <summary>
    /// Jump to specific index in queue. Returns path or null.
    /// </summary>
    public string JumpToIndex(int index)
    {
        if (index < 0 || index >= _playbackQueue.Count) return null;
        _currentQueueIndex = index;
        return _playbackQueue[index];
    }
    #endregion

    #region Persistence
    private void LoadPlaylists()
    {
        _playlists.Clear();

        string json = PlayerPrefs.GetString(PLAYLISTS_DATA_KEY, "");
        if (string.IsNullOrEmpty(json))
        {
            _isLoaded = true;
            return;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<PlaylistsWrapper>(json);
            if (wrapper != null && wrapper.playlists != null)
            {
                _playlists = wrapper.playlists;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MediaPlaylistService] Error loading playlists: {ex.Message}");
        }

        _isLoaded = true;
        Debug.Log($"[MediaPlaylistService] Loaded {_playlists.Count} playlists");
    }

    private void SavePlaylists()
    {
        try
        {
            var wrapper = new PlaylistsWrapper { playlists = _playlists };
            string json = JsonUtility.ToJson(wrapper);
            PlayerPrefs.SetString(PLAYLISTS_DATA_KEY, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MediaPlaylistService] Error saving playlists: {ex.Message}");
        }
    }

    private void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            LoadPlaylists();
        }
    }

    [Serializable]
    private class PlaylistsWrapper
    {
        public List<MediaPlaylist> playlists = new List<MediaPlaylist>();
    }
    #endregion

    #region Unity Lifecycle
    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            SavePlaylists();
        }
    }

    private void OnApplicationQuit()
    {
        SavePlaylists();
    }
    #endregion
}
