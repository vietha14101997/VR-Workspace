using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRWorkspace.Media.Data
{
    /// <summary>
    /// Data model cho playlist
    /// </summary>
    [Serializable]
    public class MediaPlaylist
    {
        /// <summary>Unique identifier</summary>
        public string Id;

        /// <summary>Display name</summary>
        public string Name;

        /// <summary>Optional custom icon</summary>
        public Sprite CustomIcon;

        /// <summary>List of video paths in this playlist</summary>
        public List<string> VideoPaths = new List<string>();

        /// <summary>Creation timestamp</summary>
        public DateTime Created;

        /// <summary>Last modification timestamp</summary>
        public DateTime Modified;

        /// <summary>Playlist settings</summary>
        public PlaylistSettings Settings = new PlaylistSettings();

        /// <summary>Number of videos in playlist</summary>
        public int VideoCount => VideoPaths?.Count ?? 0;

        /// <summary>
        /// Create a new playlist with default settings
        /// </summary>
        public static MediaPlaylist Create(string name)
        {
            return new MediaPlaylist
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                VideoPaths = new List<string>(),
                Created = DateTime.Now,
                Modified = DateTime.Now,
                Settings = new PlaylistSettings()
            };
        }

        /// <summary>
        /// Add video to playlist
        /// </summary>
        public void AddVideo(string path)
        {
            if (!VideoPaths.Contains(path))
            {
                VideoPaths.Add(path);
                Modified = DateTime.Now;
            }
        }

        /// <summary>
        /// Remove video from playlist
        /// </summary>
        public bool RemoveVideo(string path)
        {
            bool removed = VideoPaths.Remove(path);
            if (removed) Modified = DateTime.Now;
            return removed;
        }

        /// <summary>
        /// Check if playlist contains video
        /// </summary>
        public bool ContainsVideo(string path)
        {
            return VideoPaths.Contains(path);
        }

        /// <summary>
        /// Get index of video in playlist
        /// </summary>
        public int IndexOf(string path)
        {
            return VideoPaths.IndexOf(path);
        }

        /// <summary>
        /// Move video to new position
        /// </summary>
        public void MoveVideo(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= VideoPaths.Count) return;
            if (toIndex < 0 || toIndex >= VideoPaths.Count) return;

            var video = VideoPaths[fromIndex];
            VideoPaths.RemoveAt(fromIndex);
            VideoPaths.Insert(toIndex, video);
            Modified = DateTime.Now;
        }
    }

    /// <summary>
    /// Playlist playback settings
    /// </summary>
    [Serializable]
    public class PlaylistSettings
    {
        /// <summary>Auto-play next video when current ends</summary>
        public bool AutoPlay = true;

        /// <summary>Shuffle playback order</summary>
        public bool ShuffleEnabled = false;

        /// <summary>Repeat mode</summary>
        public RepeatMode Repeat = RepeatMode.None;
    }

    /// <summary>
    /// Playlist repeat mode
    /// </summary>
    public enum RepeatMode
    {
        /// <summary>Stop when playlist ends</summary>
        None,

        /// <summary>Repeat current video</summary>
        RepeatOne,

        /// <summary>Repeat entire playlist</summary>
        RepeatAll
    }

}
