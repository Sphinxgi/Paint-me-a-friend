using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>Audio dimensions use per-channel sample frames, not interleaved samples.</summary>
    public readonly struct EditorAudioMetadata
    {
        public int Channels { get; }
        public int Frequency { get; }
        public long Frames { get; }
        public double Duration => Frequency > 0 ? (double)Frames / Frequency : 0d;

        public EditorAudioMetadata(int channels, int frequency, long frames)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (frequency < 1) throw new ArgumentOutOfRangeException(nameof(frequency));
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            Channels = channels;
            Frequency = frequency;
            Frames = frames;
        }
    }

    /// <summary>Decoded, interleaved PCM with the dimensions of its source.</summary>
    public sealed class EditorAudioSamples
    {
        public EditorAudioMetadata Metadata { get; }
        public float[] Samples { get; }

        public EditorAudioSamples(EditorAudioMetadata metadata, float[] samples)
        {
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            if (samples.LongLength % metadata.Channels != 0 || samples.LongLength / metadata.Channels != metadata.Frames)
                throw new ArgumentException("Sample count does not match the audio dimensions.", nameof(samples));
            Metadata = metadata;
        }
    }

    public interface IEditorAudioPlayback : IDisposable
    {
        EditorAudioMetadata Metadata { get; }
        bool IsPlaying { get; }
        long PositionFrames { get; }
        void Seek(long frame);
        void Pause();
        void Resume();
        void Stop();
    }

    public interface IEditorAudioBackend
    {
        string Id { get; }
        Task<EditorAudioMetadata> ReadMetadataAsync(string path, CancellationToken token);
        Task<EditorAudioSamples> DecodeAsync(string path, long maxFrames, CancellationToken token);
        Task<IEditorAudioPlayback> PlayFileAsync(string path, long startFrame, long endFrameExclusive, bool loop, CancellationToken token);
        Task<IEditorAudioPlayback> PlaySamplesAsync(float[] samples, int channels, int frequency, long startFrame, long endFrameExclusive, bool loop, CancellationToken token);
    }

    public sealed class EditorAudioUnavailableException : InvalidOperationException
    {
        public EditorAudioUnavailableException(string message) : base(message) { }
        public EditorAudioUnavailableException(string message, Exception cause) : base(message, cause) { }
    }

    /// <summary>Selects an editor audio backend only when an audio operation is requested.</summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class EditorAudioService
    {
        private static Func<IEditorAudioBackend> _unityFactory;
        private static Func<IEditorAudioBackend> _fmodFactory;
        private static IEditorAudioBackend _unity;
        private static IEditorAudioBackend _fmod;
        private static EditorAudioUnavailableException _fmodFailure;
        private static bool _lastDisabled;
        private static string _lastWarning;

        public static void RegisterUnity(Func<IEditorAudioBackend> factory)
        {
            _unityFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            _unity = null;
        }

        public static void RegisterFmod(Func<IEditorAudioBackend> factory)
        {
            _fmodFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            _fmod = null;
            _fmodFailure = null;
        }

        /// <summary>Explicit user retry after an unavailable FMOD installation or device.</summary>
        public static void RetryFmod()
        {
            _fmodFailure = null;
            _lastWarning = null;
            IEditorAudioBackend previous = _fmod;
            _fmod = null;
            if (previous is IDisposable owned) owned.Dispose();
        }

        public static void WarnUnavailableOnce(EditorAudioUnavailableException failure)
        {
            if (failure == null || _lastWarning == failure.Message) return;
            _lastWarning = failure.Message;
            Debug.LogWarning(failure.Message);
        }

        public static IEditorAudioBackend GetBackend()
        {
            bool disabled = CommonEditorAudioSettings.IsDisabled;
            if (!disabled)
            {
                if (_lastDisabled)
                {
                    try { RetryFmod(); }
                    catch (Exception exception) { Debug.LogWarning("FMOD cleanup after enabling Unity audio failed: " + exception.Message); }
                }
                _lastDisabled = false;
                _lastWarning = null;
                if (_unity == null)
                {
                    if (_unityFactory == null)
                        throw new EditorAudioUnavailableException("The Unity audio preview backend is unavailable.");
                    _unity = _unityFactory();
                }
                return _unity;
            }

            if (!_lastDisabled) RetryFmod();
            _lastDisabled = true;
            if (_fmodFailure != null) throw _fmodFailure;
            if (_fmodFactory == null)
            {
                _fmodFailure = new EditorAudioUnavailableException(
                    "Unity audio is disabled and FMOD for Unity is not installed. Install FMOD for Unity or enable Unity audio in Project Settings > Audio, then retry.");
                throw _fmodFailure;
            }

            if (_fmod == null)
            {
                try
                {
                    _fmod = _fmodFactory();
                    if (_fmod == null) throw new InvalidOperationException("The FMOD backend could not be created.");
                }
                catch (Exception exception)
                {
                    _fmodFailure = new EditorAudioUnavailableException(
                        "Unity audio is disabled and FMOD could not be initialized. Check the FMOD installation and audio output, then retry.", exception);
                    throw _fmodFailure;
                }
            }
            return _fmod;
        }
    }
}
