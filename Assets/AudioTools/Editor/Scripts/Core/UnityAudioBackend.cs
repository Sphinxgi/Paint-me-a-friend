using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
#if !AUDIO_TOOL_NOAUDIO
using JD.EditorAudioUtils;
#endif

namespace AudioTool
{
    internal sealed class UnityAudioBackend : IEditorAudioBackend
    {
        public string Id => "Unity";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorAudioService.RegisterUnity(() => new UnityAudioBackend());
        }

        public async Task<EditorAudioMetadata> ReadMetadataAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            AudioHeaderReader.AudioHeaderInfo header = AudioHeaderReader.ReadHeader(path);
            if (header != null && header.Duration > 0f && header.Channels > 0 && header.SampleRate > 0)
                return new EditorAudioMetadata(header.Channels, header.SampleRate,
                    (long)Math.Round((double)header.Duration * header.SampleRate));

            AudioClip clip = await AudioManager.LoadAudioFromFile(path);
            try
            {
                token.ThrowIfCancellationRequested();
                if (clip == null) throw new InvalidDataException("Unity could not read the audio file.");
                return new EditorAudioMetadata(clip.channels, clip.frequency, clip.samples);
            }
            finally
            {
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        public async Task<EditorAudioSamples> DecodeAsync(string path, long maxFrames, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            AudioClip clip = await AudioManager.LoadAudioFromFile(path, false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (clip == null || !clip.LoadAudioData())
                    throw new InvalidDataException("Unity could not decode the audio file.");
                EditorAudioMetadata metadata = new EditorAudioMetadata(clip.channels, clip.frequency, clip.samples);
                if (maxFrames >= 0 && metadata.Frames > maxFrames)
                    throw new InvalidDataException("The decoded audio exceeds the permitted duration.");
                int count = checked(clip.samples * clip.channels);
                float[] samples = new float[count];
                if (!clip.GetData(samples, 0))
                    throw new InvalidDataException("Unity could not read the audio samples.");
                token.ThrowIfCancellationRequested();
                return new EditorAudioSamples(metadata, samples);
            }
            finally
            {
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        public async Task<IEditorAudioPlayback> PlayFileAsync(string path, long startFrame, long endFrameExclusive, bool loop, CancellationToken token)
        {
#if !AUDIO_TOOL_NOAUDIO
            AudioClip clip = await AudioManager.LoadAudioFromFile(path);
            if (token.IsCancellationRequested)
            {
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                token.ThrowIfCancellationRequested();
            }
            if (clip == null) throw new InvalidDataException("Unity could not load the audio preview.");
            return Start(clip, startFrame, endFrameExclusive, loop);
#else
            throw new EditorAudioUnavailableException("Audio preview support is disabled for this installation.");
#endif
        }

        public async Task<IEditorAudioPlayback> PlaySamplesAsync(float[] samples, int channels, int frequency,
            long startFrame, long endFrameExclusive, bool loop, CancellationToken token)
        {
#if !AUDIO_TOOL_NOAUDIO
            string path = Path.Combine(Application.temporaryCachePath, "AudioToolPreview-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                AudioClip clip = await AudioProcessor.CreateClipFromSamples(samples, channels, frequency, "ProcessedAudio", path);
                if (token.IsCancellationRequested)
                {
                    if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
                    token.ThrowIfCancellationRequested();
                }
                if (clip == null) throw new InvalidDataException("Unity could not prepare the processed audio preview.");
                return Start(clip, startFrame, endFrameExclusive, loop);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
#else
            throw new EditorAudioUnavailableException("Audio preview support is disabled for this installation.");
#endif
        }

#if !AUDIO_TOOL_NOAUDIO
        private static IEditorAudioPlayback Start(AudioClip clip, long startFrame, long endFrameExclusive, bool loop)
        {
            clip.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                int start = (int)Math.Max(0, Math.Min(clip.samples - 1, startFrame));
                int end = (int)Math.Max(start + 1, Math.Min(clip.samples, endFrameExclusive));
                UnityAudioPlayback playback = new UnityAudioPlayback(clip, start, end, loop);
                playback.Seek(start);
                return playback;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(clip);
                throw;
            }
        }

        private sealed class UnityAudioPlayback : IEditorAudioPlayback
        {
            private AudioClip _clip;
            private readonly int _end;
            private readonly bool _loop;

            public UnityAudioPlayback(AudioClip clip, int start, int end, bool loop)
            {
                _clip = clip;
                _end = end;
                _loop = loop;
                Metadata = new EditorAudioMetadata(clip.channels, clip.frequency, clip.samples);
            }

            public EditorAudioMetadata Metadata { get; }
            public bool IsPlaying => _clip != null && EditorAudioUtility.LastPlayedPreviewClip == _clip &&
                EditorAudioUtility.IsPreviewClipPlaying() && PositionFrames < _end;

            public long PositionFrames => _clip == null ? 0L :
                (long)Math.Round(EditorAudioUtility.GetPreviewClipPosition() * _clip.frequency);

            public void Seek(long frame)
            {
                if (_clip == null) return;
                int start = (int)Math.Max(0, Math.Min(_clip.samples - 1, frame));
                EditorAudioUtility.StopAllPreviewClips();
                EditorAudioUtility.PlayPreviewClip(_clip, start, _loop && _end == _clip.samples);
            }

            public void Stop()
            {
                if (_clip != null && EditorAudioUtility.LastPlayedPreviewClip == _clip)
                    EditorAudioUtility.StopAllPreviewClips();
            }

            public void Pause()
            {
                if (_clip != null && EditorAudioUtility.LastPlayedPreviewClip == _clip)
                    EditorAudioUtility.PausePreviewClip(_clip);
            }

            public void Resume()
            {
                if (_clip != null && EditorAudioUtility.LastPlayedPreviewClip == _clip)
                    EditorAudioUtility.ResumePreviewClip(_clip);
            }

            public void Dispose()
            {
                Stop();
                if (_clip == null) return;
                UnityEngine.Object.DestroyImmediate(_clip);
                _clip = null;
            }
        }
#endif
    }
}
