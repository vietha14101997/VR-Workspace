using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Infrastructure.Media
{
    /// <summary>
    /// Manages the favorites list for the media library.
    /// Persists via PlayerPrefs. Plain C# class - no MonoBehaviour required.
    /// </summary>
    public class MediaFavoritesService
    {
        #region Constants
        private const string FAVORITES_KEY = "MediaLibrary_Favorites";
        #endregion

        #region Events
        /// <summary>Fired after a favorite is toggled. Args: (path, isFavorite).</summary>
        public event Action<string, bool> OnFavoriteChanged;
        #endregion

        #region Private Fields
        private HashSet<string> _favorites = new HashSet<string>();
        #endregion

        public MediaFavoritesService()
        {
            Load();
        }

        #region Public API
        /// <summary>Returns a read-only snapshot of all favorite paths.</summary>
        public HashSet<string> GetFavoritePaths() => new HashSet<string>(_favorites);

        /// <summary>Check whether a path is marked as favorite.</summary>
        public bool IsFavorite(string path) => _favorites.Contains(path);

        /// <summary>Toggle favorite status. Returns the new state.</summary>
        public bool ToggleFavorite(string path)
        {
            bool newState = !_favorites.Contains(path);
            SetFavorite(path, newState);
            return newState;
        }

        /// <summary>Add a path to favorites.</summary>
        public void AddFavorite(string path) => SetFavorite(path, true);

        /// <summary>Remove a path from favorites.</summary>
        public void RemoveFavorite(string path) => SetFavorite(path, false);

        /// <summary>
        /// Set favorite status for a path and update the flag on the matching video inside
        /// <paramref name="allVideos"/> (modifies the struct in-place).
        /// </summary>
        public void SetFavorite(string path, bool isFavorite, List<MediaVideoInfo> allVideos = null)
        {
            if (isFavorite) _favorites.Add(path);
            else            _favorites.Remove(path);

            // Sync flag on matching entry in the library list
            if (allVideos != null)
            {
                for (int i = 0; i < allVideos.Count; i++)
                {
                    if (allVideos[i].Path == path)
                    {
                        var video = allVideos[i];
                        video.IsFavorite = isFavorite;
                        allVideos[i]     = video;
                        break;
                    }
                }
            }

            Save();
            OnFavoriteChanged?.Invoke(path, isFavorite);
        }

        /// <summary>Return all videos from <paramref name="allVideos"/> that are favorited.</summary>
        public List<MediaVideoInfo> GetFavoriteVideos(List<MediaVideoInfo> allVideos)
        {
            return allVideos.Where(v => v.IsFavorite).ToList();
        }

        /// <summary>Apply the current favorites set to an entire list (e.g. after loading from cache).</summary>
        public void ApplyFavoritesToList(List<MediaVideoInfo> allVideos)
        {
            for (int i = 0; i < allVideos.Count; i++)
            {
                var video = allVideos[i];
                bool wasFavorite = video.IsFavorite;
                bool isFavorite  = _favorites.Contains(video.Path);
                if (wasFavorite != isFavorite)
                {
                    video.IsFavorite = isFavorite;
                    allVideos[i]     = video;
                }
            }
        }
        #endregion

        #region Persistence
        private void Load()
        {
            string json = PlayerPrefs.GetString(FAVORITES_KEY, "[]");
            try
            {
                var list = JsonUtility.FromJson<StringListWrapper>(json);
                _favorites = new HashSet<string>(list?.items ?? new List<string>());
            }
            catch
            {
                _favorites = new HashSet<string>();
            }
        }

        private void Save()
        {
            var wrapper = new StringListWrapper { items = _favorites.ToList() };
            string json = JsonUtility.ToJson(wrapper);
            PlayerPrefs.SetString(FAVORITES_KEY, json);
            PlayerPrefs.Save();
        }
        #endregion

        #region Serialization Helper
        [Serializable]
        private class StringListWrapper
        {
            public List<string> items = new List<string>();
        }
        #endregion
    }
}
