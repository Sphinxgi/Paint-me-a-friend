using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioTool
{
    /// <summary>
    /// Audio editor window for trimming, processing, and exporting audio files.
    /// Can work standalone (via context menu) or embedded (via IAudioSource).
    /// </summary>
    internal sealed class AudioEditorUI : CommonEditorUI
    {
        private const float CompactBreakpoint = 620f;
        private const float WideBreakpoint = 980f;
        private const string BackupHint = "A backup of each original is kept in Library/AudioTools/Backups. Advanced > Restore Replaced Files puts them back.";

        // Instance tables keep the window free of static state, so Unity 6.7 needs no lifetime declaration for it.
        private readonly List<string> _silenceModeChoices = new List<string> { "Trim start and end", "Shorten pauses", "Split into clips" };
        private readonly List<string> _normalizeModeChoices = new List<string> { "Peak", "Loudness (LUFS)" };
        private readonly List<string> _loopShapeChoices = new List<string> { "Equal power", "Linear" };
        private readonly List<string> _formatChoices = new List<string> { "Match source", "16-bit PCM", "24-bit PCM", "32-bit float" };
        private readonly List<string> _channelChoices = new List<string> { "Keep", "Mono (mix)", "Left channel", "Right channel" };
        private readonly int[] _sampleRates = { 0, 48000, 44100, 32000, 24000, 22050, 16000, 11025 };

        [SerializeField] private AudioEditSession _session;
        [SerializeField] private AudioFileQueue _queue = new AudioFileQueue();

        private AudioEditorController _controller;
        internal AudioEditorController Controller => _controller;
        private VisualElement _emptyState;
        private VisualElement _audioSettingsWarning;
        private HelpBox _audioBackendHelp;
        private VisualElement _workspace;
        private VisualElement _workspaceBody;
        private AudioWaveformElement _waveform;
        private Label _sourceTitle;
        private Label _sourceDetails;
        private Label _sourcePeak;
        private Label _outputPeak;
        private Label _loudness;
        private VisualElement _fileNavigator;
        private Label _filePosition;
        private Button _previousFileButton;
        private Button _nextFileButton;
        private VisualElement _filesCard;
        private ListView _fileList;
        private Button _analyzeButton;
        private Label _selectionSummary;
        private Label _destinationSummary;
        private Label _volumeDb;
        private Label _effectStatus;
        private HelpBox _statusBox;
        private HelpBox _errorBox;
        private HelpBox _clippingBox;
        private VisualElement _ratePrompt;
        private Button _playButton;
        private Button _clearSelectionButton;
        private Button _replaceButton;
        private Button _primaryExportButton;
        private Button _restoreButton;
        private Toggle _selectionPlaybackToggle;
        private Toggle _trimSilenceToggle;
        private DropdownField _silenceModeField;
        private FloatField _paddingBeforeField;
        private FloatField _paddingAfterField;
        private FloatField _minimumPauseField;
        private FloatField _keptPauseField;
        private Toggle _removeDcToggle;
        private Toggle _highPassToggle;
        private Slider _highPassFrequencySlider;
        private DropdownField _normalizeModeField;
        private Slider _loudnessTargetSlider;
        private Slider _peakCeilingSlider;
        private Toggle _reverseToggle;
        private Toggle _seamlessLoopToggle;
        private FloatField _loopCrossfadeField;
        private DropdownField _loopShapeField;
        private VisualElement _fadeInCard;
        private VisualElement _fadeOutCard;
        private DropdownField _formatField;
        private DropdownField _channelsField;
        private DropdownField _sampleRateField;
        private DropdownField _destinationField;
        private CancellationTokenSource _batchCancellation;
        private string _batchStatus;
        private string _queueSignature;
        private int _backupCount = -1;
        private int _pendingCount;
        private AudioOutputFormat _resolvedFormatSetting = (AudioOutputFormat)(-1);
        private string _resolvedFormatPath;
        private WavSampleFormat _resolvedFormat;
        private bool _synchronizing;
        // Assembly reloads preserve private window fields, so a persisted flag would block every restore after the first.
        [NonSerialized] private bool _restoreRequested;

        public static AudioEditorUI ShowWindow()
        {
            AudioEditorUI window = GetWindow<AudioEditorUI>();
            window.titleContent = new GUIContent("Audio Editor", EditorGUIUtility.IconContent("AudioClip Icon").image);
            window.minSize = new Vector2(430f, 300f);
            window.Show();
            return window;
        }

        /// <summary>
        /// Initializes the editor with an audio source.
        /// </summary>
        /// <param name="audioSource">The audio source to edit.</param>
        /// <param name="exportFolder">Target folder for export (defaults to the source folder).</param>
        /// <param name="playWhenOpened">Whether the clip starts playing once it is loaded, so a host can keep its own auto-play setting.</param>
        public async void Init(IAudioSource audioSource, string exportFolder = null, bool playWhenOpened = true)
        {
            EnsureController();
            if (_batchCancellation != null) return;
            _queue.Clear();
            _batchStatus = null;
            await _controller.InitializeAsync(audioSource, exportFolder, playWhenOpened);
            // The window can close while the file loads; OnDisable then has already released the controller.
            if (_controller == null) return;
            if (_controller.HasAudio && audioSource != null) _queue.Add(AudioQueueEntry.FromSource(audioSource, _session.MaterializedPath, _session.ExportFolder));
            _queueSignature = null;
            BindWaveform();
            RefreshView();
        }

        /// <summary>Opens several files that share one set of edit settings.</summary>
        /// <param name="fullPaths">Audio files in the order to step through them.</param>
        internal async void InitMany(IReadOnlyList<string> fullPaths)
        {
            EnsureController();
            if (_batchCancellation != null || fullPaths == null || fullPaths.Count == 0) return;
            _queue.Clear();
            _batchStatus = null;
            foreach (string path in fullPaths) _queue.Add(AudioQueueEntry.FromFile(path));
            _queueSignature = null;
            await OpenQueueEntryAsync(0, true);
        }

        private void OnEnable()
        {
            EnsureController();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            _batchCancellation?.Cancel();
            if (_controller != null)
            {
                _controller.Changed -= RefreshView;
                _controller.Dispose();
                _controller = null;
            }
        }

        private void OnDestroy()
        {
            if (_session != null && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                DestroyImmediate(_session);
                _session = null;
            }
        }

        public void CreateGUI()
        {
            EnsureController();
            VisualElement root = rootVisualElement;
            root.Clear();
            root.name = "audio-tools-root";
            StyleSheet styleSheet = CommonUITK.LoadStyleSheetFromAnchor(
                "AudioEditorUI",
                "Editor/Scripts/UI/AudioEditorUI.cs",
                "Editor/Scripts/UI/AudioTools.uss");
            CommonUITK.ApplyRoot(root, styleSheet, "at-root", "at-theme-dark", "at-theme-light");
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            _audioSettingsWarning = BuildAudioSettingsWarning();
            root.Add(_audioSettingsWarning);
            _emptyState = BuildEmptyState();
            root.Add(_emptyState);
            _workspace = BuildWorkspace();
            root.Add(_workspace);
            RegisterDragAndDrop(root);

            root.schedule.Execute(() =>
            {
                if (_controller == null) return;
                _controller.TickPlayback();
                if (_controller.IsPlaying) _waveform?.Refresh();
            }).Every(33);

            _queueSignature = null;
            _backupCount = AudioReplaceBackup.LatestCount();
            BindWaveform();
            RefreshView();

            if (!_restoreRequested && !_controller.HasAudio && !string.IsNullOrEmpty(_session.MaterializedPath))
            {
                _restoreRequested = true;
                _ = RestoreSourceAsync();
            }
        }

        private async Task RestoreSourceAsync()
        {
            if (_controller == null) return;
            await _controller.RestoreAsync();
            BindWaveform();
            RefreshView();
        }

        private void EnsureController()
        {
            if (_session == null)
            {
                _session = CreateInstance<AudioEditSession>();
                _session.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_queue == null) _queue = new AudioFileQueue();
            if (_controller != null) return;
            _controller = new AudioEditorController(_session);
            _controller.Changed += RefreshView;
        }

        private VisualElement BuildAudioSettingsWarning()
        {
            VisualElement warning = CommonUITK.CreateContainer("at-audio-settings-warning");
            warning.name = "audio-settings-warning";
            _audioBackendHelp = CommonUITK.CreateHelpBox(CommonEditorAudioSettings.DisabledMessage, HelpBoxMessageType.Warning, "at-message");
            warning.Add(_audioBackendHelp);

            VisualElement actions = CommonUITK.CreateContainer("at-empty-actions");
            Button settings = CommonUITK.CreateButton("Open Audio Settings", CommonEditorAudioSettings.OpenSettings, "at-button");
            settings.name = "audio-open-settings";
            settings.tooltip = "Open Project Settings > Audio to review the project's Unity audio setting.";
            Button retry = CommonUITK.CreateButton("Retry", async () =>
            {
                EditorAudioService.RetryFmod();
                await RestoreSourceAsync();
            }, "at-button");
            retry.name = "audio-retry-load";
            retry.tooltip = "Try the selected audio file again after checking the audio backend.";
            actions.Add(settings);
            actions.Add(retry);
            warning.Add(actions);
            return warning;
        }

        private VisualElement BuildEmptyState()
        {
            CommonEmptyState state = new CommonEmptyState(new CommonEmptyState.EmptyStateClasses
            {
                RootClass = "at-empty-state",
                IconClass = "at-empty-logo",
                TitleClass = "at-empty-title",
                DetailClass = "at-empty-description",
                ActionsClass = "at-empty-actions"
            });
            state.name = "audio-empty-state";

            Texture2D logo = CommonUIStyles.LoadTexture("AudioTools");
            Button browse = CommonUITK.CreateButton("Browse File...", BrowseForSource, "at-button", "at-primary-button");
            browse.tooltip = "Choose a supported audio file from disk.";
            Button help = CommonUITK.CreateButton("How it works", AudioToolsWelcomeWindow.ShowWindow, "at-button");
            help.tooltip = "Open the Audio Tools quick-start guide.";
            state.SetContent(
                "Shape audio without leaving Unity",
                "Choose audio files, select the part you need, preview the result, and save new WAVs. Your sources stay untouched until you explicitly replace them.",
                logo,
                new VisualElement[] { browse, help });

            ObjectField clipField = new ObjectField("Audio asset")
            {
                name = "audio-source-field",
                objectType = typeof(AudioClip),
                allowSceneObjects = false,
                tooltip = "Select an AudioClip from this project."
            };
            clipField.AddToClassList("at-source-field");
            clipField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is AudioClip clip) LoadProjectClip(clip);
            });
            state.Insert(state.IndexOf(state.Actions), clipField);
            state.Add(CommonUITK.CreateLabel("You can also drag WAV, MP3, OGG, AIFF, AIF, or FLAC files or whole folders here.", "at-drop-hint"));

            _errorBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Error, "at-message");
            state.Add(_errorBox);
            return state;
        }

        private VisualElement BuildWorkspace()
        {
            VisualElement root = CommonUITK.CreateContainer("at-workspace");
            root.name = "audio-workspace";
            root.Add(BuildSourceHeader());

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "audio-workspace-scroll",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            scroll.AddToClassList("at-scroll");
            root.Add(scroll);

            _workspaceBody = CommonUITK.CreateContainer("at-workspace-body");
            scroll.Add(_workspaceBody);

            VisualElement editing = CommonUITK.CreateContainer("at-editing-column");
            editing.Add(BuildWaveformCard());
            editing.Add(BuildTransportCard());
            editing.Add(BuildFilesCard());
            _workspaceBody.Add(editing);
            _workspaceBody.Add(BuildProcessingPanel());

            root.Add(BuildExportFooter());
            return root;
        }

        private VisualElement BuildSourceHeader()
        {
            VisualElement header = CommonUITK.CreateContainer("at-source-header");
            VisualElement identity = CommonUITK.CreateContainer("at-source-identity");
            _sourceTitle = CommonUITK.CreateLabel(string.Empty, "at-source-title");
            _sourceDetails = CommonUITK.CreateLabel(string.Empty, "at-source-details");
            identity.Add(_sourceTitle);
            identity.Add(_sourceDetails);

            _fileNavigator = CommonUITK.CreateContainer("at-file-navigator");
            _fileNavigator.name = "audio-file-navigator";
            _previousFileButton = CommonUITK.CreateButton("‹", () => _ = StepFileAsync(-1), "at-button", "at-quiet-button", "at-nav-button");
            _previousFileButton.name = "audio-previous-file";
            _previousFileButton.tooltip = "Open the previous file with the same settings (Page Up).";
            _filePosition = CommonUITK.CreateLabel(string.Empty, "at-file-position");
            _filePosition.name = "audio-file-position";
            _nextFileButton = CommonUITK.CreateButton("›", () => _ = StepFileAsync(1), "at-button", "at-quiet-button", "at-nav-button");
            _nextFileButton.name = "audio-next-file";
            _nextFileButton.tooltip = "Open the next file with the same settings (Page Down).";
            Button addClips = CommonUITK.CreateButton("Add Clips", AddClips, "at-button", "at-quiet-button");
            addClips.name = "audio-add-clips";
            addClips.tooltip = "Apply these settings to more files: adds the audio files selected in the Project window, or asks for a folder when none are selected.";
            _fileNavigator.Add(_previousFileButton);
            _fileNavigator.Add(_filePosition);
            _fileNavigator.Add(_nextFileButton);
            _fileNavigator.Add(addClips);
            identity.Add(_fileNavigator);
            header.Add(identity);

            VisualElement metrics = CommonUITK.CreateContainer("at-metrics");
            _sourcePeak = CreateMetric(metrics, "SOURCE PEAK");
            _outputPeak = CreateMetric(metrics, "OUTPUT PEAK");
            _loudness = CreateMetric(metrics, "LOUDNESS");
            _loudness.name = "audio-loudness";
            header.Add(metrics);
            return header;
        }

        private static Label CreateMetric(VisualElement parent, string title)
        {
            VisualElement metric = CommonUITK.CreateContainer("at-metric");
            metric.Add(CommonUITK.CreateLabel(title, "at-metric-label"));
            Label value = CommonUITK.CreateLabel("-", "at-metric-value");
            metric.Add(value);
            parent.Add(metric);
            return value;
        }

        private VisualElement BuildWaveformCard()
        {
            VisualElement card = CreateCard("Waveform", "Drag to select, click to seek, use Ctrl/Cmd + wheel to zoom, and right-drag to pan.");
            _waveform = new AudioWaveformElement();
            _waveform.EditGestureStarted += () => Undo.RecordObject(_session, "Edit audio selection");
            _waveform.EditGestureFinished += () =>
            {
                EditorUtility.SetDirty(_session);
                StoreCurrentSelection();
                _controller.ScheduleProcessing();
                RefreshView();
            };
            _waveform.SelectionChanged += RefreshView;
            _waveform.SeekRequested += _controller.Seek;
            _waveform.ViewportChanged += RefreshView;
            card.Add(_waveform);
            return card;
        }

        private VisualElement BuildTransportCard()
        {
            VisualElement card = CreateCard("Preview & selection", "Use the waveform handles to choose and preview the exact region you need.");
            VisualElement transport = CommonUITK.CreateContainer("at-transport");
            _playButton = CommonUITK.CreateButton("Play", _controller.TogglePlayback, "at-button", "at-transport-button", "at-primary-button");
            transport.Add(_playButton);
            transport.Add(CommonUITK.CreateButton("Rewind", () =>
            {
                _controller.Stop();
                _controller.Seek(_session.HasSelection && _session.PlaySelection ? _session.SelectionStart : 0f);
            }, "at-button", "at-quiet-button"));
            Toggle loop = new Toggle("Loop") { name = "audio-loop", tooltip = "Repeat the preview. With a region, the region repeats exactly as a saved loop would." };
            loop.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle audio loop");
                _session.Loop = evt.newValue;
            });
            transport.Add(loop);
            _selectionPlaybackToggle = new Toggle("Selection only") { name = "audio-selection-playback", tooltip = "Play only what saving writes: the selection, trimmed audio or clips." };
            _selectionPlaybackToggle.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change preview region");
                _session.PlaySelection = evt.newValue;
            });
            transport.Add(_selectionPlaybackToggle);
            card.Add(transport);

            VisualElement quickActions = CommonUITK.CreateContainer("at-quick-actions");
            Button audible = CommonUITK.CreateButton("Select Audible Content", SelectAudibleContent, "at-button");
            audible.tooltip = "Select everything between the first and last sound above the silence threshold.";
            quickActions.Add(audible);
            quickActions.Add(CommonUITK.CreateButton("Select All", SelectAll, "at-button", "at-quiet-button"));
            _clearSelectionButton = CommonUITK.CreateButton("Clear Selection", ClearSelection, "at-button", "at-quiet-button");
            quickActions.Add(_clearSelectionButton);
            card.Add(quickActions);
            return card;
        }

        private VisualElement BuildFilesCard()
        {
            _filesCard = CommonUITK.CreateContainer("at-card", "at-files-card");
            _filesCard.name = "audio-files-card";
            VisualElement header = CommonUITK.CreateContainer("at-files-header");
            header.Add(CommonUITK.CreateLabel("Files", "at-card-title"));
            _analyzeButton = CommonUITK.CreateButton("Analyze", () => _ = RunBatchAsync(new List<AudioQueueEntry>(_queue.Entries), CreateBatchOptions(false, false), false), "at-button", "at-quiet-button");
            _analyzeButton.name = "audio-analyze-files";
            _analyzeButton.tooltip = "Measure what the current settings do to every file without writing anything: removed silence, gain, loudness and clipping.";
            header.Add(_analyzeButton);
            _filesCard.Add(header);

            _fileList = new ListView
            {
                name = "audio-file-list",
                fixedItemHeight = 24f,
                selectionType = SelectionType.None,
                itemsSource = _queue.Entries,
                makeItem = MakeFileRow,
                bindItem = BindFileRow
            };
            _fileList.AddToClassList("at-file-list");
            _filesCard.Add(_fileList);
            return _filesCard;
        }

        private VisualElement MakeFileRow()
        {
            VisualElement row = CommonUITK.CreateContainer("at-file-row");
            row.Add(CommonUITK.CreateLabel(string.Empty, "at-file-name"));
            row.Add(CommonUITK.CreateLabel(string.Empty, "at-file-result"));
            Button close = CommonUITK.CreateButton("×", null, "at-button", "at-quiet-button", "at-file-close");
            close.tooltip = "Close this file. It stays on disk.";
            close.clicked += () =>
            {
                if (close.userData is int index) _ = CloseFileAsync(index);
            };
            row.Add(close);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button || !(row.userData is int index) || index == _queue.Index) return;
                _ = OpenQueueEntryAsync(index, false);
            });
            return row;
        }

        private void BindFileRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _queue.Entries.Count) return;
            AudioQueueEntry entry = _queue.Entries[index];
            row.userData = index;
            row.EnableInClassList("at-file-row-current", index == _queue.Index);
            Label name = row.Q<Label>(className: "at-file-name");
            Label result = row.Q<Label>(className: "at-file-result");
            name.text = entry.FileName;
            name.tooltip = entry.Path;
            bool current = entry.ResultKey != null && entry.ResultKey == CreateEntryKey(entry, entry.Status == AudioQueueStatus.Saved);
            string text = entry.Status == AudioQueueStatus.Failed ? entry.Summary : current ? entry.Summary : string.Empty;
            if (current && entry.Status == AudioQueueStatus.Saved) text = "Saved · " + text;
            if (entry.Replaced) text = "Replaced · " + entry.Summary;
            result.text = text ?? string.Empty;
            result.tooltip = entry.Status == AudioQueueStatus.Saved && entry.Outputs != null && entry.Outputs.Length > 0 ? string.Join("\n", entry.Outputs) : text ?? string.Empty;
            result.EnableInClassList("at-file-result-error", entry.Status == AudioQueueStatus.Failed);
            row.Q<Button>(className: "at-file-close").userData = index;
        }

        private VisualElement BuildProcessingPanel()
        {
            VisualElement panel = CommonUITK.CreateContainer("at-processing-panel");
            panel.name = "audio-processing-panel";
            Foldout processing = CommonUITK.CreateFoldout("Enhance", _session.ProcessingExpanded, value => _session.ProcessingExpanded = value,
                "Processing is non-destructive until export.", "at-processing-foldout");
            panel.Add(processing);
            processing.Add(BuildTrimSilenceEffect());
            processing.Add(BuildCleanUpEffect());
            processing.Add(BuildNormalizeEffect());
            _fadeInCard = BuildFadeEffect(true);
            processing.Add(_fadeInCard);
            _fadeOutCard = BuildFadeEffect(false);
            processing.Add(_fadeOutCard);
            processing.Add(BuildVolumeEffect());
            processing.Add(BuildReverseEffect());
            processing.Add(BuildLoopEffect());

            panel.Add(BuildOutputPanel());

            Foldout advanced = CommonUITK.CreateFoldout("Advanced", _session.AdvancedExpanded, value => _session.AdvancedExpanded = value,
                "Session reset and restoring replaced files.", "at-advanced-foldout");
            panel.Add(advanced);
            advanced.Add(CommonUITK.CreateButton("Reset editing session", ResetSession, "at-button", "at-quiet-button"));
            _restoreButton = CommonUITK.CreateButton("Restore Replaced Files", RestoreReplacedFiles, "at-button", "at-quiet-button");
            _restoreButton.name = "audio-restore-replaced";
            _restoreButton.tooltip = "Put back the originals that the last Replace overwrote, from the backup in Library/AudioTools/Backups.";
            advanced.Add(_restoreButton);
            _effectStatus = CommonUITK.CreateLabel(string.Empty, "at-effect-status");
            panel.Add(_effectStatus);
            return panel;
        }

        private VisualElement BuildTrimSilenceEffect()
        {
            VisualElement effect = CreateEffectCard("Trim Silence", "Cut quiet starts and ends, shorten pauses or split a take into clips.");
            _trimSilenceToggle = new Toggle("Enable trim silence")
            {
                name = "audio-trim-silence",
                tooltip = "Save only the audible part. Works inside a selection and applies to every open file."
            };
            BindToggle(_trimSilenceToggle, "Toggle trim silence", value => _session.TrimSilence = value);
            _silenceModeField = new DropdownField("Mode", _silenceModeChoices, 0)
            {
                name = "audio-silence-mode",
                tooltip = "Trim start and end keeps one clip. Shorten pauses also tightens long gaps inside it. Split into clips saves each sound separated by a pause as its own file."
            };
            BindDropdown(_silenceModeField, "Change trim silence mode", index => _session.SilenceMode = (AudioSilenceMode)index);

            Slider silence = new Slider("Silence threshold (dB)", -80f, -6f) { showInputField = true };
            silence.name = "audio-silence-threshold";
            silence.tooltip = "Samples below this level count as silence, for trimming and for Select Audible Content.";
            silence.SetValueWithoutNotify(RoundDb(AmplitudeToDb(_session.SilenceThreshold)));
            silence.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change silence threshold");
                float decibels = RoundDb(evt.newValue);
                silence.SetValueWithoutNotify(decibels);
                _session.SilenceThreshold = DbToAmplitude(decibels);
                if (_session.TrimSilence) _controller.ScheduleProcessing();
            });

            _paddingBeforeField = CreateMillisecondsField("Keep before (ms)", "audio-padding-before", "Quiet lead-in kept before the first sound, so its attack is not cut.",
                "Change kept lead-in", value => _session.PaddingBefore = value);
            _paddingAfterField = CreateMillisecondsField("Keep after (ms)", "audio-padding-after", "Quiet tail kept after the last sound, so its decay is not cut.",
                "Change kept tail", value => _session.PaddingAfter = value);
            _minimumPauseField = CreateMillisecondsField("Minimum pause (ms)", "audio-minimum-pause", "Silence at least this long separates clips or counts as a pause to shorten.",
                "Change minimum pause", value => _session.MinimumPause = value);
            _keptPauseField = CreateMillisecondsField("Shorten pauses to (ms)", "audio-kept-pause", "Length that each long pause keeps after shortening.",
                "Change shortened pause", value => _session.KeptPause = value);

            effect.Add(_trimSilenceToggle);
            effect.Add(_silenceModeField);
            effect.Add(silence);
            effect.Add(_paddingBeforeField);
            effect.Add(_paddingAfterField);
            effect.Add(_minimumPauseField);
            effect.Add(_keptPauseField);
            return effect;
        }

        private VisualElement BuildCleanUpEffect()
        {
            VisualElement effect = CreateEffectCard("Clean Up", "Remove DC offset and low rumble before any other processing.");
            _removeDcToggle = new Toggle("Remove DC offset")
            {
                name = "audio-remove-dc",
                tooltip = "Centers a waveform that sits above or below zero. An offset causes clicks at cuts and hides silence from trimming."
            };
            BindToggle(_removeDcToggle, "Toggle DC offset removal", value => _session.RemoveDcOffset = value);
            _highPassToggle = new Toggle("High-pass filter")
            {
                name = "audio-high-pass",
                tooltip = "Removes rumble, wind and handling noise below the cutoff (24 dB per octave)."
            };
            BindToggle(_highPassToggle, "Toggle high-pass filter", value => _session.HighPass = value);
            _highPassFrequencySlider = new Slider("Cutoff (Hz)", 20f, 500f)
            {
                showInputField = true,
                name = "audio-high-pass-frequency",
                tooltip = "Frequencies below this are reduced. 80 Hz suits voice and most effects."
            };
            BindSlider(_highPassFrequencySlider, "Change high-pass cutoff", value => _session.HighPassFrequency = Mathf.Round(value));
            effect.Add(_removeDcToggle);
            effect.Add(_highPassToggle);
            effect.Add(_highPassFrequencySlider);
            return effect;
        }

        private VisualElement BuildNormalizeEffect()
        {
            VisualElement effect = CreateEffectCard("Normalize", "Bring each clip to a precise peak or loudness level.");
            Toggle enabled = new Toggle("Enable normalize") { name = "audio-normalize", tooltip = "Scales each saved clip to the target level." };
            _normalizeModeField = new DropdownField("Mode", _normalizeModeChoices, 0)
            {
                name = "audio-normalize-mode",
                tooltip = "Peak matches the loudest sample. Loudness matches perceived loudness (ITU-R BS.1770 integrated LUFS), which evens out a pack."
            };
            BindDropdown(_normalizeModeField, "Change normalize mode", index => _session.NormalizeMode = (AudioNormalizeMode)index);
            Slider target = new Slider("Target peak (dB)", -24f, 0f) { showInputField = true, name = "audio-normalize-target", tooltip = "Level of the loudest sample after normalizing." };
            _loudnessTargetSlider = new Slider("Target loudness (LUFS)", -40f, -6f)
            {
                showInputField = true,
                name = "audio-loudness-target",
                tooltip = "Integrated loudness after normalizing. -23 LUFS is the EBU R128 reference; louder targets need more headroom."
            };
            BindSlider(_loudnessTargetSlider, "Change loudness target", value => _session.LoudnessTarget = RoundDb(value));
            _peakCeilingSlider = new Slider("Peak ceiling (dB)", -12f, 0f)
            {
                showInputField = true,
                name = "audio-peak-ceiling",
                tooltip = "Loudness normalization never raises a peak above this level, so a clip may stay below the target instead of clipping."
            };
            BindSlider(_peakCeilingSlider, "Change peak ceiling", value => _session.PeakCeiling = DbToAmplitude(RoundDb(value)));
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle normalization");
                _session.Normalize = evt.newValue;
                target.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            target.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change normalize target");
                float decibels = RoundDb(evt.newValue);
                target.SetValueWithoutNotify(decibels);
                _session.NormalizeTarget = DbToAmplitude(decibels);
                _controller.ScheduleProcessing();
            });
            effect.Add(enabled);
            effect.Add(_normalizeModeField);
            effect.Add(target);
            effect.Add(_loudnessTargetSlider);
            effect.Add(_peakCeilingSlider);
            return effect;
        }

        private VisualElement BuildFadeEffect(bool fadeIn)
        {
            string title = fadeIn ? "Fade In" : "Fade Out";
            VisualElement effect = CreateEffectCard(title, fadeIn ? "Bring the region in smoothly." : "Finish the region cleanly.");
            Toggle enabled = new Toggle("Enable " + title.ToLowerInvariant()) { name = fadeIn ? "audio-fade-in" : "audio-fade-out" };
            FloatField duration = new FloatField("Duration (seconds)") { name = fadeIn ? "audio-fade-in-duration" : "audio-fade-out-duration" };
            CurveField curve = new CurveField("Curve") { name = fadeIn ? "audio-fade-in-curve" : "audio-fade-out-curve" };
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle " + title.ToLowerInvariant());
                if (fadeIn) _session.FadeIn = evt.newValue;
                else _session.FadeOut = evt.newValue;
                duration.SetEnabled(evt.newValue);
                curve.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            duration.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change " + title.ToLowerInvariant() + " duration");
                // Each clip shortens the fade to fit itself, so the shared value is not clamped to the open file.
                float clamped = Mathf.Max(0.001f, evt.newValue);
                if (fadeIn) _session.FadeInDuration = clamped;
                else _session.FadeOutDuration = clamped;
                _controller.ScheduleProcessing();
                RefreshView();
            });
            curve.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change " + title.ToLowerInvariant() + " curve");
                if (fadeIn) _session.FadeInCurve = evt.newValue;
                else _session.FadeOutCurve = evt.newValue;
                _controller.ScheduleProcessing();
                _waveform.Refresh();
            });
            effect.Add(enabled);
            effect.Add(duration);
            effect.Add(curve);
            return effect;
        }

        private VisualElement BuildVolumeEffect()
        {
            VisualElement effect = CreateEffectCard("Volume", "Scale loudness while keeping samples within the WAV range.");
            Toggle enabled = new Toggle("Enable volume adjustment") { name = "audio-volume" };
            Slider amount = new Slider("Amount", 0f, 2f) { showInputField = true, name = "audio-volume-amount" };
            _volumeDb = CommonUITK.CreateLabel("100% · 0.0 dB", "at-secondary-value");
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle volume adjustment");
                _session.AdjustVolume = evt.newValue;
                amount.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            amount.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change volume");
                _session.Volume = evt.newValue;
                _controller.ScheduleProcessing();
                RefreshView();
            });
            effect.Add(enabled);
            effect.Add(amount);
            effect.Add(_volumeDb);
            return effect;
        }

        private VisualElement BuildReverseEffect()
        {
            VisualElement effect = CreateEffectCard("Reverse", "Play the region backwards, for risers, swells and reversed cymbals.");
            _reverseToggle = new Toggle("Reverse audio") { name = "audio-reverse", tooltip = "Reverses each saved clip before normalizing and fading, so fades shape the reversed sound." };
            BindToggle(_reverseToggle, "Toggle reverse", value => _session.Reverse = value);
            effect.Add(_reverseToggle);
            return effect;
        }

        private VisualElement BuildLoopEffect()
        {
            VisualElement effect = CreateEffectCard("Seamless Loop", "Blend the end into the start so the clip repeats without a click.");
            _seamlessLoopToggle = new Toggle("Make seamless loop")
            {
                name = "audio-seamless-loop",
                tooltip = "Crossfades the end of the region into its start. The saved loop is shorter by the crossfade length."
            };
            BindToggle(_seamlessLoopToggle, "Toggle seamless loop", value => _session.SeamlessLoop = value);
            _loopCrossfadeField = new FloatField("Crossfade (seconds)")
            {
                name = "audio-loop-crossfade",
                tooltip = "Length of the blend between the end and the start. Longer crossfades hide more, up to half of the clip."
            };
            _loopCrossfadeField.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change loop crossfade");
                _session.LoopCrossfade = evt.newValue;
                _controller.ScheduleProcessing();
                RefreshView();
            });
            _loopShapeField = new DropdownField("Shape", _loopShapeChoices, 0)
            {
                name = "audio-loop-shape",
                tooltip = "Equal power keeps ambience and noise at a steady level. Linear suits tonal material that repeats in phase."
            };
            BindDropdown(_loopShapeField, "Change loop shape", index => _session.LoopShape = (AudioLoopShape)index);
            effect.Add(_seamlessLoopToggle);
            effect.Add(_loopCrossfadeField);
            effect.Add(_loopShapeField);
            return effect;
        }

        private VisualElement BuildOutputPanel()
        {
            Foldout output = CommonUITK.CreateFoldout("Output", _session.OutputExpanded, value => _session.OutputExpanded = value,
                "Sample format, channels and sample rate of saved WAV files.", "at-output-foldout");
            output.name = "audio-output-panel";
            _formatField = new DropdownField("Format", _formatChoices, (int)AudioToolsPreferences.OutputFormat)
            {
                name = "audio-output-format",
                tooltip = "Match source keeps 24-bit and 32-bit float sources at their depth and saves everything else as 16-bit PCM. Unity compresses audio on import either way."
            };
            _formatField.RegisterValueChangedCallback(_ =>
            {
                if (_synchronizing) return;
                AudioToolsPreferences.OutputFormat = (AudioOutputFormat)Mathf.Max(0, _formatField.index);
                RefreshView();
            });
            _channelsField = new DropdownField("Channels", _channelChoices, 0)
            {
                name = "audio-output-channels",
                tooltip = "Mono (mix) averages the channels into one. Left or Right keeps one channel, for recordings with a dead or noisy side."
            };
            BindDropdown(_channelsField, "Change output channels", index => _session.OutputChannels = (AudioOutputChannels)index);
            List<string> rates = new List<string>();
            foreach (int rate in _sampleRates) rates.Add(rate == 0 ? "Keep" : rate.ToString(CultureInfo.InvariantCulture) + " Hz");
            _sampleRateField = new DropdownField("Sample rate", rates, 0)
            {
                name = "audio-output-sample-rate",
                tooltip = "Converts saved files with a band-limited resampler. Unity's Override Sample Rate import option does the same for builds without changing the file."
            };
            BindDropdown(_sampleRateField, "Change output sample rate", index => _session.OutputSampleRate = _sampleRates[Mathf.Clamp(index, 0, _sampleRates.Length - 1)]);
            _destinationField = new DropdownField("Save copies to", new List<string> { "Next to each original" }, 0)
            {
                name = "audio-output-destination",
                tooltip = "Where multi-file saves write their copies."
            };
            _destinationField.RegisterValueChangedCallback(_ =>
            {
                if (_synchronizing) return;
                OnDestinationChosen(_destinationField.index);
            });
            output.Add(_formatField);
            output.Add(_channelsField);
            output.Add(_sampleRateField);
            output.Add(_destinationField);
            return output;
        }

        private VisualElement BuildExportFooter()
        {
            VisualElement footer = CommonUITK.CreateContainer("at-export-footer");
            footer.name = "audio-export-footer";
            VisualElement summary = CommonUITK.CreateContainer("at-export-summary");
            _selectionSummary = CommonUITK.CreateLabel(string.Empty, "at-export-title");
            _destinationSummary = CommonUITK.CreateLabel(string.Empty, "at-export-details");
            summary.Add(_selectionSummary);
            summary.Add(_destinationSummary);
            footer.Add(summary);

            _statusBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Info, "at-message", "at-footer-message");
            footer.Add(_statusBox);
            _clippingBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Warning, "at-message", "at-footer-message");
            footer.Add(_clippingBox);

            _ratePrompt = CommonUITK.CreateContainer("at-rate-prompt");
            _ratePrompt.name = "audio-rate-prompt";
            _ratePrompt.style.display = DisplayStyle.None;
            Button rate = CommonUITK.CreateButton("Rate Audio Tools on the Asset Store", () =>
            {
                AudioToolsPreferences.OpenStorePage();
                _controller.RatePromptPending = false;
                RefreshView();
            }, "at-button", "at-quiet-button", "at-rate-button");
            rate.tooltip = "Ratings help other Unity developers find Audio Tools.";
            Button dismiss = CommonUITK.CreateButton("×", () =>
            {
                AudioToolsPreferences.MarkRatePromptHandled();
                _controller.RatePromptPending = false;
                RefreshView();
            }, "at-button", "at-quiet-button", "at-file-close");
            dismiss.tooltip = "Hide this for good.";
            _ratePrompt.Add(rate);
            _ratePrompt.Add(dismiss);
            footer.Add(_ratePrompt);

            VisualElement actions = CommonUITK.CreateContainer("at-export-actions");
            _replaceButton = CommonUITK.CreateButton("Replace Original WAV", ReplaceOriginal, "at-button", "at-danger-button");
            _replaceButton.style.display = DisplayStyle.None;
            _primaryExportButton = CommonUITK.CreateButton("Save Copy", SavePrimary, "at-button", "at-primary-button");
            _primaryExportButton.name = "audio-primary-export";
            actions.Add(_replaceButton);
            actions.Add(_primaryExportButton);
            footer.Add(actions);
            return footer;
        }

        private static VisualElement CreateCard(string title, string description)
        {
            VisualElement card = CommonUITK.CreateContainer("at-card");
            card.Add(CommonUITK.CreateLabel(title, "at-card-title"));
            card.Add(CommonUITK.CreateLabel(description, "at-card-description"));
            return card;
        }

        private static VisualElement CreateEffectCard(string title, string description)
        {
            VisualElement card = CommonUITK.CreateContainer("at-effect-card");
            card.Add(CommonUITK.CreateLabel(title, "at-effect-title"));
            card.Add(CommonUITK.CreateLabel(description, "at-effect-description"));
            return card;
        }

        private FloatField CreateMillisecondsField(string label, string name, string tooltip, string undoName, Action<float> applySeconds)
        {
            FloatField field = new FloatField(label) { name = name, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession(undoName);
                applySeconds(Mathf.Max(0f, evt.newValue) / 1000f);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            return field;
        }

        private void BindToggle(Toggle toggle, string undoName, Action<bool> apply)
        {
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession(undoName);
                apply(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
        }

        private void BindSlider(Slider slider, string undoName, Action<float> apply)
        {
            slider.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession(undoName);
                apply(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
        }

        private void BindDropdown(DropdownField field, string undoName, Action<int> apply)
        {
            field.RegisterValueChangedCallback(_ =>
            {
                if (_synchronizing || field.index < 0) return;
                RecordSession(undoName);
                apply(field.index);
                _controller.ScheduleProcessing();
                RefreshView();
            });
        }

        private void RefreshView()
        {
            if (rootVisualElement == null || _emptyState == null || _workspace == null || _controller == null) return;
            _synchronizing = true;
            try
            {
                bool hasAudio = _controller.HasAudio;
                SetDisplayed(_audioSettingsWarning, _controller.RequiresAudioSettings);
                if (_audioBackendHelp != null && _controller.RequiresAudioSettings)
                    _audioBackendHelp.text = _controller.Error ?? CommonEditorAudioSettings.DisabledMessage;
                _emptyState.style.display = hasAudio || _controller.IsLoading ? DisplayStyle.None : DisplayStyle.Flex;
                _workspace.style.display = hasAudio || _controller.IsLoading ? DisplayStyle.Flex : DisplayStyle.None;
                _errorBox.text = _controller.Error ?? string.Empty;
                _errorBox.style.display = string.IsNullOrEmpty(_controller.Error) || _controller.RequiresAudioSettings ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshQueueView();

                if (!hasAudio)
                {
                    if (_controller.IsLoading)
                    {
                        _workspace.style.display = DisplayStyle.Flex;
                        _sourceTitle.text = _session.FileName ?? "Loading audio";
                        _sourceDetails.text = _controller.Status ?? "Preparing source...";
                        SyncEffectControls();
                    }
                    return;
                }

                bool running = _batchCancellation != null;
                EditorAudioMetadata metadata = _controller.SourceMetadata.Value;
                _sourceTitle.text = _session.FileName ?? "Audio";
                _sourceDetails.text = FormatTime((float)metadata.Duration) + "  ·  " + (metadata.Channels == 1 ? "Mono" : metadata.Channels + " channels") + "  ·  " + metadata.Frequency + " Hz";
                _sourcePeak.text = FormatPeak(_controller.SourcePeak);
                _outputPeak.text = FormatPeak(_controller.OutputPeak);
                RefreshLoudness();
                _playButton.text = _controller.IsPlaying ? "Stop" : "Play";
                _selectionPlaybackToggle.SetValueWithoutNotify(_session.PlaySelection);
                _selectionPlaybackToggle.SetEnabled(_session.HasEffectiveRegion);
                _clearSelectionButton.SetEnabled(_session.HasSelection);

                _selectionSummary.text = GetExportSummary(metadata);
                RefreshDestinationSummary();
                RefreshExportActions(running);
                string status = _batchStatus ?? _controller.Status;
                _statusBox.messageType = HelpBoxMessageType.Info;
                _statusBox.text = status ?? string.Empty;
                _statusBox.style.display = string.IsNullOrEmpty(status) || status == "Ready" ? DisplayStyle.None : DisplayStyle.Flex;
                _clippingBox.text = _controller.ClippedSampleCount > 0
                    ? _controller.ClippedSampleCount + " samples reach the clipping limit. You can still export, but lowering volume may sound cleaner."
                    : string.Empty;
                _clippingBox.style.display = _controller.ClippedSampleCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                SetDisplayed(_ratePrompt, _controller.RatePromptPending);
                if (_controller.RatePromptPending) AudioToolsPreferences.MarkRatePromptHandled();
                _volumeDb.text = Mathf.RoundToInt(_session.Volume * 100f) + "% · " + FormatDb(_session.Volume);
                _effectStatus.text = GetEffectStatus();
                _workspaceBody.SetEnabled(!running);
                SyncEffectControls();
                BindWaveform();
                _waveform.SetOverlay(BuildOverlay());
                _waveform.Refresh();
            }
            finally
            {
                _synchronizing = false;
            }
        }

        private void RefreshLoudness()
        {
            AudioRenderResult render = _controller.Render;
            AudioLoudness loudness = render != null && render.Segments.Count > 0 ? render.Segments[0].Loudness : _controller.SourceLoudness;
            _loudness.text = loudness.IsSilent ? "-∞ LUFS" : loudness.Integrated.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS";
            string source = _controller.SourceLoudness.IsSilent ? "silent" : _controller.SourceLoudness.Integrated.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS";
            string scope = render != null && render.Segments.Count > 1 ? " First of " + render.Segments.Count + " clips." : string.Empty;
            string kind = loudness.IsShort ? " Shorter than 400 ms, so measured over the whole clip without gating." : string.Empty;
            _loudness.tooltip = "Integrated loudness of the saved result (ITU-R BS.1770). Source: " + source + "." + scope + kind;
        }

        private void RefreshQueueView()
        {
            bool multi = _queue.IsMulti;
            SetDisplayed(_filesCard, multi);
            SetDisplayed(_destinationField, multi);
            SetDisplayed(_previousFileButton, multi);
            SetDisplayed(_nextFileButton, multi);
            SetDisplayed(_filePosition, multi);
            SetDisplayed(_fileNavigator, _controller.HasAudio || multi);
            if (multi)
            {
                _filePosition.text = (_queue.Index + 1) + " of " + _queue.Entries.Count;
                _previousFileButton.SetEnabled(_queue.Index > 0 && _batchCancellation == null);
                _nextFileButton.SetEnabled(_queue.Index < _queue.Entries.Count - 1 && _batchCancellation == null);
                _analyzeButton.SetEnabled(_batchCancellation == null);
                RefreshDestinationChoices();
            }

            string signature = multi ? _queue.Entries.Count + ":" + _queue.Index + ":" + _session.CreateSettings().CreateKey() + ":" + GetStatusSignature() + ":" + (int)AudioToolsPreferences.OutputFormat + ":" + _queue.DestinationFolder : null;
            if (signature == _queueSignature) return;
            _queueSignature = signature;
            if (!multi) return;
            _pendingCount = CountPendingEntries();
            if (!ReferenceEquals(_fileList.itemsSource, _queue.Entries)) _fileList.itemsSource = _queue.Entries;
            _fileList.style.height = Mathf.Min(8, _queue.Entries.Count) * _fileList.fixedItemHeight + 2f;
            _fileList.RefreshItems();
        }

        private string GetStatusSignature()
        {
            unchecked
            {
                int hash = 17;
                foreach (AudioQueueEntry entry in _queue.Entries) hash = hash * 31 + (int)entry.Status + (entry.ResultKey?.GetHashCode() ?? 0) + (entry.Summary?.GetHashCode() ?? 0);
                return hash.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void RefreshDestinationChoices()
        {
            List<string> choices = new List<string> { "Next to each original" };
            if (!string.IsNullOrEmpty(_queue.DestinationFolder)) choices.Add(GetCompactPath(_queue.DestinationFolder));
            choices.Add("Choose Folder");
            _destinationField.choices = choices;
            _destinationField.index = string.IsNullOrEmpty(_queue.DestinationFolder) ? 0 : 1;
            _destinationField.tooltip = string.IsNullOrEmpty(_queue.DestinationFolder) ? "Where multi-file saves write their copies." : _queue.DestinationFolder;
        }

        private void OnDestinationChosen(int index)
        {
            int chooseIndex = _destinationField.choices.Count - 1;
            if (index == 0) _queue.DestinationFolder = null;
            else if (index == chooseIndex)
            {
                string start = string.IsNullOrEmpty(_queue.DestinationFolder) ? Application.dataPath : _queue.DestinationFolder;
                string folder = EditorUtility.OpenFolderPanel("Save Audio Copies To", start, string.Empty);
                if (!string.IsNullOrEmpty(folder)) _queue.DestinationFolder = folder;
            }

            _queueSignature = null;
            RefreshView();
        }

        private void RefreshDestinationSummary()
        {
            if (_queue.IsMulti)
            {
                _destinationSummary.text = string.IsNullOrEmpty(_queue.DestinationFolder) ? "Copies are saved next to each original." : "Copies are saved to " + GetCompactPath(_queue.DestinationFolder);
                _destinationSummary.tooltip = _queue.DestinationFolder ?? string.Empty;
                return;
            }

            string destination = _controller.GetDefaultOutputPath(false);
            int outputs = _controller.GetOutputCount();
            if (string.IsNullOrEmpty(destination)) _destinationSummary.text = "Choose a destination when saving.";
            else if (outputs > 1) _destinationSummary.text = "Next outputs: " + GetCompactPath(destination) + " and " + (outputs - 1) + " more";
            else _destinationSummary.text = "Next output: " + GetCompactPath(destination);
            _destinationSummary.tooltip = destination ?? string.Empty;
        }

        private void RefreshExportActions(bool running)
        {
            if (running)
            {
                SetDisplayed(_replaceButton, false);
                _primaryExportButton.text = "Cancel";
                _primaryExportButton.tooltip = "Stop after the current file.";
                return;
            }

            if (_queue.IsMulti)
            {
                int count = _queue.Entries.Count;
                int replaceCount = _queue.Entries.FindAll(entry => entry.IsProjectWav && !entry.Replaced).Count;
                bool canReplace = !_session.SplitsClips && replaceCount > 0 && _queue.Entries.TrueForAll(entry => entry.IsProjectWav);
                SetDisplayed(_replaceButton, canReplace);
                _replaceButton.text = "Replace " + replaceCount + (replaceCount == 1 ? " WAV" : " WAVs");
                _replaceButton.tooltip = "Overwrite every original WAV with its result. " + BackupHint;
                int pending = _pendingCount;
                _primaryExportButton.text = _session.SplitsClips ? "Save Clips of " + pending + " Files" : "Save " + pending + (pending == 1 ? " Copy" : " Copies");
                _primaryExportButton.tooltip = "Save numbered WAV copies of every file that is not yet saved with these settings. Copies keep the import settings of their originals.";
                return;
            }

            SetDisplayed(_replaceButton, CanReplaceOriginal());
            _replaceButton.text = "Replace Original WAV";
            _replaceButton.tooltip = "Overwrite the original WAV with the result. " + BackupHint;
            int outputs = _controller.GetOutputCount();
            _primaryExportButton.text = outputs > 1 ? "Save " + outputs + " Clips" : GetPrimaryExportLabel();
            _primaryExportButton.tooltip = "Write the result as a new WAV without changing the source. Copies in the project keep the source's import settings.";
        }

        private void SyncEffectControls()
        {
            Toggle normalize = rootVisualElement.Q<Toggle>("audio-normalize");
            Slider normalizeTarget = rootVisualElement.Q<Slider>("audio-normalize-target");
            Toggle fadeIn = rootVisualElement.Q<Toggle>("audio-fade-in");
            FloatField fadeInDuration = rootVisualElement.Q<FloatField>("audio-fade-in-duration");
            CurveField fadeInCurve = rootVisualElement.Q<CurveField>("audio-fade-in-curve");
            Toggle fadeOut = rootVisualElement.Q<Toggle>("audio-fade-out");
            FloatField fadeOutDuration = rootVisualElement.Q<FloatField>("audio-fade-out-duration");
            CurveField fadeOutCurve = rootVisualElement.Q<CurveField>("audio-fade-out-curve");
            Toggle volume = rootVisualElement.Q<Toggle>("audio-volume");
            Slider volumeAmount = rootVisualElement.Q<Slider>("audio-volume-amount");
            Toggle loop = rootVisualElement.Q<Toggle>("audio-loop");

            bool peakMode = _session.NormalizeMode == AudioNormalizeMode.Peak;
            normalize?.SetValueWithoutNotify(_session.Normalize);
            normalizeTarget?.SetValueWithoutNotify(RoundDb(AmplitudeToDb(_session.NormalizeTarget)));
            normalizeTarget?.SetEnabled(_session.Normalize);
            SetDisplayed(normalizeTarget, _session.Normalize && peakMode);
            _normalizeModeField.SetValueWithoutNotify(_normalizeModeChoices[(int)_session.NormalizeMode]);
            SetDisplayed(_normalizeModeField, _session.Normalize);
            _loudnessTargetSlider.SetValueWithoutNotify(_session.LoudnessTarget);
            SetDisplayed(_loudnessTargetSlider, _session.Normalize && !peakMode);
            _peakCeilingSlider.SetValueWithoutNotify(RoundDb(AmplitudeToDb(_session.PeakCeiling)));
            SetDisplayed(_peakCeilingSlider, _session.Normalize && !peakMode);

            fadeIn?.SetValueWithoutNotify(_session.FadeIn);
            fadeInDuration?.SetValueWithoutNotify(_session.FadeInDuration);
            fadeInDuration?.SetEnabled(_session.FadeIn);
            SetDisplayed(fadeInDuration, _session.FadeIn);
            fadeInCurve?.SetValueWithoutNotify(_session.FadeInCurve);
            fadeInCurve?.SetEnabled(_session.FadeIn);
            SetDisplayed(fadeInCurve, _session.FadeIn);
            fadeOut?.SetValueWithoutNotify(_session.FadeOut);
            fadeOutDuration?.SetValueWithoutNotify(_session.FadeOutDuration);
            fadeOutDuration?.SetEnabled(_session.FadeOut);
            SetDisplayed(fadeOutDuration, _session.FadeOut);
            fadeOutCurve?.SetValueWithoutNotify(_session.FadeOutCurve);
            fadeOutCurve?.SetEnabled(_session.FadeOut);
            SetDisplayed(fadeOutCurve, _session.FadeOut);
            // Fades would dip at the seam of a loop, so the loop owns both ends while it is on.
            SetDisplayed(_fadeInCard, !_session.SeamlessLoop);
            SetDisplayed(_fadeOutCard, !_session.SeamlessLoop);
            volume?.SetValueWithoutNotify(_session.AdjustVolume);
            volumeAmount?.SetValueWithoutNotify(_session.Volume);
            volumeAmount?.SetEnabled(_session.AdjustVolume);
            SetDisplayed(volumeAmount, _session.AdjustVolume);
            SetDisplayed(_volumeDb, _session.AdjustVolume);
            loop?.SetValueWithoutNotify(_session.Loop);
            rootVisualElement.Q<Slider>("audio-silence-threshold")?.SetValueWithoutNotify(RoundDb(AmplitudeToDb(_session.SilenceThreshold)));

            bool trim = _session.TrimSilence;
            _trimSilenceToggle.SetValueWithoutNotify(trim);
            _silenceModeField.SetValueWithoutNotify(_silenceModeChoices[(int)_session.SilenceMode]);
            SetDisplayed(_silenceModeField, trim);
            _paddingBeforeField.SetValueWithoutNotify(Milliseconds(_session.PaddingBefore));
            _paddingAfterField.SetValueWithoutNotify(Milliseconds(_session.PaddingAfter));
            _minimumPauseField.SetValueWithoutNotify(Milliseconds(_session.MinimumPause));
            _keptPauseField.SetValueWithoutNotify(Milliseconds(_session.KeptPause));
            SetDisplayed(_paddingBeforeField, trim);
            SetDisplayed(_paddingAfterField, trim);
            SetDisplayed(_minimumPauseField, trim && _session.SilenceMode != AudioSilenceMode.TrimEdges);
            SetDisplayed(_keptPauseField, trim && _session.SilenceMode == AudioSilenceMode.ShortenPauses);

            _removeDcToggle.SetValueWithoutNotify(_session.RemoveDcOffset);
            _highPassToggle.SetValueWithoutNotify(_session.HighPass);
            _highPassFrequencySlider.SetValueWithoutNotify(_session.HighPassFrequency);
            SetDisplayed(_highPassFrequencySlider, _session.HighPass);
            _reverseToggle.SetValueWithoutNotify(_session.Reverse);
            _seamlessLoopToggle.SetValueWithoutNotify(_session.SeamlessLoop);
            _loopCrossfadeField.SetValueWithoutNotify(_session.LoopCrossfade);
            _loopShapeField.SetValueWithoutNotify(_loopShapeChoices[(int)_session.LoopShape]);
            SetDisplayed(_loopCrossfadeField, _session.SeamlessLoop);
            SetDisplayed(_loopShapeField, _session.SeamlessLoop);

            _formatField.SetValueWithoutNotify(_formatChoices[(int)AudioToolsPreferences.OutputFormat]);
            _channelsField.SetValueWithoutNotify(_channelChoices[(int)_session.OutputChannels]);
            bool multiChannel = !_controller.SourceMetadata.HasValue || _controller.SourceMetadata.Value.Channels > 1 || _queue.IsMulti;
            SetDisplayed(_channelsField, multiChannel || _session.OutputChannels != AudioOutputChannels.Keep);
            int rateIndex = Array.IndexOf(_sampleRates, _session.OutputSampleRate);
            _sampleRateField.SetValueWithoutNotify(_sampleRateField.choices[Mathf.Max(0, rateIndex)]);
            SetDisplayed(_restoreButton, _backupCount > 0);
        }

        private AudioWaveformOverlay BuildOverlay()
        {
            AudioRenderResult render = _controller.Render;
            AudioWaveformOverlay overlay = new AudioWaveformOverlay
            {
                DimOutside = _session.TrimSilence,
                DrawFades = !_session.SeamlessLoop
            };

            bool useRender = render != null && render.Frames > 0 && render.Segments.Count > 0 && (_session.TrimSilence || _session.SeamlessLoop);
            if (!useRender)
            {
                // Without trimming the live selection is the region, so fades follow a drag before the preview catches up.
                overlay.Segments.Add(_session.HasSelection ? new Vector2(_session.SelectionStart, _session.SelectionEnd) : new Vector2(0f, 1f));
                return overlay;
            }

            float frames = render.Frames;
            int kept = AudioEditPipeline.SecondsToFrames(_session.KeptPause, render.Frequency);
            foreach (AudioRenderSegment segment in render.Segments)
            {
                overlay.Segments.Add(new Vector2(segment.Start / frames, segment.ExportEnd / frames));
                if (segment.LoopTailFrames > 0) overlay.LoopTails.Add(new Vector2(segment.ExportEnd / frames, segment.End / frames));
                foreach (AudioFrameRange pause in segment.Pauses)
                {
                    if (pause.Length <= kept) continue;
                    int half = kept / 2;
                    overlay.RemovedPauses.Add(new Vector2((pause.Start + half) / frames, (pause.End - half) / frames));
                }
            }

            return overlay;
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            if (element != null) element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BindWaveform()
        {
            _waveform?.Bind(_session, _controller?.PeakCache, _controller?.SourceMetadata == null ? 1f : (float)_controller.SourceMetadata.Value.Duration);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyResponsiveState(evt.newRect.width);
        }

        internal void ApplyResponsiveState(float width)
        {
            if (_workspaceBody == null) return;
            _workspaceBody.EnableInClassList("at-compact", width < CompactBreakpoint);
            _workspaceBody.EnableInClassList("at-standard", width >= CompactBreakpoint && width < WideBreakpoint);
            _workspaceBody.EnableInClassList("at-wide", width >= WideBreakpoint);
            _workspace?.EnableInClassList("at-compact", width < CompactBreakpoint);
            _workspace?.EnableInClassList("at-standard", width >= CompactBreakpoint && width < WideBreakpoint);
            _workspace?.EnableInClassList("at-wide", width >= WideBreakpoint);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!_controller.HasAudio || _batchCancellation != null || IsEditingField(evt.target as VisualElement)) return;
            bool handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    _controller.TogglePlayback();
                    break;
                case KeyCode.Escape:
                    ClearSelection();
                    break;
                case KeyCode.Home:
                    _controller.Seek(0f);
                    break;
                case KeyCode.End:
                    _controller.Seek(1f);
                    break;
                case KeyCode.A when evt.actionKey:
                    SelectAll();
                    break;
                case KeyCode.LeftArrow:
                    NudgeActivePosition(-1f, evt);
                    break;
                case KeyCode.RightArrow:
                    NudgeActivePosition(1f, evt);
                    break;
                case KeyCode.PageUp when _queue.IsMulti:
                    _ = StepFileAsync(-1);
                    break;
                case KeyCode.PageDown when _queue.IsMulti:
                    _ = StepFileAsync(1);
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled) evt.StopPropagation();
        }

        private void NudgeActivePosition(float direction, KeyDownEvent evt)
        {
            float delta;
            if (evt.altKey) delta = 1f / Mathf.Max(1, _controller.SourceFrameCount);
            else delta = _waveform.PixelDeltaToNormalized(evt.shiftKey ? 10f : 1f);
            RecordSession("Nudge audio position");
            _controller.Seek(_session.Playhead + direction * delta);
        }

        private static bool IsEditingField(VisualElement target)
        {
            return target is TextField || target is FloatField || target is Slider || target is CurveField || target is ObjectField || target is DropdownField;
        }

        private void SelectAll()
        {
            RecordSession("Select full audio clip");
            _session.HasSelection = true;
            _session.SelectionStart = 0f;
            _session.SelectionEnd = 1f;
            _session.Playhead = 0f;
            StoreCurrentSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void ClearSelection()
        {
            if (!_session.HasSelection) return;
            RecordSession("Clear audio selection");
            _session.ClearSelection();
            StoreCurrentSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void SelectAudibleContent()
        {
            RecordSession("Select audible content");
            if (!_controller.SelectAudibleContent(out string message))
            {
                _effectStatus.text = message ?? "No audible region was found.";
                return;
            }
            StoreCurrentSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void ResetSession()
        {
            RecordSession("Reset Audio Tools session");
            _controller.Stop();
            _session.ResetEdits();
            StoreCurrentSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private async void SavePrimary()
        {
            if (_batchCancellation != null)
            {
                _batchCancellation.Cancel();
                return;
            }

            if (_queue.IsMulti)
            {
                await SaveAllAsync(false);
                return;
            }

            try
            {
                string explicitPath = null;
                string suggested = _controller.GetDefaultOutputPath(false);
                if (RequiresSavePanel(suggested))
                {
                    string defaultName = Path.GetFileNameWithoutExtension(_session.FileName ?? "audio") + ".wav";
                    explicitPath = EditorUtility.SaveFilePanel("Save Audio as WAV", Path.GetDirectoryName(suggested ?? string.Empty) ?? string.Empty, defaultName, "wav");
                    if (string.IsNullOrEmpty(explicitPath)) return;
                }
                _controller.SaveOutputs(explicitPath);
                _batchStatus = null;
                RefreshView();
                await Task.Yield();
            }
            catch (Exception exception)
            {
                ShowExportError(exception);
            }
        }

        private async void ReplaceOriginal()
        {
            if (_queue.IsMulti)
            {
                await SaveAllAsync(true);
                return;
            }

            if (!CanReplaceOriginal()) return;
            string path = _controller.GetDefaultOutputPath(true);
            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Original WAV?",
                "This writes the current result to:\n\n" + path + "\n\n" + BackupHint,
                "Replace WAV",
                "Cancel");
            if (!confirmed) return;
            try
            {
                _controller.ReplaceOriginal();
                _backupCount = AudioReplaceBackup.LatestCount();
                await _controller.InitializeAsync(new FileAudioSource(path), Path.GetDirectoryName(path), false);
                if (_controller == null) return;
                _queue.Clear();
                if (_controller.HasAudio) _queue.Add(AudioQueueEntry.FromFile(path));
                _queueSignature = null;
                BindWaveform();
                RefreshView();
            }
            catch (Exception exception)
            {
                ShowExportError(exception);
            }
        }

        private async Task SaveAllAsync(bool replace)
        {
            StoreCurrentSelection();
            AudioBatchOptions options = CreateBatchOptions(replace, true);
            if (options == null) return;
            List<AudioQueueEntry> entries = replace ? _queue.Entries.FindAll(entry => entry.IsProjectWav && !entry.Replaced) : GetPendingEntries(options);
            if (entries.Count == 0) return;
            if (replace && !EditorUtility.DisplayDialog("Replace Original WAVs?",
                    "This overwrites " + entries.Count + " WAV files in the project with their results.\n\n" + BackupHint, "Replace " + entries.Count + " WAVs", "Cancel"))
                return;

            AudioBatchReport report = await RunBatchAsync(entries, options, true);
            if (!replace || _controller == null) return;
            bool finished = report != null && !report.Cancelled && report.Failed == 0 && _queue.Entries.TrueForAll(entry => !entry.IsProjectWav || entry.Replaced);
            if (!finished)
            {
                _batchStatus = (_batchStatus ?? string.Empty) + " Replace again to finish the remaining files.";
                _queueSignature = null;
                RefreshView();
                return;
            }

            // Every original now holds its result, so edits and per-file selections start over, as after a single Replace.
            foreach (AudioQueueEntry entry in _queue.Entries)
            {
                entry.HasSelection = false;
                entry.SelectionStart = 0f;
                entry.SelectionEnd = 1f;
                entry.ResetResult();
            }

            _session.ClearSelection();
            await OpenQueueEntryAsync(_queue.Index, true);
        }

        private AudioBatchOptions CreateBatchOptions(bool replace, bool askForFolder)
        {
            AudioBatchOptions options = new AudioBatchOptions
            {
                Format = AudioToolsPreferences.OutputFormat,
                ReplaceOriginals = replace,
                DestinationFolder = string.IsNullOrEmpty(_queue.DestinationFolder) ? null : _queue.DestinationFolder,
                CopyImportSettings = true
            };
            if (replace || options.DestinationFolder != null || !askForFolder) return options;
            if (_queue.Entries.TrueForAll(entry => entry.IsInProject || AudioOutputWriter.IsInsideProjectAssets(entry.ExportFolder))) return options;

            string folder = EditorUtility.OpenFolderPanel("Save Audio Copies To", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(folder)) return null;
            _queue.DestinationFolder = folder;
            options.DestinationFolder = folder;
            return options;
        }

        private List<AudioQueueEntry> GetPendingEntries(AudioBatchOptions options)
        {
            AudioEditSettings settings = _session.CreateSettings();
            List<AudioQueueEntry> pending = _queue.Entries.FindAll(entry =>
                entry.Status != AudioQueueStatus.Saved || entry.ResultKey != AudioBatchRunner.CreateResultKey(settings.WithSelection(entry.HasSelection, entry.SelectionStart, entry.SelectionEnd), options, true));
            return pending.Count > 0 ? pending : new List<AudioQueueEntry>(_queue.Entries);
        }

        private int CountPendingEntries()
        {
            AudioBatchOptions options = CreateBatchOptions(false, false);
            return GetPendingEntries(options).Count;
        }

        private string CreateEntryKey(AudioQueueEntry entry, bool write)
        {
            AudioEditSettings settings = _session.CreateSettings().WithSelection(entry.HasSelection, entry.SelectionStart, entry.SelectionEnd);
            return AudioBatchRunner.CreateResultKey(settings, CreateBatchOptions(false, false), write);
        }

        private async Task<AudioBatchReport> RunBatchAsync(List<AudioQueueEntry> entries, AudioBatchOptions options, bool write)
        {
            if (entries == null || entries.Count == 0 || options == null || _batchCancellation != null) return null;
            AudioBatchReport report = null;
            StoreCurrentSelection();
            _controller.Stop();
            _batchCancellation = new CancellationTokenSource();
            AudioEditSettings settings = _session.CreateSettings();
            try
            {
                RefreshView();
                report = await AudioBatchRunner.RunAsync(entries, settings, options, write, (index, total, name) =>
                {
                    _batchStatus = name == null ? null : (write ? "Saving " : "Analyzing ") + (index + 1) + " of " + total + ": " + name;
                    _queueSignature = null;
                    RefreshView();
                }, _batchCancellation.Token);
                _batchStatus = DescribeReport(report, write);
                if (!string.IsNullOrEmpty(report.BackupFolder)) _backupCount = AudioReplaceBackup.LatestCount();
                if (write && report.Saved > 0 && AudioToolsPreferences.RegisterSaveAndCheckRatePrompt() && _controller != null) _controller.RatePromptPending = true;
            }
            catch (Exception exception)
            {
                _batchStatus = "Batch failed: " + exception.Message;
                Debug.LogError("Audio Tools batch failed: " + exception);
            }
            finally
            {
                _batchCancellation.Dispose();
                _batchCancellation = null;
                _queueSignature = null;
                RefreshView();
            }

            return report;
        }

        private static string DescribeReport(AudioBatchReport report, bool write)
        {
            string text = write ? "Saved " + report.Written.Count + (report.Written.Count == 1 ? " file" : " files") : "Analyzed " + report.Analyzed + (report.Analyzed == 1 ? " file" : " files");
            if (report.Failed > 0) text += ", " + report.Failed + " failed";
            if (report.Cancelled) text += ", then stopped";
            return text + ".";
        }

        private async Task StepFileAsync(int direction)
        {
            if (!_queue.IsMulti || _batchCancellation != null) return;
            int index = Mathf.Clamp(_queue.Index + direction, 0, _queue.Entries.Count - 1);
            if (index != _queue.Index) await OpenQueueEntryAsync(index, false);
        }

        private async Task OpenQueueEntryAsync(int index, bool resetEdits)
        {
            if (_controller == null || index < 0 || index >= _queue.Entries.Count) return;
            StoreCurrentSelection();
            // Stopping writes the old file's position into the session, so it has to happen before the next file's state.
            _controller.Stop();
            _queue.Index = index;
            AudioQueueEntry entry = _queue.Entries[index];
            _batchStatus = null;
            if (resetEdits)
            {
                await _controller.InitializeAsync(entry.CreateSource(), entry.ExportFolder, true);
            }
            else
            {
                _session.HasSelection = entry.HasSelection;
                _session.SelectionStart = entry.SelectionStart;
                _session.SelectionEnd = entry.SelectionEnd;
                _session.Playhead = entry.HasSelection ? entry.SelectionStart : 0f;
                _session.Zoom = 1f;
                _session.ViewportStart = 0f;
                // Undo steps of the previous file would rewrite this file's selection.
                Undo.ClearUndo(_session);
                await _controller.OpenKeepingEditsAsync(entry.CreateSource(), entry.ExportFolder, true);
            }

            _queueSignature = null;
            BindWaveform();
            RefreshView();
        }

        private async Task CloseFileAsync(int index)
        {
            if (_batchCancellation != null || index < 0 || index >= _queue.Entries.Count) return;
            bool current = index == _queue.Index;
            StoreCurrentSelection();
            _queue.RemoveAt(index);
            _queueSignature = null;
            if (_queue.Entries.Count == 0)
            {
                _controller.ClearSource();
                RefreshView();
                return;
            }

            if (current) await OpenQueueEntryAsync(_queue.Index, false);
            else RefreshView();
        }

        private void AddClips()
        {
            if (_batchCancellation != null) return;
            List<string> paths = AudioContextMenu.CollectFromSelection(Selection.objects);
            if (paths.FindAll(path => _queue.IndexOf(path) < 0).Count == 0)
            {
                string start = string.IsNullOrEmpty(_session.MaterializedPath) ? Application.dataPath : Path.GetDirectoryName(_session.MaterializedPath);
                string folder = EditorUtility.OpenFolderPanel("Add Audio Folder", start, string.Empty);
                if (string.IsNullOrEmpty(folder)) return;
                paths = AudioContextMenu.Collect(new[] { folder });
            }

            if (_queue.Entries.Count == 0 && _controller.HasAudio)
            {
                _queue.Add(new AudioQueueEntry
                {
                    Path = _session.MaterializedPath,
                    FileName = _session.FileName,
                    IsInProject = _session.IsInProject,
                    ProjectPath = _session.ProjectPath,
                    ExportFolder = _session.ExportFolder
                });
                _queue.Index = 0;
                StoreCurrentSelection();
            }

            int added = 0;
            foreach (string path in paths)
            {
                if (_queue.Add(AudioQueueEntry.FromFile(path))) added++;
            }

            _batchStatus = added == 0 ? "These files are already open." : "Added " + added + (added == 1 ? " file" : " files") + ". The current settings apply to all of them.";
            _queueSignature = null;
            RefreshView();
        }

        private void StoreCurrentSelection()
        {
            AudioQueueEntry entry = _queue?.Current;
            if (entry == null || _session == null || !IsLoadedFile(entry)) return;
            entry.HasSelection = _session.HasSelection;
            entry.SelectionStart = _session.SelectionStart;
            entry.SelectionEnd = _session.SelectionEnd;
        }

        // The current entry can briefly differ from the loaded file while switching files or after closing one.
        private bool IsLoadedFile(AudioQueueEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Path) || string.IsNullOrEmpty(_session.MaterializedPath)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(entry.Path), Path.GetFullPath(_session.MaterializedPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                return false;
            }
        }

        private async void RestoreReplacedFiles()
        {
            int count = AudioReplaceBackup.LatestCount();
            if (count == 0) return;
            if (!EditorUtility.DisplayDialog("Restore Replaced Files?", "This puts back the " + count + (count == 1 ? " original" : " originals") + " that the last Replace overwrote.", "Restore", "Cancel")) return;
            try
            {
                List<string> restored = AudioReplaceBackup.RestoreLatest();
                List<AudioImportRequest> imports = new List<AudioImportRequest>();
                foreach (string path in restored) imports.Add(new AudioImportRequest(path, null));
                AudioOutputWriter.Import(imports, false);
                _backupCount = AudioReplaceBackup.LatestCount();
                _batchStatus = "Restored " + restored.Count + (restored.Count == 1 ? " file." : " files.");
                bool currentRestored = restored.Exists(path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(_session.MaterializedPath ?? string.Empty), StringComparison.OrdinalIgnoreCase));
                if (currentRestored && _controller != null) await _controller.RestoreAsync(true);
                RefreshView();
            }
            catch (Exception exception)
            {
                ShowExportError(exception);
            }
        }

        private void ShowExportError(Exception exception)
        {
            _statusBox.messageType = HelpBoxMessageType.Error;
            _statusBox.text = "Export failed: " + exception.Message;
            _statusBox.style.display = DisplayStyle.Flex;
            Debug.LogError("Audio Tools export failed: " + exception);
        }

        private bool CanReplaceOriginal()
        {
            return _session.IsInProject && !string.IsNullOrEmpty(_session.FileName) && _session.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && _controller.GetOutputCount() == 1;
        }

        private string GetPrimaryExportLabel()
        {
            if (_session.IsInProject) return "Save Copy";
            return AudioOutputWriter.IsInsideProjectAssets(_session.ExportFolder) ? "Import WAV" : "Save As WAV...";
        }

        private bool RequiresSavePanel(string path)
        {
            return !_session.IsInProject && !AudioOutputWriter.IsInsideProjectAssets(_session.ExportFolder) || string.IsNullOrEmpty(path);
        }

        private void LoadProjectClip(AudioClip clip)
        {
            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath)) return;
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            Init(new FileAudioSource(fullPath), Path.GetDirectoryName(fullPath));
        }

        private void BrowseForSource()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Choose Audio File",
                string.IsNullOrEmpty(_session.MaterializedPath) ? Application.dataPath : Path.GetDirectoryName(_session.MaterializedPath),
                new[] { "Audio files", "wav,mp3,ogg,aiff,aif,flac", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            Init(new FileAudioSource(path));
        }

        private void RegisterDragAndDrop(VisualElement root)
        {
            root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!HasDroppableAudio()) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            root.RegisterCallback<DragPerformEvent>(evt =>
            {
                List<string> paths = GetDroppedAudioPaths();
                if (paths.Count == 0) return;
                DragAndDrop.AcceptDrag();
                if (paths.Count == 1) Init(new FileAudioSource(paths[0]));
                else InitMany(paths);
                evt.StopPropagation();
            });
        }

        private static bool HasDroppableAudio()
        {
            if (DragAndDrop.paths == null) return false;
            foreach (string path in DragAndDrop.paths)
            {
                string full = ToFullPath(path);
                if (AudioContextMenu.IsAudioFile(full) || Directory.Exists(full)) return true;
            }

            return false;
        }

        private static List<string> GetDroppedAudioPaths()
        {
            List<string> paths = new List<string>();
            if (DragAndDrop.paths == null) return paths;
            foreach (string path in DragAndDrop.paths) paths.Add(ToFullPath(path));
            return AudioContextMenu.Collect(paths);
        }

        private static string ToFullPath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, path);
        }

        private void OnUndoRedo()
        {
            _session.NormalizeSelection();
            StoreCurrentSelection();
            _controller.ScheduleProcessingIfStale();
            RefreshView();
        }

        private void RecordSession(string name)
        {
            Undo.RecordObject(_session, name);
            EditorUtility.SetDirty(_session);
            _batchStatus = null;
        }

        private string GetEffectStatus()
        {
            if (!_controller.HasAudio) return string.Empty;
            AudioRenderResult render = _controller.Render;
            if (render != null)
            {
                if (_session.TrimSilence && render.NoAudibleContent) return "No audible content at this threshold, so nothing is trimmed. Lower the silence threshold.";
                if (render.PhaseCancellation) return "Mixing these channels to mono cancels part of the sound. Try Left channel or Right channel.";
                if (render.AnyLimitedByCeiling) return "The peak ceiling keeps the level below the loudness target.";
                if (render.LoopShortened) return "The loop crossfade is shortened to half of the clip.";
            }

            float regionDuration = _controller.GetSelectionDuration();
            bool clamped = !_session.SeamlessLoop && (_session.FadeIn && _session.FadeInDuration > regionDuration || _session.FadeOut && _session.FadeOutDuration > regionDuration);
            if (clamped) return "Fade duration is shortened to fit the active region.";
            if (_session.SeamlessLoop && (_session.FadeIn || _session.FadeOut)) return "Fades stay off while Seamless Loop is on.";
            if (!_session.ChangesSamples && !_session.TrimSilence) return "No enhancements enabled. The source waveform is unchanged.";
            return "Enhancements preview automatically after each change.";
        }

        // Match Source reads the file header, so the result is cached instead of read on every repaint.
        private WavSampleFormat GetResolvedFormat()
        {
            AudioOutputFormat setting = AudioToolsPreferences.OutputFormat;
            if (setting != _resolvedFormatSetting || _session.MaterializedPath != _resolvedFormatPath)
            {
                _resolvedFormatSetting = setting;
                _resolvedFormatPath = _session.MaterializedPath;
                _resolvedFormat = AudioWavWriter.Resolve(setting, _session.MaterializedPath);
            }

            return _resolvedFormat;
        }

        private string GetExportSummary(EditorAudioMetadata metadata)
        {
            WavSampleFormat format = GetResolvedFormat();
            string layout = string.Empty;
            if (_session.OutputChannels != AudioOutputChannels.Keep && metadata.Channels > 1) layout += " · " + _channelChoices[(int)_session.OutputChannels];
            if (_session.OutputSampleRate > 0) layout += " · " + _session.OutputSampleRate.ToString(CultureInfo.InvariantCulture) + " Hz";
            if (_queue.IsMulti) return _queue.Entries.Count + " files · WAV / " + AudioWavWriter.Describe(format) + layout;

            int outputs = _controller.GetOutputCount();
            string scope = outputs > 1 ? outputs + " clips" : _session.SeamlessLoop ? "Loop" : _session.TrimSilence ? "Trimmed" : _session.HasSelection ? "Selection" : "Full clip";
            return scope + " · " + FormatTime(_controller.GetOutputDuration()) + " · WAV / " + AudioWavWriter.Describe(format) + layout;
        }

        private static float Milliseconds(float seconds)
        {
            return Mathf.Round(seconds * 10000f) / 10f;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return minutes + ":" + remainder.ToString("00.000", CultureInfo.InvariantCulture);
        }

        private static string FormatPeak(float amplitude)
        {
            return Mathf.RoundToInt(amplitude * 100f) + "%  ·  " + FormatDb(amplitude);
        }

        private static string FormatDb(float amplitude)
        {
            return amplitude <= 0.000001f ? "-∞ dB" : (20f * Mathf.Log10(amplitude)).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        }

        private static float AmplitudeToDb(float amplitude)
        {
            return amplitude <= 0.0001f ? -80f : 20f * Mathf.Log10(amplitude);
        }

        // Matches the 0.1 dB precision of the level readouts; the slider's text field cannot apply a format string.
        private static float RoundDb(float decibels)
        {
            return Mathf.Round(decibels * 10f) / 10f;
        }

        private static float DbToAmplitude(float decibels)
        {
            return Mathf.Pow(10f, decibels / 20f);
        }

        private static string GetCompactPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(projectRoot) && normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(projectRoot.Length + 1);
            }
            if (normalized.Length <= 72) return normalized;
            string fileName = Path.GetFileName(normalized);
            string parent = Path.GetFileName(Path.GetDirectoryName(normalized));
            return ".../" + parent + "/" + fileName;
        }

    }
}
