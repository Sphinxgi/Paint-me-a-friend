using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
#if !AUDIO_TOOL_NOAUDIO
using JD.EditorAudioUtils;
#endif

namespace AudioTool
{
    /// <summary>
    /// Audio playback and loading for the Audio Tool.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class AudioManager
    {
        private static AudioClip _currentClip;
        private static int _rangeStartSample;
        private static int _rangeEndSample;
        private static bool _isRangePlaying;
        private static bool _currentLoop;
        private static IEditorAudioPlayback _currentPlayback;
        private static int _requestVersion;

        public static EditorAudioMetadata? CurrentMetadata => _currentPlayback != null
            ? _currentPlayback.Metadata
            : _currentClip != null ? new EditorAudioMetadata(_currentClip.channels, _currentClip.frequency, _currentClip.samples) : (EditorAudioMetadata?)null;

        /// <summary>Plays a materialized file using the project's available audio backend.</summary>
        public static async Task<bool> PlayFileAsync(string path, bool loop = false, CancellationToken token = default)
        {
            StopAudio();
            int version = _requestVersion;
            try
            {
                IEditorAudioBackend backend = EditorAudioService.GetBackend();
                IEditorAudioPlayback playback = await backend.PlayFileAsync(path, 0, long.MaxValue, loop, token);
                if (token.IsCancellationRequested || version != _requestVersion)
                {
                    playback?.Dispose();
                    return false;
                }
                _currentPlayback = playback;
                _currentLoop = loop;
                return playback != null;
            }
            catch (OperationCanceledException) { return false; }
            catch (EditorAudioUnavailableException exception)
            {
                EditorAudioService.WarnUnavailableOnce(exception);
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogError("Audio preview failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>Plays processed interleaved samples without requiring a Unity AudioClip.</summary>
        public static async Task<bool> PlaySamplesAsync(float[] samples, int channels, int frequency,
            long startFrame, long endFrameExclusive, bool loop, CancellationToken token = default)
        {
            StopAudio();
            int version = _requestVersion;
            try
            {
                IEditorAudioBackend backend = EditorAudioService.GetBackend();
                IEditorAudioPlayback playback = await backend.PlaySamplesAsync(samples, channels, frequency,
                    startFrame, endFrameExclusive, loop, token);
                if (token.IsCancellationRequested || version != _requestVersion)
                {
                    playback?.Dispose();
                    return false;
                }
                _currentPlayback = playback;
                _currentLoop = loop;
                _rangeStartSample = (int)Math.Min(int.MaxValue, Math.Max(0, startFrame));
                _rangeEndSample = (int)Math.Min(int.MaxValue, Math.Max(0, endFrameExclusive));
                _isRangePlaying = _rangeEndSample > _rangeStartSample &&
                    (startFrame > 0 || endFrameExclusive < samples.LongLength / channels);
                return playback != null;
            }
            catch (OperationCanceledException) { return false; }
            catch (EditorAudioUnavailableException exception)
            {
                EditorAudioService.WarnUnavailableOnce(exception);
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogError("Processed audio preview failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>Seeks the current editor audio preview to the specified sample frame.</summary>
        /// <param name="frame">The zero-based sample frame at which playback should continue.</param>
        public static void SeekCurrent(long frame)
        {
            if (_currentPlayback != null) _currentPlayback.Seek(frame);
            else if (_currentClip != null) PlayClip(_currentClip, (int)Math.Min(int.MaxValue, Math.Max(0, frame)), _currentLoop);
        }

        [InitializeOnLoadMethod]
        private static void RegisterEditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StopAudio;
            AssemblyReloadEvents.beforeAssemblyReload += StopAudio;
            EditorApplication.quitting -= StopAudio;
            EditorApplication.quitting += StopAudio;
        }

        /// <summary>
        /// Gets the clip most recently started through this manager, or <c>null</c> before playback.
        /// </summary>
        public static AudioClip CurrentClip => _currentClip;

        /// <summary>
        /// Gets whether the current preview is being tracked against a requested sample range.
        /// </summary>
        public static bool IsRangePlaying => _isRangePlaying;

        /// <summary>
        /// Gets the exclusive per-channel sample-frame index used to detect the end of range playback.
        /// </summary>
        public static int RangeEndSample => _rangeEndSample;

        /// <summary>
        /// Gets the inclusive per-channel sample-frame index where range playback started.
        /// </summary>
        public static int RangeStartSample => _rangeStartSample;

        /// <summary>
        /// Loads an audio file from disk.
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="allowStreaming">If true, uses streaming for better performance. Set to false to access raw sample data.</param>
        /// <returns>The loaded AudioClip, or null if loading failed</returns>
        public static async Task<AudioClip> LoadAudioFromFile(string filePath, bool allowStreaming = true)
        {
            if (!File.Exists(filePath)) return null;
            if (CommonEditorAudioSettings.WarnIfDisabled()) return null;

            // workaround for Unity not supporting loading local files with # or + or unicode chars in the name
            if (filePath.Contains("#") || filePath.Contains("+") || filePath.IsUnicode())
            {
                string newName = Path.Combine(Application.temporaryCachePath, "AudioToolPreview" + Path.GetExtension(filePath));
                File.Copy(filePath, newName, true);
                filePath = newName;
            }

            // use uri form to support network shares
            filePath = IOUtils.ToShortPath(filePath);
            string fileUri;
            try
            {
                fileUri = new Uri(filePath).AbsoluteUri;
            }
            catch (UriFormatException e)
            {
                Debug.LogError($"Could not convert path to URI '{filePath}': {e.Message}");
                return null;
            }

            // select appropriate audio type from extension where UNKNOWN heuristic can fail
            // retry with other types since some files may be stored under the wrong format
            List<AudioType> fallbackChain;
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".aiff":
                case ".aif":
                    fallbackChain = new List<AudioType> {AudioType.AIFF, AudioType.OGGVORBIS, AudioType.WAV, AudioType.UNKNOWN};
                    break;

                case ".ogg":
                    fallbackChain = new List<AudioType> {AudioType.OGGVORBIS, AudioType.WAV, AudioType.UNKNOWN, AudioType.AIFF};
                    break;

                case ".wav":
                    fallbackChain = new List<AudioType> {AudioType.WAV, AudioType.OGGVORBIS, AudioType.UNKNOWN, AudioType.AIFF};
                    break;

                case ".mp3":
                    fallbackChain = new List<AudioType> {AudioType.MPEG, AudioType.WAV, AudioType.UNKNOWN, AudioType.AIFF};
                    break;

                default:
                    fallbackChain = new List<AudioType> {AudioType.UNKNOWN, AudioType.OGGVORBIS, AudioType.WAV, AudioType.AIFF};
                    break;
            }
            fallbackChain.AddRange(new List<AudioType> {AudioType.MPEG, AudioType.IT, AudioType.S3M, AudioType.XM, AudioType.ACC, AudioType.MOD, AudioType.VAG, AudioType.XMA, AudioType.AUDIOQUEUE});

            foreach (AudioType type in fallbackChain)
            {
                using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(fileUri, type))
                {
                    // Streaming is a performance boost, but tracker formats (MOD, IT, S3M, XM) don't support streaming
                    // Also, streaming prevents access to raw sample data via GetData()
                    bool shouldStream = allowStreaming && (type == AudioType.OGGVORBIS ||
                        type == AudioType.MPEG ||
                        type == AudioType.WAV ||
                        type == AudioType.AIFF);
                    ((DownloadHandlerAudioClip)uwr.downloadHandler).streamAudio = shouldStream;
                    uwr.timeout = 30;
                    UnityWebRequestAsyncOperation request = uwr.SendWebRequest();
                    while (!request.isDone) await Task.Yield();

                    if (uwr.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"Error fetching '{filePath} ({fileUri})': {uwr.error}");
                        return null;
                    }

                    DownloadHandlerAudioClip dlHandler = (DownloadHandlerAudioClip)uwr.downloadHandler;
                    if (dlHandler.isDone)
                    {
                        AudioClip clip = dlHandler.audioClip;
                        if (clip == null || (clip.channels == 0 && clip.length == 0)) continue;

                        return clip;
                    }
                }
            }
            Debug.LogError($"Could not load audio clip '{filePath} ({fileUri})'");

            return null;
        }

        /// <summary>
        /// Plays an AudioClip directly.
        /// </summary>
        /// <param name="clip">The audio clip to play</param>
        /// <param name="startSample">Inclusive per-channel sample-frame index at which playback begins.</param>
        /// <param name="loop">Whether to loop playback</param>
        public static void PlayClip(AudioClip clip, int startSample = 0, bool loop = false)
        {
#if !AUDIO_TOOL_NOAUDIO
            if (clip == null) return;

            StopAudio();
            _currentClip = clip;
            _currentLoop = loop;
            _isRangePlaying = false;
            EditorAudioUtility.StopAllPreviewClips();
            EditorAudioUtility.PlayPreviewClip(clip, startSample, loop);
#endif
        }

        /// <summary>
        /// Plays a specific range of an AudioClip.
        /// Note: Due to Unity's API limitations, this plays from startSample but doesn't auto-stop at endSample.
        /// Use GetCurrentSamplePosition() to check and stop manually if needed.
        /// </summary>
        /// <param name="clip">The audio clip to play</param>
        /// <param name="startSample">Inclusive per-channel sample-frame index at which playback begins.</param>
        /// <param name="endSample">Exclusive per-channel sample-frame index tracked as the requested end.</param>
        /// <param name="loop">Whether to loop playback</param>
        public static void PlayClipRange(AudioClip clip, int startSample, int endSample, bool loop = false)
        {
#if !AUDIO_TOOL_NOAUDIO
            if (clip == null) return;

            StopAudio();
            _currentClip = clip;
            _currentLoop = loop;
            _rangeStartSample = startSample;
            _rangeEndSample = endSample;
            _isRangePlaying = true;
            EditorAudioUtility.StopAllPreviewClips();
            EditorAudioUtility.PlayPreviewClip(clip, startSample, loop);
#endif
        }

        /// <summary>
        /// Gets the current playback position in seconds.
        /// </summary>
        /// <returns>Current position in seconds, or 0 if not playing</returns>
        public static float GetCurrentPosition()
        {
            if (_currentPlayback != null)
                return (float)(_currentPlayback.PositionFrames / (double)_currentPlayback.Metadata.Frequency);
#if !AUDIO_TOOL_NOAUDIO
            if (EditorAudioUtility.IsPreviewClipPlaying() && EditorAudioUtility.LastPlayedPreviewClip != null)
            {
                return EditorAudioUtility.GetPreviewClipPosition();
            }
#endif
            return 0f;
        }

        /// <summary>
        /// Gets the current playback position in samples.
        /// </summary>
        /// <returns>Current sample position, or 0 if not playing</returns>
        public static int GetCurrentSamplePosition()
        {
            if (_currentPlayback != null)
                return (int)Math.Min(int.MaxValue, _currentPlayback.PositionFrames);
#if !AUDIO_TOOL_NOAUDIO
            if (EditorAudioUtility.IsPreviewClipPlaying() && EditorAudioUtility.LastPlayedPreviewClip != null)
            {
                AudioClip clip = EditorAudioUtility.LastPlayedPreviewClip;
                float position = EditorAudioUtility.GetPreviewClipPosition();
                return Mathf.RoundToInt(position * clip.frequency);
            }
#endif
            return 0;
        }

        /// <summary>
        /// Checks if the playback has reached the end of the specified range.
        /// </summary>
        /// <returns>True if range playback has completed</returns>
        public static bool HasReachedRangeEnd()
        {
            if (_currentPlayback != null)
                return _isRangePlaying && !_currentLoop &&
                    (!_currentPlayback.IsPlaying || _currentPlayback.PositionFrames >= _rangeEndSample);
#if !AUDIO_TOOL_NOAUDIO
            if (!_isRangePlaying) return false;
            if (!EditorAudioUtility.IsPreviewClipPlaying()) return true;

            int currentSample = GetCurrentSamplePosition();
            return currentSample >= _rangeEndSample;
#else
            return true;
#endif
        }

        /// <summary>
        /// Checks if audio is currently playing.
        /// </summary>
        /// <returns>True if audio is playing</returns>
        public static bool IsPlaying()
        {
            if (_currentPlayback != null) return _currentPlayback.IsPlaying;
#if !AUDIO_TOOL_NOAUDIO
            return EditorAudioUtility.IsPreviewClipPlaying();
#else
            return false;
#endif
        }

        /// <summary>
        /// Pauses the currently playing audio.
        /// </summary>
        public static void PauseAudio()
        {
            if (_currentPlayback != null)
            {
                _currentPlayback.Pause();
                return;
            }
#if !AUDIO_TOOL_NOAUDIO
            if (EditorAudioUtility.LastPlayedPreviewClip != null)
            {
                EditorAudioUtility.PausePreviewClip(EditorAudioUtility.LastPlayedPreviewClip);
            }
#endif
        }

        /// <summary>
        /// Resumes paused audio.
        /// </summary>
        public static void ResumeAudio()
        {
            if (_currentPlayback != null)
            {
                _currentPlayback.Resume();
                return;
            }
#if !AUDIO_TOOL_NOAUDIO
            if (EditorAudioUtility.LastPlayedPreviewClip != null)
            {
                EditorAudioUtility.ResumePreviewClip(EditorAudioUtility.LastPlayedPreviewClip);
            }
#endif
        }

        /// <summary>
        /// Stops audio playback.
        /// </summary>
        public static void StopAudio()
        {
            _requestVersion++;
            if (_currentPlayback != null)
            {
                _currentPlayback.Dispose();
                _currentPlayback = null;
            }
#if !AUDIO_TOOL_NOAUDIO
            EditorAudioUtility.StopAllPreviewClips();
#endif
            _currentClip = null;
            _currentLoop = false;
            _rangeStartSample = 0;
            _rangeEndSample = 0;
            _isRangePlaying = false;
        }
    }
}
