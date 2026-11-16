using System;
using UnityEngine;

namespace AudioTool
{
    [Serializable]
    internal sealed class AudioEditSession : ScriptableObject
    {
        internal const float DefaultPeakCeiling = 0.8912509f; // -1 dBFS

        [SerializeField] private string _materializedPath;
        [SerializeField] private string _fileName;
        [SerializeField] private string _projectPath;
        [SerializeField] private string _exportFolder;
        [SerializeField] private bool _isInProject;

        [SerializeField] private bool _hasSelection;
        [SerializeField] private float _selectionStart;
        [SerializeField] private float _selectionEnd = 1f;
        [SerializeField] private float _playhead;
        [SerializeField] private float _zoom = 1f;
        [SerializeField] private float _viewportStart;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _playSelection = true;

        [SerializeField] private bool _trimSilence;
        [SerializeField] private AudioSilenceMode _silenceMode;
        [SerializeField] private float _paddingBefore = 0.005f;
        [SerializeField] private float _paddingAfter = 0.02f;
        [SerializeField] private float _minimumPause = 0.3f;
        [SerializeField] private float _keptPause = 0.1f;
        [SerializeField] private bool _removeDcOffset;
        [SerializeField] private bool _highPass;
        [SerializeField] private float _highPassFrequency = 80f;
        [SerializeField] private bool _reverse;
        [SerializeField] private bool _normalize;
        [SerializeField] private AudioNormalizeMode _normalizeMode;
        [SerializeField] private float _normalizeTarget = 0.95f;
        [SerializeField] private float _loudnessTarget = -23f;
        [SerializeField] private float _peakCeiling = DefaultPeakCeiling;
        [SerializeField] private bool _fadeIn;
        [SerializeField] private float _fadeInDuration = 1f;
        [SerializeField] private AnimationCurve _fadeInCurve = null;
        [SerializeField] private bool _fadeOut;
        [SerializeField] private float _fadeOutDuration = 1f;
        [SerializeField] private AnimationCurve _fadeOutCurve = null;
        [SerializeField] private bool _adjustVolume;
        [SerializeField] private float _volume = 1f;
        [SerializeField] private bool _seamlessLoop;
        [SerializeField] private float _loopCrossfade = 0.25f;
        [SerializeField] private AudioLoopShape _loopShape;
        [SerializeField] private AudioOutputChannels _outputChannels;
        [SerializeField] private int _outputSampleRate;
        [SerializeField] private float _silenceThreshold = 0.01f;
        [SerializeField] private bool _processingExpanded = true;
        [SerializeField] private bool _outputExpanded;
        [SerializeField] private bool _advancedExpanded;

