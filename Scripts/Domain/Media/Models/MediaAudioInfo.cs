using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRWorkspace.Media.Data
{
    /// <summary>
    /// Data model for audio files in Media Library
    /// </summary>
    [Serializable]
    public struct MediaAudioInfo
    {
        #region Identity
        /// <summary>Full path to audio file</summary>
        public string Path;

        /// <summary>Display title (from ID3 tag or filename)</summary>
        public string Title;
        #endregion

        #region Audio Properties
        /// <summary>Audio duration</summary>
        [NonSerialized]
        public TimeSpan Duration;

        /// <summary>Audio file format</summary>
        public AudioFormat Format;

        /// <summary>Bit rate in kbps</summary>
        public int BitRate;

        /// <summary>Sample rate in Hz</summary>
        public int SampleRate;

        /// <summary>Number of channels (1=mono, 2=stereo)</summary>
        public int Channels;

        /// <summary>File size in bytes</summary>
        public long FileSizeBytes;
        #endregion

        #region ID3 Metadata
        /// <summary>Artist name</summary>
        public string Artist;

        /// <summary>Album name</summary>
        public string Album;

        /// <summary>Album artist (for compilations)</summary>
        public string AlbumArtist;

        /// <summary>Genre</summary>
        public string Genre;

        /// <summary>Year of release</summary>
        public int Year;

        /// <summary>Track number</summary>
        public int TrackNumber;

        /// <summary>Total tracks in album</summary>
        public int TotalTracks;

        /// <summary>Disc number</summary>
        public int DiscNumber;

        /// <summary>Total discs</summary>
        public int TotalDiscs;

        /// <summary>Composer</summary>
        public string Composer;

        /// <summary>Lyrics</summary>
        public string Lyrics;

        /// <summary>Comment</summary>
        public string Comment;
        #endregion

        #region Timestamps
        /// <summary>When file was added to library</summary>
        public DateTime DateAdded;

        /// <summary>File modification date</summary>
        public DateTime DateModified;

        /// <summary>Last time audio was played</summary>
        public DateTime LastPlayed;

        /// <summary>Number of times played</summary>
        public int PlayCount;
        #endregion

        #region User Data
        /// <summary>Is in favorites list</summary>
        public bool IsFavorite;

        /// <summary>User rating (0-5 stars)</summary>
        public int Rating;

        /// <summary>Playlist IDs this audio belongs to</summary>
        public List<string> PlaylistIds;

        /// <summary>Last playback position for resume</summary>
        [NonSerialized]
        public TimeSpan LastPosition;
        #endregion

        #region Cached
        /// <summary>Cached album art texture</summary>
        [NonSerialized]
        public Texture2D AlbumArt;
        #endregion

        #region Computed Properties
        /// <summary>Get formatted duration string (HH:MM:SS or MM:SS)</summary>
        public string FormattedDuration
        {
            get
            {
                if (Duration.TotalHours >= 1)
                    return $"{(int)Duration.TotalHours}:{Duration.Minutes:D2}:{Duration.Seconds:D2}";
                return $"{Duration.Minutes}:{Duration.Seconds:D2}";
            }
        }

        /// <summary>Get formatted file size (KB, MB, GB)</summary>
        public string FormattedSize
        {
            get
            {
                if (FileSizeBytes >= 1024L * 1024 * 1024)
                    return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F1} GB";
                if (FileSizeBytes >= 1024L * 1024)
                    return $"{FileSizeBytes / (1024.0 * 1024):F1} MB";
                if (FileSizeBytes >= 1024)
                    return $"{FileSizeBytes / 1024.0:F1} KB";
                return $"{FileSizeBytes} B";
            }
        }

        /// <summary>Get formatted bit rate</summary>
        public string FormattedBitRate => BitRate > 0 ? $"{BitRate} kbps" : null;

        /// <summary>Get formatted sample rate</summary>
        public string FormattedSampleRate => SampleRate > 0 ? $"{SampleRate / 1000f:F1} kHz" : null;

        /// <summary>Get channels description</summary>
        public string ChannelsDescription => Channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            6 => "5.1 Surround",
            8 => "7.1 Surround",
            _ => Channels > 0 ? $"{Channels} channels" : null
        };

        /// <summary>Check if audio has been played before</summary>
        public bool HasBeenPlayed => LastPlayed != default;

        /// <summary>Check if audio can resume from last position</summary>
        public bool CanResume => LastPosition.TotalSeconds > 10 &&
                                  LastPosition < Duration - TimeSpan.FromSeconds(30);

        /// <summary>Get display artist (or "Unknown Artist")</summary>
        public string DisplayArtist => !string.IsNullOrEmpty(Artist) ? Artist : "Unknown Artist";

        /// <summary>Get display album (or "Unknown Album")</summary>
        public string DisplayAlbum => !string.IsNullOrEmpty(Album) ? Album : "Unknown Album";

        /// <summary>Get track and disc info string</summary>
        public string TrackInfo
        {
            get
            {
                if (TrackNumber <= 0) return null;
                string track = TotalTracks > 0 ? $"{TrackNumber}/{TotalTracks}" : TrackNumber.ToString();
                if (DiscNumber > 0)
                {
                    string disc = TotalDiscs > 0 ? $"{DiscNumber}/{TotalDiscs}" : DiscNumber.ToString();
                    return $"Disc {disc}, Track {track}";
                }
                return $"Track {track}";
            }
        }

        /// <summary>Get audio quality description</summary>
        public AudioQuality Quality
        {
            get
            {
                if (Format == AudioFormat.FLAC || Format == AudioFormat.WAV || Format == AudioFormat.ALAC)
                {
                    if (SampleRate >= 96000 || BitRate >= 2000)
                        return AudioQuality.HiRes;
                    return AudioQuality.Lossless;
                }
                if (BitRate >= 320) return AudioQuality.High;
                if (BitRate >= 192) return AudioQuality.Medium;
                return AudioQuality.Low;
            }
        }

        /// <summary>Get quality badge text</summary>
        public string QualityBadge => Quality switch
        {
            AudioQuality.HiRes => "Hi-Res",
            AudioQuality.Lossless => "Lossless",
            AudioQuality.High => "HQ",
            _ => null
        };
        #endregion

        #region Factory Methods
        /// <summary>
        /// Create MediaAudioInfo from file path
        /// </summary>
        public static MediaAudioInfo FromPath(string path)
        {
            var info = new MediaAudioInfo
            {
                Path = path,
                Title = System.IO.Path.GetFileNameWithoutExtension(path),
                Format = DetectFormat(path),
                PlaylistIds = new List<string>()
            };

            // Get file info
            try
            {
                var fileInfo = new FileInfo(path);
                info.FileSizeBytes = fileInfo.Length;
                info.DateModified = fileInfo.LastWriteTime;
                info.DateAdded = fileInfo.CreationTime;
            }
            catch
            {
                info.DateAdded = DateTime.Now;
            }

            return info;
        }

        /// <summary>
        /// Detect audio format from file extension
        /// </summary>
        public static AudioFormat DetectFormat(string path)
        {
            string ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".mp3" => AudioFormat.MP3,
                ".flac" => AudioFormat.FLAC,
                ".wav" => AudioFormat.WAV,
                ".aac" or ".m4a" => AudioFormat.AAC,
                ".ogg" or ".oga" => AudioFormat.OGG,
                ".wma" => AudioFormat.WMA,
                ".alac" => AudioFormat.ALAC,
                ".aiff" or ".aif" => AudioFormat.AIFF,
                ".opus" => AudioFormat.OPUS,
                _ => AudioFormat.Unknown
            };
        }

        /// <summary>
        /// Create a copy with updated metadata
        /// </summary>
        public MediaAudioInfo WithMetadata(string title = null, string artist = null, string album = null)
        {
            var copy = this;
            if (!string.IsNullOrEmpty(title)) copy.Title = title;
            if (!string.IsNullOrEmpty(artist)) copy.Artist = artist;
            if (!string.IsNullOrEmpty(album)) copy.Album = album;
            return copy;
        }
        #endregion
    }

    /// <summary>
    /// Audio file format
    /// </summary>
    public enum AudioFormat
    {
        MP3,
        FLAC,
        WAV,
        AAC,
        OGG,
        WMA,
        ALAC,
        AIFF,
        OPUS,
        Unknown
    }

    /// <summary>
    /// Audio quality level
    /// </summary>
    public enum AudioQuality
    {
        Low,       // < 192 kbps lossy
        Medium,    // 192-319 kbps lossy
        High,      // 320+ kbps lossy
        Lossless,  // FLAC, WAV, ALAC at standard sample rates
        HiRes      // Lossless at 96kHz+ or very high bitrate
    }

}
