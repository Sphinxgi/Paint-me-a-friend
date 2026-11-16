using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace AudioTool
{
    internal sealed class AudioEditorController : IDisposable
    {
        private readonly AudioEditSession _session;
        private readonly SemaphoreSlim _processingGate = new SemaphoreSlim(1, 1);
        private readonly List<AudioPlaybackSpan> _sliceMap = new List<AudioPlaybackSpan>();
        private CancellationTokenSource _loadCancellation;
        private int _loadVersion;
        private int _processVersion;
        private int _appliedVersion;
        private string _appliedKey;
        private AudioEditSettings _appliedSettings;
        private float[] _appliedSource;
        private AudioRenderResult _render;
        private AudioClip _sourceClip;
        private AudioClip _processedClip;
        private EditorAudioMetadata? _sourceMetadata;
        private int _playVersion;
        private bool _playPending;
        // Source frame where the playing slice starts, or -1 while a range of the full preview plays.
        private int _sliceOffset = -1;
        private bool _sliceUsesMap;
        private float[] _originalSamples;
        private readonly string _previewPath;
        private bool _disposed;

        internal event Action Changed;

        internal AudioEditorController(AudioEditSession session)
        {
            _session = session;
            PeakCache = new AudioWaveformPeakCache();
            _previewPath = Path.Combine(Application.temporaryCachePath, "AudioToolPreview-" + session.GetStableId() + ".wav");
        }

        internal AudioWaveformPeakCache PeakCache { get; private set; }
        internal AudioClip SourceClip => _sourceClip;
        internal EditorAudioMetadata? SourceMetadata => _sourceMetadata;
        internal int SourceFrameCount => _sourceMetadata.HasValue ? (int)Math.Min(int.MaxValue, _sourceMetadata.Value.Frames) : 0;
        internal bool HasAudio => !RequiresAudioSettings && _sourceMetadata.HasValue && _originalSamples != null;
        internal bool IsLoading { get; private set; }
        internal bool IsPlaying { get; private set; }
        internal bool RequiresAudioSettings { get; private set; }
        internal string Status { get; private set; }
        internal string Error { get; private set; }
        internal float SourcePeak { get; private set; }
        internal float OutputPeak { get; private set; }
        internal int ClippedSampleCount { get; private set; }
        internal AudioLoudness SourceLoudness { get; private set; } = AudioLoudness.Silent;

        /// <summary>Latest preview render: exported segments, removed pauses, loop tails and per-segment levels.</summary>
        internal AudioRenderResult Render => _render;

        /// <summary>True once after a save that earned the one-time rating request.</summary>
        internal bool RatePromptPending { get; set; }

        internal Task InitializeAsync(IAudioSource source, string exportFolder, bool playWhenOpened)
        {
            return LoadAsync(source, exportFolder, playWhenOpened, false);
        }

        /// <summary>Opens another file while keeping the current edit settings, for multi-file sessions.</summary>
        internal Task OpenKeepingEditsAsync(IAudioSource source, string exportFolder, bool playWhenOpened)
        {
            return LoadAsync(source, exportFolder, playWhenOpened, true);
        }

        private async Task LoadAsync(IAudioSource source, string exportFolder, bool playWhenOpened, bool keepEdits)
        {
            if (source == null)
            {
                ClearSource();
                return;
            }

            CancelPendingWork();
            int loadVersion = _loadVersion;
            Stop();
            DestroyClip(_sourceClip);
            _sourceClip = null;
            DestroyClip(_processedClip);
            _processedClip = null;
            _sourceMetadata = null;
            _originalSamples = null;
            _render = null;
            if (!keepEdits) _session.ResetEdits();
            _session.FileName = source.FileName;
            _session.IsInProject = source.IsInProject;
            _session.ProjectPath = source.ProjectPath;
            _session.ExportFolder = ResolveExportFolder(source, exportFolder);
            IsLoading = true;
            RequiresAudioSettings = false;
            Error = null;
            Status = "Loading audio file...";
            RaiseChanged();

            _loadCancellation = new CancellationTokenSource();
            CancellationToken token = _loadCancellation.Token;
            try
            {
                string path = await source.GetMaterializedPathAsync(token);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SetError("The audio source is no longer available. Choose the file again or retry the source operation.");
                    return;
                }

                _session.MaterializedPath = path;
                Status = "Decoding audio...";
                RaiseChanged();
                string gaplessClipPath = Path.Combine(Application.temporaryCachePath, "AudioToolSource-" + _session.GetStableId() + "-" + loadVersion + ".wav");
                AudioDecodedSource decoded = await AudioSourceDecoder.DecodeAsync(path, true, gaplessClipPath, token);
                if (token.IsCancellationRequested) DestroyClip(decoded.Clip);
                token.ThrowIfCancellationRequested();

                DestroyClip(_sourceClip);
                _sourceClip = decoded.Clip;
                _sourceMetadata = decoded.Metadata;
                _originalSamples = decoded.Samples;
                int channels = decoded.Metadata.Channels;
                int frequency = decoded.Metadata.Frequency;
                SourcePeak = AudioDsp.GetPeak(decoded.Samples, out int sourceClipped);
                OutputPeak = SourcePeak;
                ClippedSampleCount = sourceClipped;
                Status = "Building waveform...";
                RaiseChanged();

                // Edits are possible as soon as the samples exist, so the first render shares the gate with preview processing.
                await _processingGate.WaitAsync(token);
                try
                {
                    int version = _processVersion;
                    AudioEditSettings settings = _session.CreateSettings();
                    float[] samples = decoded.Samples;
                    AudioWaveformPeakCache peaks = new AudioWaveformPeakCache();
                    AudioRenderResult render = null;
                    AudioLoudness sourceLoudness = AudioLoudness.Silent;
                    await Task.Run(() =>
                    {
                        render = AudioEditPipeline.Render(samples, channels, frequency, settings, token);
                        sourceLoudness = AudioLoudnessMeter.Measure(samples, channels, frequency, 0, samples.Length / channels);
                        peaks.Build(render.Samples, channels);
                    }, token);
                    token.ThrowIfCancellationRequested();
                    PeakCache = peaks;
                    SourceLoudness = sourceLoudness;
                    // A change made during this render supersedes it; its queued update applies the current settings next.
                    if (version == _processVersion) await ApplyRenderAsync(render, settings, version, token);
                    token.ThrowIfCancellationRequested();
                }
                finally
                {
                    _processingGate.Release();
                }
                IsLoading = false;
                Status = "Ready";
                RaiseChanged();

                if (playWhenOpened) Play();
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && loadVersion == _loadVersion)
                {
                    IsLoading = false;
                    Status = HasAudio ? "Ready" : null;
                    RaiseChanged();
                }
            }
            catch (EditorAudioUnavailableException exception)
            {
                if (_disposed || loadVersion != _loadVersion) return;
                RequiresAudioSettings = true;
                SetError(exception.Message);
                EditorAudioService.WarnUnavailableOnce(exception);
            }
            catch (AudioDecodeException exception)
            {
                if (_disposed || loadVersion != _loadVersion) return;
                SetError(exception.Message);
            }
            catch (Exception exception)
            {
                if (_disposed || loadVersion != _loadVersion) return;
                SetError("Audio loading failed: " + exception.Message);
                Debug.LogError("Audio Tools load failed: " + exception);
            }
        }

        internal async Task RestoreAsync(bool playWhenOpened = false)
        {
            if (string.IsNullOrEmpty(_session.MaterializedPath) || !File.Exists(_session.MaterializedPath))
            {
                SetError("The previous source could not be restored. Choose the audio file again.");
                return;
            }

            // Loading renders with the restored settings, so the preview is current without another pass.
            await LoadAsync(new RestoredAudioSource(_session), _session.ExportFolder, playWhenOpened, true);
        }

        internal void ClearSource()
        {
            CancelPendingWork();
            Stop();
            DestroyClip(_sourceClip);
            DestroyClip(_processedClip);
            _sourceClip = null;
            _processedClip = null;
            _sourceMetadata = null;
            _originalSamples = null;
            _render = null;
            IsLoading = false;
            Error = null;
            Status = null;
            RequiresAudioSettings = false;
            SourcePeak = 0f;
            OutputPeak = 0f;
            ClippedSampleCount = 0;
            SourceLoudness = AudioLoudness.Silent;
            RaiseChanged();
        }

        internal async void ScheduleProcessing()
        {
            int version = ++_processVersion;
            try
            {
                await Task.Delay(150);
                if (_disposed || version != _processVersion) return;
                await ApplyEffectsAsync(version);
            }
            catch (Exception exception)
            {
                Error = "Preview processing failed: " + exception.Message;
                Debug.LogError("Audio Tools preview processing failed: " + exception);
                RaiseChanged();
            }
        }

        internal bool PreviewIsCurrent => HasAudio && _appliedVersion == _processVersion && ReferenceEquals(_appliedSource, _originalSamples)
                                          && _appliedKey != null && _appliedKey == _session.CreateSettings().CreateKey();

        internal void ScheduleProcessingIfStale()
        {
            if (!PreviewIsCurrent) ScheduleProcessing();
        }

        internal async Task ApplyEffectsNowAsync()
        {
            int version = ++_processVersion;
            await ApplyEffectsAsync(version);
        }

        private async Task ApplyEffectsAsync(int version)
        {
            if (!HasAudio || version != _processVersion) return;
            await _processingGate.WaitAsync();
            try
            {
                if (_disposed || !HasAudio || version != _processVersion) return;
                AudioEditSettings settings = _session.CreateSettings();
                int channels = _sourceMetadata.Value.Channels;
                int frequency = _sourceMetadata.Value.Frequency;
                float[] source = _originalSamples;
                Status = "Updating preview...";
                RaiseChanged();
                AudioRenderResult render = await Task.Run(() => AudioEditPipeline.Render(source, channels, frequency, settings));
                if (_disposed || version != _processVersion) return;

                // A new cache keeps the drawn one intact while the worker thread builds.
                AudioWaveformPeakCache peaks = new AudioWaveformPeakCache();
                await Task.Run(() => peaks.Build(render.Samples, channels));
                if (_disposed || version != _processVersion) return;
                if (!await ApplyRenderAsync(render, settings, version, CancellationToken.None)) return;
                PeakCache = peaks;

                Status = "Ready";
                Error = null;
                if (IsPlaying)
                {
                    Stop();
                    Play();
                }

                RaiseChanged();
            }
            finally
            {
                _processingGate.Release();
            }
        }

        private async Task<bool> ApplyRenderAsync(AudioRenderResult render, AudioEditSettings settings, int version, CancellationToken token)
        {
            AudioClip clip = null;
            if (settings.ChangesSamples && _sourceClip != null)
            {
                clip = AudioSourceDecoder.TakeOwnership(await AudioProcessor.CreateClipFromSamples(render.Samples, render.Channels, render.Frequency, "AudioToolsPreview", _previewPath));
                if (_disposed || version != _processVersion || token.IsCancellationRequested)
                {
                    DestroyClip(clip);
                    return false;
                }
            }

            // Swapped only after the version check, so a superseded render never removes the current preview.
            DestroyClip(_processedClip);
            _processedClip = clip;
            _render = render;
            OutputPeak = render.Peak;
            ClippedSampleCount = render.ClippedSampleCount;
            _appliedSettings = settings;
            _appliedKey = settings.CreateKey();
            _appliedSource = _originalSamples;
            _appliedVersion = version;
            return true;
        }

        internal void TogglePlayback()
        {
            if (IsPlaying) Stop();
            else Play();
        }

        internal void Play()
        {
            if (!HasAudio) return;
            int version = ++_playVersion;
            _sliceOffset = -1;
            _sliceUsesMap = false;
            _sliceMap.Clear();
            float[] slice = CreatePlaybackSlice(out int sliceOffset);
            if (slice != null || _sourceClip == null)
            {
                _playPending = true;
                IsPlaying = true;
                _ = PlaySamplesAsync(version, slice, sliceOffset);
                RaiseChanged();
                return;
            }

            AudioClip clip = _processedClip != null ? _processedClip : _sourceClip;
            if (clip == null) return;

            IsPlaying = true;
            if (PlaysRegion)
            {
                GetRegionFrames(out int start, out int end);
                AudioManager.PlayClipRange(clip, start, end, _session.Loop);
            }
            else
            {
                int start = Mathf.RoundToInt(_session.Playhead * clip.samples);
                AudioManager.PlayClip(clip, start, _session.Loop);
            }

            RaiseChanged();
        }

        // Region playback with Loop, shortened pauses or a seamless loop plays its exact audio on its own:
        // both backends then loop natively and stop sample-accurately instead of restarting from a UI poll.
        private float[] CreatePlaybackSlice(out int offset)
        {
            offset = 0;
            if (!PlaysRegion || _appliedSettings == null) return null;
            int channels = _render.Channels;
            AudioRenderSegment first = _render.Segments[0];
            if (_appliedSettings.ShortensPauses && first.Pauses.Count > 0)
            {
                offset = first.Start;
                _sliceUsesMap = true;
                return AudioEditPipeline.RenderShortenedPreview(_render, _appliedSettings, 0, _sliceMap);
            }

            if (!_session.Loop) return null;
            int start = _appliedSettings.SeamlessLoop ? first.Start : _render.ExportStart;
            int end = _appliedSettings.SeamlessLoop ? first.ExportEnd : _render.ExportEnd;
            if (end <= start) return null;
            float[] slice = new float[(end - start) * channels];
            Array.Copy(_render.Samples, start * channels, slice, 0, slice.Length);
            offset = start;
            return slice;
        }

        private bool PlaysRegion => _session.PlaySelection && _session.HasEffectiveRegion && _render != null && _render.Segments.Count > 0;

        private void GetRegionFrames(out int start, out int end)
        {
            start = _render.ExportStart;
            end = Math.Max(start + 1, _render.ExportEnd);
        }

        private async Task PlaySamplesAsync(int version, float[] slice, int sliceOffset)
        {
            try
            {
                EditorAudioMetadata metadata = _sourceMetadata.Value;
                float[] samples;
                long start;
                long end;
                if (slice != null)
                {
                    samples = slice;
                    start = 0;
                    end = slice.Length / metadata.Channels;
                }
                else
                {
                    samples = _render != null ? _render.Samples : _originalSamples;
                    int frames = SourceFrameCount;
                    if (PlaysRegion)
                    {
                        GetRegionFrames(out int regionStart, out int regionEnd);
                        start = regionStart;
                        end = regionEnd;
                    }
                    else
                    {
                        start = Mathf.RoundToInt(_session.Playhead * frames);
                        end = frames;
                    }
                }

                bool started = await AudioManager.PlaySamplesAsync(samples, metadata.Channels, metadata.Frequency, start, end, _session.Loop);
                if (_disposed || version != _playVersion) return;
                _playPending = false;
                IsPlaying = started;
                if (started && slice != null) _sliceOffset = sliceOffset;
                if (!started)
                {
                    Error = _sourceClip == null
                        ? "Audio preview could not start. Check the FMOD installation and audio output, then retry."
                        : "Audio preview could not start. Check the audio output, then retry.";
                }

                RaiseChanged();
            }
            catch (Exception exception)
            {
                if (_disposed || version != _playVersion) return;
                _playPending = false;
                IsPlaying = false;
                Error = "Audio preview failed: " + exception.Message;
                RaiseChanged();
            }
        }

        internal void Stop()
        {
            _playVersion++;
            _playPending = false;
            if (IsPlaying)
            {
                float position = GetNormalizedPlaybackPosition();
                if (position >= 0f) _session.Playhead = position;
            }

            IsPlaying = false;
            _sliceOffset = -1;
            AudioManager.StopAudio();
            RaiseChanged();
        }

        internal void Seek(float normalized)
        {
            _session.Playhead = normalized;
            if (IsPlaying)
            {
                Stop();
                Play();
            }

            RaiseChanged();
        }

        internal void TickPlayback()
        {
            if (!IsPlaying) return;
            if (_playPending) return;

            float position = GetNormalizedPlaybackPosition();
            if (position >= 0f) _session.Playhead = position;
            if (_sliceOffset < 0 && PlaysRegion && AudioManager.IsRangePlaying && AudioManager.HasReachedRangeEnd())
            {
                if (_session.Loop) Play();
                else Stop();
            }
            else if (!AudioManager.IsPlaying())
            {
                if (_session.Loop) Play();
                else IsPlaying = false;
            }

            RaiseChanged();
        }

        internal bool SelectAudibleContent(out string message)
        {
            message = null;
            if (!HasAudio) return false;

            int frames = SourceFrameCount;
            int channels = _sourceMetadata.Value.Channels;
            int rangeStart = _session.HasSelection ? Mathf.RoundToInt(_session.SelectionStart * frames) : 0;
            int rangeEnd = _session.HasSelection ? Mathf.RoundToInt(_session.SelectionEnd * frames) : frames;
            if (!AudioSilenceDetector.FindAudibleRange(_originalSamples, channels, rangeStart, rangeEnd, _session.SilenceThreshold, out AudioFrameRange audible))
            {
                message = "No audible samples were found at this threshold. Lower the silence threshold and try again.";
                return false;
            }

            _session.HasSelection = true;
            _session.SelectionStart = (float)audible.Start / frames;
            _session.SelectionEnd = (float)audible.End / frames;
            _session.Playhead = _session.SelectionStart;
            _session.NormalizeSelection(1f / frames);
            RaiseChanged();
            return true;
        }

        internal string GetDefaultOutputPath(bool replaceOriginal)
        {
            if (replaceOriginal && _session.IsInProject && !string.IsNullOrEmpty(_session.ProjectPath))
            {
                return Path.Combine(Path.GetDirectoryName(Application.dataPath), _session.ProjectPath);
            }

            if (string.IsNullOrEmpty(_session.ExportFolder)) return null;
            return AudioProcessor.GenerateUniqueFilename(_session.ExportFolder, _session.FileName ?? "audio.wav");
        }

        /// <summary>Writes the result to <paramref name="outputPath"/>; several clips become numbered files based on it.</summary>
        internal string Export(string outputPath)
        {
            if (!HasAudio) throw new InvalidOperationException("No audio is loaded.");
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentException("Choose an output path first.", nameof(outputPath));
            return SaveOutputs(outputPath)[0];
        }

        /// <summary>Writes numbered copies next to the source, or to <paramref name="explicitPath"/> when given.</summary>
        internal IReadOnlyList<string> SaveOutputs(string explicitPath)
        {
            if (!HasAudio) throw new InvalidOperationException("No audio is loaded.");
            GetCurrentRender(out AudioRenderResult render, out AudioEditSettings settings);
            List<AudioOutputClip> outputs = AudioEditPipeline.RenderOutputs(render, settings);
            if (outputs.Count == 0) throw new InvalidOperationException("The selected region contains no audio samples.");
            if (string.IsNullOrEmpty(explicitPath) && string.IsNullOrEmpty(_session.ExportFolder)) throw new ArgumentException("Choose an output path first.", nameof(explicitPath));

            WavSampleFormat format = AudioWavWriter.Resolve(AudioToolsPreferences.OutputFormat, _session.MaterializedPath);
            List<string> written = AudioOutputWriter.Write(outputs, _session.ExportFolder, _session.FileName, explicitPath, format);
            FinishSave(written, _session.IsInProject ? _session.ProjectPath : null);
            return written;
        }

        /// <summary>Overwrites the source WAV asset after keeping a backup of it.</summary>
        internal string ReplaceOriginal()
        {
            if (!HasAudio) throw new InvalidOperationException("No audio is loaded.");
            string path = GetDefaultOutputPath(true);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only WAV files in the project can be replaced.");
            GetCurrentRender(out AudioRenderResult render, out AudioEditSettings settings);
            List<AudioOutputClip> outputs = AudioEditPipeline.RenderOutputs(render, settings);
            if (outputs.Count != 1) throw new InvalidOperationException("Replacing needs exactly one output clip. Save copies to keep several clips.");

            WavSampleFormat format = AudioWavWriter.Resolve(AudioToolsPreferences.OutputFormat, path);
            AudioReplaceBackup.Create(new[] { path });
            AudioWavWriter.Write(path, outputs[0].Samples, outputs[0].Channels, outputs[0].Frequency, format);
            FinishSave(new List<string> { path }, null);
            return path;
        }

        /// <summary>Clips the current settings would write.</summary>
        internal int GetOutputCount()
        {
            if (_render == null) return HasAudio ? 1 : 0;
            int count = 0;
            foreach (AudioRenderSegment segment in _render.Segments)
            {
                if (segment.ExportFrames > 0) count++;
            }

            return count;
        }

        /// <summary>Length of what would be written, in seconds at the source rate.</summary>
        internal float GetOutputDuration()
        {
            if (!_sourceMetadata.HasValue) return 0f;
            if (_render == null || _appliedSettings == null) return GetSelectionDuration();
            int frequency = _sourceMetadata.Value.Frequency;
            int kept = AudioEditPipeline.SecondsToFrames(_appliedSettings.KeptPause, frequency);
            long frames = 0;
            foreach (AudioRenderSegment segment in _render.Segments)
            {
                frames += Math.Max(0, segment.ExportFrames);
                foreach (AudioFrameRange pause in segment.Pauses) frames -= Math.Max(0, pause.Length - kept);
            }

            return (float)Math.Max(0, frames) / frequency;
        }

        /// <summary>Length of the active region: the first exported clip, the selection or the whole file.</summary>
        internal float GetSelectionDuration()
        {
            if (!_sourceMetadata.HasValue) return 0f;
            if (_render != null && _render.Segments.Count > 0 && _session.HasEffectiveRegion)
                return (float)(_render.Segments[0].End - _render.Segments[0].Start) / _sourceMetadata.Value.Frequency;
            return (_session.HasSelection ? _session.SelectionEnd - _session.SelectionStart : 1f) * (float)_sourceMetadata.Value.Duration;
        }

        internal float GetNormalizedPlaybackPosition()
        {
            if (!_sourceMetadata.HasValue || !AudioManager.IsPlaying()) return -1f;
            if (_sliceOffset >= 0)
            {
                int frame = (int)Math.Round(AudioManager.GetCurrentPosition() * (double)_sourceMetadata.Value.Frequency);
                int sourceFrame = _sliceUsesMap ? AudioSilenceDetector.MapToSource(_sliceMap, frame) : _sliceOffset + frame;
                return Mathf.Clamp01((float)sourceFrame / Math.Max(1, SourceFrameCount));
            }

            return Mathf.Clamp01(AudioManager.GetCurrentPosition() / Mathf.Max(0.0001f, (float)_sourceMetadata.Value.Duration));
        }

        private void GetCurrentRender(out AudioRenderResult render, out AudioEditSettings settings)
        {
            if (PreviewIsCurrent && _render != null && _appliedSettings != null)
            {
                render = _render;
                settings = _appliedSettings;
                return;
            }

            // A save right after a change must not write the previous preview.
            settings = _session.CreateSettings();
            render = AudioEditPipeline.Render(_originalSamples, _sourceMetadata.Value.Channels, _sourceMetadata.Value.Frequency, settings);
        }

        private void FinishSave(List<string> written, string sourceProjectPath)
        {
            List<AudioImportRequest> imports = new List<AudioImportRequest>(written.Count);
            foreach (string path in written) imports.Add(new AudioImportRequest(path, sourceProjectPath));
            AudioOutputWriter.Import(imports, true);
            PingOutput(written[0]);
            Status = written.Count == 1 ? "Saved " + Path.GetFileName(written[0]) : "Saved " + written.Count + " files";
            RatePromptPending = AudioToolsPreferences.RegisterSaveAndCheckRatePrompt();
            RaiseChanged();
        }

        private static string ResolveExportFolder(IAudioSource source, string exportFolder)
        {
            if (!string.IsNullOrEmpty(exportFolder)) return exportFolder;
            if (source.IsInProject && !string.IsNullOrEmpty(source.ProjectPath))
            {
                string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), source.ProjectPath);
                return Path.GetDirectoryName(fullPath);
            }
            if (source is FileAudioSource fileSource) return Path.GetDirectoryName(fileSource.FullPath);
            return null;
        }

        private static void PingOutput(string outputPath)
        {
            string projectPath = AudioOutputWriter.ToAssetPath(outputPath);
            if (projectPath == null) return;
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectPath);
            if (asset == null) return;
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private void CancelPendingWork()
        {
            _loadVersion++;
            _processVersion++;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
        }

        private void SetError(string message)
        {
            IsLoading = false;
            Error = message;
            Status = null;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private static void DestroyClip(AudioClip clip)
        {
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingWork();
            Stop();
            DestroyClip(_processedClip);
            DestroyClip(_sourceClip);
            _processedClip = null;
            _sourceClip = null;
            _sourceMetadata = null;
            _render = null;
            if (!string.IsNullOrEmpty(_previewPath) && File.Exists(_previewPath))
            {
                try { File.Delete(_previewPath); }
                catch (IOException) { }
            }
        }

        // Restores the session's own identity, so a file opened from another tool keeps its name and project path after a reload.
        private sealed class RestoredAudioSource : IAudioSource
        {
            private readonly string _path;

            internal RestoredAudioSource(AudioEditSession session)
            {
                _path = session.MaterializedPath;
                FileName = string.IsNullOrEmpty(session.FileName) ? Path.GetFileName(_path) : session.FileName;
                IsInProject = session.IsInProject;
                ProjectPath = session.ProjectPath;
            }

            public string FileName { get; }
            public bool IsInProject { get; }
            public string ProjectPath { get; }

            public Task<string> GetMaterializedPathAsync(CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(_path);
            }
        }
    }
}
