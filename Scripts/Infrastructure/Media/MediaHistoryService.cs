using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Infrastructure.Media
{
    /// <summary>
    /// Manages the playback history for the media library.
    /// Stores the most recent <see cref="MaxHistory"/> played paths in order (newest first).
    /// Persists via PlayerPrefs. Plain C# class - no MonoBehaviour required.
    /// </summary>
    public class MediaHistoryService
    {
        #region Constants
        private const string HISTORY_KEY = "MediaLibrary_History";
        public  const int    MaxHistory  = 50;
        #endregion

        #region Events
        /// <summary>Fired after a play is recorded. Arg: the path that was played.</summary>
        public event Action<string> OnPlayRecorded;
        #endregion

        #region Private Fields
        private List<string> _history = new List<string>();
        #endregion

        public MediaHistoryService()
        {
            Load();
        }

        #region Public API
        /// <summary>
        /// Record a play for <paramref name="path"/>.
        /// Moves path to the front of the history and trims to <see cref="MaxHistory"/>.
        /// Also updates <c>LastPlayed</c> on the matching entry in <paramref name="allVideos"/>.
        /// </summary>
        public void RecordPlay(string path, List<MediaVideoInfo> allVideos = null)
        {
            _history.Remove(path);
            _history.Insert(0, path);

            if (_history.Count > MaxHistory)
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);

            // Sync LastPlayed on matching video struct
            if (allVideos != null)
            {
                for (int i = 0; i < allVideos.Count; i++)
                {
                    if (allVideos[i].Path == path)
                    {
                        var video = allVideos[i];
                        video.LastPlayed = DateTime.Now;
                        allVideos[i]     = video;
                        break;
                    }
                }
            }

            Save();
            OnPlayRecorded?.Invoke(path);
        }

        /// <summary>
        /// Get the most recent <paramref name="count"/> played paths (newest first).
        /// </summary>
        public List<string> GetHistoryPaths(int count = MaxHistory)
        {
            return _history.Take(count).ToList();
        }

        /// <summary>
        /// Get playback history as resolved <see cref="MediaVideoInfo"/> objects.
        /// Paths that no longer exist in <paramref name="allVideos"/> are skipped.
        /// </summary>
        public List<MediaVideoInfo> GetHistoryVideos(List<MediaVideoInfo> allVideos, int count = MaxHistory)
        {
            var result = new List<MediaVideoInfo>();
            foreach (var path in _history.Take(count))
            {
                var video = allVideos.FirstOrDefault(v => v.Path == path);
                if (video.Path != null)
                    result.Add(video);
            }
            return result;
        }

        /// <summary>Clear the full history.</summary>
        public void ClearHistory()
        {
            _history.Clear();
            Save();
        }
        #endregion

        #region Persistence
        private void Load()
        {
            string json = PlayerPrefs.GetString(HISTORY_KEY, "[]");
            try
            {
                var list = JsonUtility.FromJson<StringListWrapper>(json);
                _history = list?.items ?? new List<string>();
            }
            catch
            {
                _history = new List<string>();
            }
        }

        private void Save()
        {
            var wrapper = new StringListWrapper { items = _history };
            string json = JsonUtility.ToJson(wrapper);
            PlayerPrefs.SetString(HISTORY_KEY, json);
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
