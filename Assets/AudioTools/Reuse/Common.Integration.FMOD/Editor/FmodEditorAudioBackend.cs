using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common.Integration.FMOD
{
    /// <summary>Optional editor playback and decoding through the installed FMOD Core API.</summary>
    internal sealed class FmodEditorAudioBackend : IEditorAudioBackend, IDisposable
    {
        private readonly SynchronizationContext _editorContext;
        private readonly int _editorThreadId;
        private readonly object _decodeSync = new object();
        private readonly object _playSync = new object();
        private readonly HashSet<FmodPlayback> _playbacks = new HashSet<FmodPlayback>();
        private FmodSystem _decodeSystem;
        private FmodSystem _playSystem;
        private bool _decodeSuspended;
        private bool _playSuspended;
        private bool _updating;
        private bool _disposed;
        private int _generation;
        private int _pendingOpens;
        private EditorAudioUnavailableException _decodeFailure;
        private EditorAudioUnavailableException _playFailure;

        internal FmodEditorAudioBackend()
        {
            _editorContext = SynchronizationContext.Current;
            _editorThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_editorContext == null)
                throw new EditorAudioUnavailableException("The Unity editor audio context is unavailable. Retry from an editor audio control.");
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public string Id => "FMOD";

        public Task<EditorAudioMetadata> ReadMetadataAsync(string path, CancellationToken token)
        {
            path = GetValidatedPath(path);
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_decodeSync)
                {
                    ThrowIfDisposed();
                    FmodSystem system = GetDecodeSystem();
                    IntPtr sound = FmodApi.OpenFile(system, path, false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        EditorAudioMetadata raw = FmodApi.Metadata(sound, out FmodSampleFormat format);
                        EditorMp3GaplessInfo.Range? gapless = ResolveGapless(path, sound, raw, format, token);
                        return gapless.HasValue ? LogicalMetadata(raw, gapless.Value) : raw;
                    }
                    finally
                    {
                        FmodApi.ReleaseSound(sound);
                    }
                }
            }, token);
        }

        public Task<EditorAudioSamples> DecodeAsync(string path, long maxFrames, CancellationToken token)
        {
            path = GetValidatedPath(path);
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_decodeSync)
                {
                    ThrowIfDisposed();
                    FmodSystem system = GetDecodeSystem();
                    IntPtr sound = FmodApi.OpenFile(system, path, false);
                    try
                    {
                        EditorAudioMetadata metadata = FmodApi.Metadata(sound, out FmodSampleFormat format);
                        EditorMp3GaplessInfo.Range? gapless = ResolveGapless(path, sound, metadata, format, token);
                        long logicalFrames = gapless.HasValue ? gapless.Value.FrameCount : metadata.Frames;
                        if (maxFrames >= 0 && logicalFrames > maxFrames)
                            throw new InvalidDataException("The decoded audio exceeds the requested sample limit.");
                        long sampleCount = checked(metadata.Frames * metadata.Channels);
                        if (sampleCount > int.MaxValue)
                            throw new InvalidDataException("This audio file is too large to hold as editable samples.");
                        int bytesPerSample = BytesPerSample(format);
                        if (!BitConverter.IsLittleEndian)
                            throw new EditorAudioUnavailableException("This FMOD audio decoder requires a little-endian editor platform.");
                        int frameBytes = checked(bytesPerSample * metadata.Channels);
                        int blockFrames = Math.Max(1, 65536 / frameBytes);
                        byte[] buffer = new byte[checked(blockFrames * frameBytes)];
                        float[] samples = new float[checked((int)sampleCount)];
                        long framesRead = 0;
                        while (framesRead < metadata.Frames)
                        {
                            token.ThrowIfCancellationRequested();
                            ThrowIfDisposed();
                            int remainingFrames = checked((int)Math.Min(blockFrames, metadata.Frames - framesRead));
                            if (buffer.Length != remainingFrames * frameBytes)
                                buffer = new byte[remainingFrames * frameBytes];
                            FmodResult result = FmodApi.Read(sound, buffer, out uint bytesRead);
                            if (bytesRead == 0) break;
                            if (bytesRead % frameBytes != 0)
                                throw new InvalidDataException("FMOD returned an incomplete audio sample frame.");
                            int blockSampleCount = checked((int)(bytesRead / bytesPerSample));
                            int destination = checked((int)(framesRead * metadata.Channels));
                            for (int i = 0; i < blockSampleCount; i++)
                            {
                                float value = ConvertSample(buffer, i * bytesPerSample, format);
                                if (float.IsNaN(value) || float.IsInfinity(value))
                                    throw new InvalidDataException("FMOD returned a non-finite audio sample.");
                                samples[destination + i] = value;
                            }
                            framesRead += bytesRead / (uint)frameBytes;
                            if (result == FmodResult.ERR_FILE_EOF) break;
                        }
                        if (framesRead == 0 && metadata.Frames != 0)
                            throw new InvalidDataException("FMOD could not decode audio samples from this file.");
                        if (framesRead != metadata.Frames)
                        {
                            Array.Resize(ref samples, checked((int)(framesRead * metadata.Channels)));
                            metadata = new EditorAudioMetadata(metadata.Channels, metadata.Frequency, framesRead);
                        }
                        if (gapless.HasValue &&
                            (long)gapless.Value.StartFrame + gapless.Value.FrameCount <= metadata.Frames)
                        {
                            float[] trimmed = new float[checked(gapless.Value.FrameCount * metadata.Channels)];
                            Array.Copy(samples, checked(gapless.Value.StartFrame * metadata.Channels),
                                trimmed, 0, trimmed.Length);
                            samples = trimmed;
                            metadata = LogicalMetadata(metadata, gapless.Value);
                        }
                        if (maxFrames >= 0 && metadata.Frames > maxFrames)
                            throw new InvalidDataException("The decoded audio exceeds the requested sample limit.");
                        return new EditorAudioSamples(metadata, samples);
                    }
                    finally
                    {
                        FmodApi.ReleaseSound(sound);
                    }
                }
            }, token);
        }

        public Task<IEditorAudioPlayback> PlayFileAsync(string path, long startFrame, long endFrameExclusive,
            bool loop, CancellationToken token)
        {
            path = GetValidatedPath(path);
            int generation = Volatile.Read(ref _generation);
            return Task.Run<IEditorAudioPlayback>(() =>
            {
                IntPtr sound = IntPtr.Zero;
                lock (_playSync)
                {
                    ThrowIfInactive(generation, token);
                    FmodSystem system = GetPlaySystem();
                    ResumePlaySystem();
                    _pendingOpens++;
                    try
                    {
                        sound = FmodApi.OpenFile(system, path, true);
                    }
                    catch
                    {
                        _pendingOpens--;
                        SuspendIfIdle();
                        throw;
                    }
                }
                try
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                    while (true)
                    {
                        ThrowIfInactive(generation, token);
                        FmodOpenState state;
                        lock (_playSync)
                        {
                            FmodApi.Update(_playSystem);
                            state = FmodApi.OpenState(sound, path);
                        }
                        if (state == FmodOpenState.Ready) break;
                        if (state == FmodOpenState.Error) throw new InvalidDataException("FMOD could not open " + Path.GetFileName(path) + ".");
                        if (DateTime.UtcNow >= deadline) throw new IOException("FMOD timed out opening " + Path.GetFileName(path) + ".");
                        Thread.Sleep(10);
                    }
                    EditorAudioMetadata raw;
                    lock (_playSync)
                    {
                        ThrowIfInactive(generation, token);
                        raw = FmodApi.Metadata(sound, out _);
                    }
                    EditorMp3GaplessInfo.Range? gapless = ResolveGaplessFromFile(path, raw, token);
                    long nativeOffset = gapless.HasValue ? gapless.Value.StartFrame : 0;
                    EditorAudioMetadata metadata = gapless.HasValue ? LogicalMetadata(raw, gapless.Value) : raw;
                    NormalizeRange(metadata, ref startFrame, ref endFrameExclusive);
                    lock (_playSync)
                    {
                        ThrowIfInactive(generation, token);
                        IntPtr channel = FmodApi.Play(_playSystem, sound,
                            checked(startFrame + nativeOffset), checked(endFrameExclusive + nativeOffset), loop);
                        FmodPlayback playback = new FmodPlayback(this, sound, channel, metadata,
                            startFrame, endFrameExclusive, loop, nativeOffset);
                        sound = IntPtr.Zero;
                        _playbacks.Add(playback);
                        EnsureUpdating();
                        return playback;
                    }
                }
                finally
                {
                    lock (_playSync)
                    {
                        if (sound != IntPtr.Zero) FmodApi.ReleaseSound(sound);
                        _pendingOpens--;
                        SuspendIfIdle();
                        if ((_disposed || generation != _generation) && _pendingOpens == 0 && _playbacks.Count == 0)
                            ReleasePlaySystem();
                    }
                }
            }, token);
        }

        public Task<IEditorAudioPlayback> PlaySamplesAsync(float[] samples, int channels, int frequency,
            long startFrame, long endFrameExclusive, bool loop, CancellationToken token)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels < 1 || frequency < 1 || samples.Length == 0 || samples.Length % channels != 0)
                throw new ArgumentException("Processed audio must contain complete sample frames.");
            EditorAudioMetadata metadata = new EditorAudioMetadata(channels, frequency, samples.LongLength / channels);
            NormalizeRange(metadata, ref startFrame, ref endFrameExclusive);
            if (!BitConverter.IsLittleEndian)
                throw new EditorAudioUnavailableException("FMOD processed playback requires a little-endian editor platform.");
            int generation = Volatile.Read(ref _generation);
            return Task.Run<IEditorAudioPlayback>(() =>
            {
                ThrowIfInactive(generation, token);
                byte[] pcm = new byte[checked(samples.Length * sizeof(float))];
                Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);
                token.ThrowIfCancellationRequested();
                lock (_playSync)
                {
                    ThrowIfInactive(generation, token);
                    FmodSystem system = GetPlaySystem();
                    ResumePlaySystem();
                    IntPtr sound = FmodApi.OpenFloatSamples(system, pcm, channels, frequency);
                    try
                    {
                        IntPtr channel = FmodApi.Play(system, sound, startFrame, endFrameExclusive, loop);
                        FmodPlayback playback = new FmodPlayback(this, sound, channel, metadata,
                            startFrame, endFrameExclusive, loop, 0);
                        _playbacks.Add(playback);
                        EnsureUpdating();
                        return playback;
                    }
                    catch
                    {
                        FmodApi.ReleaseSound(sound);
                        SuspendIfIdle();
                        throw;
                    }
                }
            }, token);
        }

        private static string GetValidatedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An audio file path is required.", nameof(path));
            // FMOD opens Windows extended-length paths with native separators.
            string fullPath = IOUtils.ToLongPath(Path.GetFullPath(path));
            if (!File.Exists(fullPath)) throw new FileNotFoundException("The audio file is unavailable.", path);
            return fullPath;
        }

        private static EditorAudioMetadata LogicalMetadata(EditorAudioMetadata raw, EditorMp3GaplessInfo.Range range) =>
            new EditorAudioMetadata(raw.Channels, raw.Frequency, range.FrameCount);

        private EditorMp3GaplessInfo.Range? ResolveGaplessFromFile(string path, EditorAudioMetadata raw,
            CancellationToken token)
        {
            if (!EditorMp3GaplessInfo.TryRead(path, out _)) return null;
            lock (_decodeSync)
            {
                token.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                FmodSystem system = GetDecodeSystem();
                IntPtr sound = FmodApi.OpenFile(system, path, false);
                try
                {
                    EditorAudioMetadata probe = FmodApi.Metadata(sound, out FmodSampleFormat format);
                    if (probe.Channels != raw.Channels || probe.Frequency != raw.Frequency || probe.Frames != raw.Frames)
                        return null;
                    return ResolveGapless(path, sound, probe, format, token);
                }
                finally
                {
                    FmodApi.ReleaseSound(sound);
                }
            }
        }

        private EditorMp3GaplessInfo.Range? ResolveGapless(string path, IntPtr sound, EditorAudioMetadata raw,
            FmodSampleFormat format, CancellationToken token)
        {
            if (!EditorMp3GaplessInfo.TryRead(path, out EditorMp3GaplessInfo.Range candidate)) return null;
            if (candidate.InfoFrameSamples < 1 || candidate.StartFrame < candidate.InfoFrameSamples ||
                candidate.FrameCount < 1 || (long)candidate.StartFrame + candidate.FrameCount > raw.Frames ||
                candidate.InfoFrameSamples > raw.Frames)
                return null;

            int bytesPerSample = BytesPerSample(format);
            int sampleCount = checked(candidate.InfoFrameSamples * raw.Channels);
            byte[] buffer = new byte[checked(sampleCount * bytesPerSample)];
            token.ThrowIfCancellationRequested();
            uint bytesRead;
            try
            {
                FmodApi.Read(sound, buffer, out bytesRead);
            }
            finally
            {
                FmodApi.SeekData(sound, 0);
            }
            if (bytesRead != buffer.Length) return null;
            for (int i = 0; i < sampleCount; i++)
            {
                if (ConvertSample(buffer, i * bytesPerSample, format) != 0f) return null;
            }
            return candidate;
        }

        private static void NormalizeRange(EditorAudioMetadata metadata, ref long start, ref long end)
        {
            if (metadata.Frames < 1) throw new InvalidDataException("This audio file contains no playable samples.");
            start = Math.Max(0, Math.Min(start, metadata.Frames - 1));
            end = end <= 0 ? metadata.Frames : Math.Min(end, metadata.Frames);
            if (end <= start) throw new ArgumentOutOfRangeException(nameof(end), "The playback range is empty.");
            if (end > uint.MaxValue || start > uint.MaxValue)
                throw new InvalidDataException("This audio file exceeds FMOD's PCM-frame playback range.");
        }

        private static int BytesPerSample(FmodSampleFormat format)
        {
            switch (format)
            {
                case FmodSampleFormat.Pcm8: return 1;
                case FmodSampleFormat.Pcm16: return 2;
                case FmodSampleFormat.Pcm24: return 3;
                case FmodSampleFormat.Pcm32:
                case FmodSampleFormat.PcmFloat: return 4;
                default: throw new InvalidDataException("FMOD decoded to unsupported sample format " + format + ".");
            }
        }

        private static float ConvertSample(byte[] buffer, int offset, FmodSampleFormat format)
        {
            switch (format)
            {
                case FmodSampleFormat.Pcm8: return unchecked((sbyte)buffer[offset]) / 128f;
                case FmodSampleFormat.Pcm16:
                    return unchecked((short)(buffer[offset] | buffer[offset + 1] << 8)) / 32768f;
                case FmodSampleFormat.Pcm24:
                    int value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
                    if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
                    return value / 8388608f;
                case FmodSampleFormat.Pcm32:
                    return (buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 |
                        buffer[offset + 3] << 24) / 2147483648f;
                case FmodSampleFormat.PcmFloat: return BitConverter.ToSingle(buffer, offset);
                default: throw new InvalidDataException("Unsupported FMOD PCM format " + format + ".");
            }
        }

        private FmodSystem GetDecodeSystem()
        {
            if (_decodeFailure != null) throw _decodeFailure;
            if (!_decodeSystem.IsValid)
            {
                try
                {
                    _decodeSystem = FmodApi.CreateSystem(true);
                    FmodApi.Suspend(_decodeSystem);
                    _decodeSuspended = true;
                }
                catch (Exception exception)
                {
                    if (_decodeSystem.IsValid) FmodApi.ReleaseSystem(_decodeSystem);
                    _decodeSystem = default;
                    _decodeFailure = exception as EditorAudioUnavailableException ??
                        new EditorAudioUnavailableException("FMOD offline decoding could not be initialized. Check the FMOD installation, then retry.", exception);
                    throw _decodeFailure;
                }
            }
            return _decodeSystem;
        }

        private FmodSystem GetPlaySystem()
        {
            if (_playFailure != null) throw _playFailure;
            if (!_playSystem.IsValid)
            {
                try
                {
                    _playSystem = FmodApi.CreateSystem(false);
                    FmodApi.Suspend(_playSystem);
                    _playSuspended = true;
                }
                catch (Exception exception)
                {
                    if (_playSystem.IsValid) FmodApi.ReleaseSystem(_playSystem);
                    _playSystem = default;
                    _playFailure = exception as EditorAudioUnavailableException ??
                        new EditorAudioUnavailableException("FMOD playback could not be initialized. Check the audio device and FMOD installation, then retry.", exception);
                    throw _playFailure;
                }
            }
            return _playSystem;
        }

        private void ResumePlaySystem()
        {
            if (_playSystem.IsValid && _playSuspended)
            {
                FmodApi.Resume(_playSystem);
                _playSuspended = false;
            }
        }

        private void SuspendIfIdle()
        {
            if (!_playSystem.IsValid || _playSuspended || _playbacks.Count != 0 || _pendingOpens != 0) return;
            StopUpdating();
            FmodApi.Suspend(_playSystem);
            _playSuspended = true;
        }

        private void EnsureUpdating()
        {
            if (Thread.CurrentThread.ManagedThreadId != _editorThreadId)
            {
                if (_editorContext == null)
                    throw new EditorAudioUnavailableException("The Unity editor audio update context is unavailable.");
                _editorContext.Post(_ =>
                {
                    lock (_playSync)
                    {
                        if (!_disposed && _playbacks.Count != 0) EnsureUpdating();
                    }
                }, null);
                return;
            }
            if (_updating) return;
            EditorApplication.update += Update;
            _updating = true;
        }

        private void StopUpdating()
        {
            if (Thread.CurrentThread.ManagedThreadId != _editorThreadId)
            {
                _editorContext?.Post(_ =>
                {
                    lock (_playSync)
                    {
                        if (_playbacks.Count == 0) StopUpdating();
                    }
                }, null);
                return;
            }
            if (!_updating) return;
            EditorApplication.update -= Update;
            _updating = false;
        }

        private void Update()
        {
            lock (_playSync)
            {
                if (!_playSystem.IsValid || _disposed) return;
                try
                {
                    FmodApi.Update(_playSystem);
                    foreach (FmodPlayback playback in new List<FmodPlayback>(_playbacks))
                        playback.CheckEnd();
                }
                catch (Exception exception)
                {
                    Debug.LogError("FMOD preview stopped after an audio device error: " + exception.Message);
                    StopAll();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FmodEditorAudioBackend));
        }

        private void ThrowIfInactive(int generation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (generation != Volatile.Read(ref _generation)) throw new OperationCanceledException("The editor audio session changed.");
        }

        internal bool Playing(IntPtr channel)
        {
            lock (_playSync) return !_disposed && FmodApi.IsPlaying(channel);
        }

        internal long Position(IntPtr channel)
        {
            lock (_playSync) return _disposed ? 0 : FmodApi.Position(channel);
        }

        internal void Seek(IntPtr channel, long frame)
        {
            lock (_playSync)
            {
                ThrowIfDisposed();
                FmodApi.Seek(channel, frame);
            }
        }

        internal void Pause(IntPtr channel, bool paused)
        {
            lock (_playSync)
            {
                ThrowIfDisposed();
                FmodApi.Pause(channel, paused);
            }
        }

        private void Stop(FmodPlayback playback, IntPtr channel, IntPtr sound)
        {
            lock (_playSync)
            {
                if (!_playbacks.Remove(playback)) return;
                try { FmodApi.Stop(channel); }
                finally
                {
                    FmodApi.ReleaseSound(sound);
                    SuspendIfIdle();
                }
            }
        }

        private void StopAll()
        {
            foreach (FmodPlayback playback in new List<FmodPlayback>(_playbacks))
                playback.Stop();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode && state != PlayModeStateChange.ExitingPlayMode) return;
            Interlocked.Increment(ref _generation);
            lock (_playSync)
            {
                StopAll();
                if (_pendingOpens == 0) ReleasePlaySystem();
            }
            lock (_decodeSync) ReleaseDecodeSystem();
        }

        private void ReleasePlaySystem()
        {
            StopUpdating();
            if (!_playSystem.IsValid) return;
            FmodApi.ReleaseSystem(_playSystem, true, _playSuspended);
            _playSystem = default;
            _playSuspended = false;
        }

        private void ReleaseDecodeSystem()
        {
            if (!_decodeSystem.IsValid) return;
            FmodApi.ReleaseSystem(_decodeSystem, true, _decodeSuspended);
            _decodeSystem = default;
            _decodeSuspended = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _generation);
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            lock (_playSync)
            {
                StopAll();
                if (_pendingOpens == 0) ReleasePlaySystem();
            }
            lock (_decodeSync) ReleaseDecodeSystem();
        }

        private sealed class FmodPlayback : IEditorAudioPlayback
        {
            private readonly FmodEditorAudioBackend _owner;
            private readonly IntPtr _sound;
            private readonly IntPtr _channel;
            private readonly long _endFrameExclusive;
            private readonly long _nativeOffset;
            private readonly bool _loop;
            private bool _stopped;
            private bool _paused;
            private long _lastPosition;

            internal FmodPlayback(FmodEditorAudioBackend owner, IntPtr sound, IntPtr channel,
                EditorAudioMetadata metadata, long startFrame, long endFrameExclusive, bool loop, long nativeOffset)
            {
                _owner = owner;
                _sound = sound;
                _channel = channel;
                Metadata = metadata;
                _lastPosition = startFrame;
                _endFrameExclusive = endFrameExclusive;
                _nativeOffset = nativeOffset;
                _loop = loop;
            }

            public EditorAudioMetadata Metadata { get; }

            public bool IsPlaying => !_stopped && !_paused && _owner.Playing(_channel);

            public long PositionFrames
            {
                get
                {
                    if (!_stopped) _lastPosition = Math.Max(0, Math.Min(Metadata.Frames, _owner.Position(_channel) - _nativeOffset));
                    return _lastPosition;
                }
            }

            public void Seek(long frame)
            {
                if (_stopped) return;
                long target = Math.Max(0, Math.Min(frame, Metadata.Frames - 1));
                _owner.Seek(_channel, checked(target + _nativeOffset));
                _lastPosition = target;
            }

            public void Pause()
            {
                if (_stopped || _paused) return;
                _owner.Pause(_channel, true);
                _paused = true;
            }

            public void Resume()
            {
                if (_stopped || !_paused) return;
                _owner.Pause(_channel, false);
                _paused = false;
            }

            internal void CheckEnd()
            {
                if (_stopped || _paused) return;
                if (!_owner.Playing(_channel))
                {
                    Stop();
                    return;
                }
                if (!_loop && PositionFrames >= _endFrameExclusive) Stop();
            }

            public void Stop()
            {
                if (_stopped) return;
                _lastPosition = Math.Max(0, Math.Min(Metadata.Frames, _owner.Position(_channel) - _nativeOffset));
                _stopped = true;
                _owner.Stop(this, _channel, _sound);
            }

            public void Dispose() => Stop();
        }
    }

    internal static class FmodEditorAudioRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorAudioService.RegisterFmod(() => new FmodEditorAudioBackend());
        }
    }
}