        internal string MaterializedPath { get => _materializedPath; set => _materializedPath = value; }
        internal string FileName { get => _fileName; set => _fileName = value; }
        internal string ProjectPath { get => _projectPath; set => _projectPath = value; }
        internal string ExportFolder { get => _exportFolder; set => _exportFolder = value; }
        internal bool IsInProject { get => _isInProject; set => _isInProject = value; }
        internal bool HasSelection { get => _hasSelection; set => _hasSelection = value; }
        internal float SelectionStart { get => _selectionStart; set => _selectionStart = Mathf.Clamp01(value); }
        internal float SelectionEnd { get => _selectionEnd; set => _selectionEnd = Mathf.Clamp01(value); }
        internal float Playhead { get => _playhead; set => _playhead = Mathf.Clamp01(value); }
        internal float Zoom { get => _zoom; set => _zoom = Mathf.Clamp(value, 1f, 32f); }
        internal float ViewportStart { get => _viewportStart; set => _viewportStart = Mathf.Clamp01(value); }
        internal bool Loop { get => _loop; set => _loop = value; }
        internal bool PlaySelection { get => _playSelection; set => _playSelection = value; }
        internal bool TrimSilence { get => _trimSilence; set => _trimSilence = value; }
        internal AudioSilenceMode SilenceMode { get => _silenceMode; set => _silenceMode = value; }
        internal float PaddingBefore { get => _paddingBefore; set => _paddingBefore = Mathf.Clamp(value, 0f, 2f); }
        internal float PaddingAfter { get => _paddingAfter; set => _paddingAfter = Mathf.Clamp(value, 0f, 2f); }
        internal float MinimumPause { get => _minimumPause; set => _minimumPause = Mathf.Clamp(value, 0.01f, 10f); }
        internal float KeptPause { get => _keptPause; set => _keptPause = Mathf.Clamp(value, 0f, 5f); }
        internal bool RemoveDcOffset { get => _removeDcOffset; set => _removeDcOffset = value; }
        internal bool HighPass { get => _highPass; set => _highPass = value; }
        internal float HighPassFrequency { get => _highPassFrequency; set => _highPassFrequency = Mathf.Clamp(value, 20f, 500f); }
        internal bool Reverse { get => _reverse; set => _reverse = value; }
        internal bool Normalize { get => _normalize; set => _normalize = value; }
        internal AudioNormalizeMode NormalizeMode { get => _normalizeMode; set => _normalizeMode = value; }
        internal float NormalizeTarget { get => _normalizeTarget; set => _normalizeTarget = Mathf.Clamp01(value); }
        internal float LoudnessTarget { get => _loudnessTarget; set => _loudnessTarget = Mathf.Clamp(value, -40f, -6f); }
        internal float PeakCeiling { get => _peakCeiling; set => _peakCeiling = Mathf.Clamp(value, 0.1f, 1f); }
        internal bool FadeIn { get => _fadeIn; set => _fadeIn = value; }
        internal float FadeInDuration { get => _fadeInDuration; set => _fadeInDuration = Mathf.Max(0.001f, value); }
        internal AnimationCurve FadeInCurve { get => _fadeInCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); set => _fadeInCurve = value; }
        internal bool FadeOut { get => _fadeOut; set => _fadeOut = value; }
        internal float FadeOutDuration { get => _fadeOutDuration; set => _fadeOutDuration = Mathf.Max(0.001f, value); }
        internal AnimationCurve FadeOutCurve { get => _fadeOutCurve ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f); set => _fadeOutCurve = value; }
        internal bool AdjustVolume { get => _adjustVolume; set => _adjustVolume = value; }
        internal float Volume { get => _volume; set => _volume = Mathf.Clamp(value, 0f, 2f); }
        internal bool SeamlessLoop { get => _seamlessLoop; set => _seamlessLoop = value; }
        internal float LoopCrossfade { get => _loopCrossfade; set => _loopCrossfade = Mathf.Clamp(value, 0.001f, 10f); }
        internal AudioLoopShape LoopShape { get => _loopShape; set => _loopShape = value; }
        internal AudioOutputChannels OutputChannels { get => _outputChannels; set => _outputChannels = value; }
        internal int OutputSampleRate { get => _outputSampleRate; set => _outputSampleRate = Mathf.Max(0, value); }
        internal float SilenceThreshold { get => _silenceThreshold; set => _silenceThreshold = Mathf.Clamp(value, 0.0001f, 1f); }
        internal bool ProcessingExpanded { get => _processingExpanded; set => _processingExpanded = value; }
        internal bool OutputExpanded { get => _outputExpanded; set => _outputExpanded = value; }
        internal bool AdvancedExpanded { get => _advancedExpanded; set => _advancedExpanded = value; }

        internal float ViewportDurationNormalized => 1f / Mathf.Max(1f, _zoom);

        /// <summary>True when Selection only playback has a region to play: a selection, trimmed silence or a seamless loop.</summary>
        internal bool HasEffectiveRegion => _hasSelection || _trimSilence || _seamlessLoop;

        internal bool SplitsClips => _trimSilence && _silenceMode == AudioSilenceMode.SplitClips;

        internal bool ChangesSamples => _removeDcOffset || _highPass || _reverse || _normalize || _fadeIn || _fadeOut || _adjustVolume || _seamlessLoop
                                        || _outputChannels != AudioOutputChannels.Keep || _outputSampleRate > 0;

        internal AudioEditSettings CreateSettings()
        {
            float[] fadeIn = new float[AudioEditSettings.CurveResolution];
            float[] fadeOut = new float[AudioEditSettings.CurveResolution];
            for (int i = 0; i < AudioEditSettings.CurveResolution; i++)
            {
                float t = (float)i / (AudioEditSettings.CurveResolution - 1);
                fadeIn[i] = FadeInCurve.Evaluate(t);
                fadeOut[i] = FadeOutCurve.Evaluate(t);
            }

            return new AudioEditSettings
            {
                HasSelection = _hasSelection,
                SelectionStart = _selectionStart,
                SelectionEnd = _selectionEnd,
                TrimSilence = _trimSilence,
                SilenceMode = _silenceMode,
                SilenceThreshold = _silenceThreshold,
                PaddingBefore = _paddingBefore,
                PaddingAfter = _paddingAfter,
                MinimumPause = _minimumPause,
                KeptPause = _keptPause,
                RemoveDcOffset = _removeDcOffset,
                HighPass = _highPass,
                HighPassFrequency = _highPassFrequency,
                Reverse = _reverse,
                Normalize = _normalize,
                NormalizeMode = _normalizeMode,
                NormalizeTarget = _normalizeTarget,
                LoudnessTarget = _loudnessTarget,
                PeakCeiling = _peakCeiling,
                FadeIn = _fadeIn,
                FadeInDuration = _fadeInDuration,
                FadeInCurve = fadeIn,
                FadeOut = _fadeOut,
                FadeOutDuration = _fadeOutDuration,
                FadeOutCurve = fadeOut,
                AdjustVolume = _adjustVolume,
                Volume = _volume,
                SeamlessLoop = _seamlessLoop,
                LoopCrossfade = _loopCrossfade,
                LoopShape = _loopShape,
                Channels = _outputChannels,
                SampleRate = _outputSampleRate
            };
        }

        internal void ClearSelection()
        {
            _hasSelection = false;
            _selectionStart = 0f;
            _selectionEnd = 1f;
        }

        internal void ResetEdits()
        {
            _hasSelection = false;
            _selectionStart = 0f;
            _selectionEnd = 1f;
            _playhead = 0f;
            _zoom = 1f;
            _viewportStart = 0f;
            _loop = true;
            _playSelection = true;
            _trimSilence = false;
            _silenceMode = AudioSilenceMode.TrimEdges;
            _paddingBefore = 0.005f;
            _paddingAfter = 0.02f;
            _minimumPause = 0.3f;
            _keptPause = 0.1f;
            _removeDcOffset = false;
            _highPass = false;
            _highPassFrequency = 80f;
            _reverse = false;
            _normalize = false;
            _normalizeMode = AudioNormalizeMode.Peak;
            _normalizeTarget = 0.95f;
            _loudnessTarget = -23f;
            _peakCeiling = DefaultPeakCeiling;
            _fadeIn = false;
            _fadeInDuration = 1f;
            _fadeOut = false;
            _fadeOutDuration = 1f;
            _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            _fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            _adjustVolume = false;
            _volume = 1f;
            _seamlessLoop = false;
            _loopCrossfade = 0.25f;
            _loopShape = AudioLoopShape.EqualPower;
            _outputChannels = AudioOutputChannels.Keep;
            _outputSampleRate = 0;
            _silenceThreshold = 0.01f;
        }

        internal void NormalizeSelection(float minimumWidth = 0.000001f)
        {
            _selectionStart = Mathf.Clamp01(_selectionStart);
            _selectionEnd = Mathf.Clamp01(_selectionEnd);
            if (_selectionStart > _selectionEnd)
            {
                float swap = _selectionStart;
                _selectionStart = _selectionEnd;
                _selectionEnd = swap;
            }

            if (_hasSelection && _selectionEnd - _selectionStart < minimumWidth)
            {
                _selectionEnd = Mathf.Min(1f, _selectionStart + minimumWidth);
                _selectionStart = Mathf.Max(0f, _selectionEnd - minimumWidth);
            }

            ClampViewport();
        }

        internal void ClampViewport()
        {
            float maximumStart = Mathf.Max(0f, 1f - ViewportDurationNormalized);
            _viewportStart = Mathf.Clamp(_viewportStart, 0f, maximumStart);
        }
    }
}
