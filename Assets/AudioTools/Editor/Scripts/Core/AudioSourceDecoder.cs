using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioTool
{
    /// <summary>A file the editor could not decode; the message is ready for the window's error state.</summary>
    internal sealed class AudioDecodeException : Exception
    {
        internal AudioDecodeException(string message) : base(message) { }
    }

    internal sealed class AudioDecodedSource
    {
        internal AudioDecodedSource(float[] samples, EditorAudioMetadata metadata, AudioClip clip)
        {
            Samples = samples;
            Metadata = metadata;
            Clip = clip;
        }

        internal float[] Samples { get; }
        internal EditorAudioMetadata Metadata { get; }

        /// <summary>Unity preview clip when requested and Unity audio is enabled; the caller owns and destroys it.</summary>
        internal AudioClip Clip { get; }
    }

    /// <summary>Decodes a source file the same way for the Audio Editor and batch saves, including MP3 gapless trimming on both backends.</summary>
    internal static class AudioSourceDecoder
    {
        /// <param name="path">Materialized source file.</param>
        /// <param name="keepClip">Returns a Unity clip of the decoded audio for preview playback.</param>
        /// <param name="gaplessClipPath">Temporary WAV used to rebuild that clip after MP3 gapless trimming.</param>
        /// <param name="token">Cancels decoding.</param>
        internal static async Task<AudioDecodedSource> DecodeAsync(string path, bool keepClip, string gaplessClipPath, CancellationToken token)
        {
            if (CommonEditorAudioSettings.IsDisabled)
            {
                IEditorAudioBackend backend = EditorAudioService.GetBackend();
                EditorAudioSamples decoded = await backend.DecodeAsync(path, -1, token);
                token.ThrowIfCancellationRequested();
                return new AudioDecodedSource(decoded.Samples, decoded.Metadata, null);
            }

            AudioClip clip = TakeOwnership(await AudioManager.LoadAudioFromFile(path, false));
            try
            {
                token.ThrowIfCancellationRequested();
                if (clip == null || !clip.LoadAudioData())
                    throw new AudioDecodeException("Unity could not decode this audio file. Try WAV, MP3, OGG, AIFF, AIF, or a Unity-supported FLAC file.");

                float[] samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) throw new AudioDecodeException("Unity decoded the file but could not read its audio samples.");

                EditorAudioMetadata metadata = new EditorAudioMetadata(clip.channels, clip.frequency, clip.samples);
                float[] gapless = Mp3GaplessInfo.TrimDecodedSamples(path, samples, clip.channels);
                if (gapless != samples)
                {
                    metadata = new EditorAudioMetadata(clip.channels, clip.frequency, gapless.Length / clip.channels);
                    samples = gapless;
                    if (keepClip)
                    {
                        AudioClip gaplessClip = await CreateClipAsync(gapless, clip.channels, clip.frequency, gaplessClipPath);
                        Object.DestroyImmediate(clip);
                        clip = gaplessClip;
                        token.ThrowIfCancellationRequested();
                        if (clip == null) throw new AudioDecodeException("Unity decoded the file but could not prepare its audio samples.");
                    }
                }

                if (!keepClip) return new AudioDecodedSource(samples, metadata, null);
                AudioDecodedSource result = new AudioDecodedSource(samples, metadata, clip);
                clip = null;
                return result;
            }
            finally
            {
                if (clip != null) Object.DestroyImmediate(clip);
            }
        }

        // Opening a scene unloads runtime clips that only managed fields reference, which would empty the editor.
        internal static AudioClip TakeOwnership(AudioClip clip)
        {
            if (clip != null) clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        private static async Task<AudioClip> CreateClipAsync(float[] samples, int channels, int frequency, string path)
        {
            string tempPath = string.IsNullOrEmpty(path) ? Path.Combine(Application.temporaryCachePath, "AudioToolSource-" + Guid.NewGuid().ToString("N") + ".wav") : path;
            try
            {
                return TakeOwnership(await AudioProcessor.CreateClipFromSamples(samples, channels, frequency, "AudioToolsSource", tempPath));
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch (IOException) { }
            }
        }
    }
}
