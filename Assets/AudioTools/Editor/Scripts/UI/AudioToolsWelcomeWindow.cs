using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioTool
{
#if UNITY_6000_7_OR_NEWER
    // The controller owns editor callbacks and intentionally persists for the loaded-code lifetime.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal sealed partial class AudioToolsWelcomeWindow : EditorWindow
    {
        private const string PackageId = "com.wetzold.audio-tools";
        private const int WelcomeRevision = 1;
        private const string DocumentationUrl = "https://www.wetzold.com/tools/audiotools/docs/";

        private static readonly CommonWelcomeController<AudioToolsWelcomeWindow> WelcomeController =
            new CommonWelcomeController<AudioToolsWelcomeWindow>(PackageId, WelcomeRevision, OpenWindow);

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticWelcome()
        {
            WelcomeController.ScheduleAutomaticOpen();
        }

        [MenuItem(AudioToolsMenuPaths.Welcome, false, AudioToolsMenuPaths.WelcomePriority)]
        internal static void ShowWindow()
        {
            WelcomeController.OpenManually();
        }

        private static AudioToolsWelcomeWindow OpenWindow()
        {
            return CommonWelcomeWindow.ShowUtility<AudioToolsWelcomeWindow>("Welcome to Audio Tools");
        }

        private void OnEnable()
        {
            CommonWelcomeWindow.ApplyDefaultConstraints(this, "Welcome to Audio Tools");
        }

        public void CreateGUI()
        {
            CommonWelcomeContent content = new CommonWelcomeContent
            {
                Logo = CommonUIStyles.LoadTexture("AudioTools"),
                ProductName = "AUDIO TOOLS",
                Headline = "Polished audio, right inside Unity",
                Description =
                    "Trim silence, shape fades, normalize peak or loudness levels, make seamless loops, and save production-ready WAV files for one clip or a whole folder without switching tools.",
                SectionTitle = "A focused edit-to-export workflow",
                SectionDescription =
                    "Work visually and non-destructively, then save exactly the result you intend.",
                Steps = new[]
                {
                    new CommonWelcomeStep(
                        "Choose audio",
                        "Open Audio Editor, select an AudioClip, browse for a file, or use Assets/Edit Audio on clips or folders in the Project window."),
                    new CommonWelcomeStep(
                        "Shape the edit",
                        "Drag across the waveform, refine the handles, and remove quiet edges with Select Audible Content."),
                    new CommonWelcomeStep(
                        "Preview and export",
                        "Enable only the enhancements you need, listen to the result, then save a copy or deliberately replace a WAV.")
                },
                Accent = CommonWelcomeAccent.Cyan
            };

            Button open = CommonWelcomeWindow.CreateAction(
                "Open Audio Editor",
                "Open Audio Editor and choose an audio source.",
                () =>
                {
                    AudioEditorUI.ShowWindow();
                    Close();
                },
                true);
            Button documentation = CommonWelcomeWindow.CreateAction(
                "Read Documentation",
                "Open the Audio Tools documentation.",
                () => Application.OpenURL(DocumentationUrl));
            if (AudioToolsPreferences.IsStandaloneInstallation && AudioToolsPreferences.StoreLink != null)
            {
                Button rate = CommonWelcomeWindow.CreateAction(
                    "Rate on the Asset Store",
                    "Open the Audio Tools store page to leave a rating.",
                    AudioToolsPreferences.OpenStorePage);
                CommonWelcomeWindow.Build(rootVisualElement, content, open, documentation, rate);
                return;
            }

            CommonWelcomeWindow.Build(rootVisualElement, content, open, documentation);
        }
    }
}
