using System;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace AudioTool
{
    /// <summary>Per-user Audio Tools choices that outlive a single editing session.</summary>
#if UNITY_6000_7_OR_NEWER
    // The installation cache is editor-session state that stays valid for the loaded code.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class AudioToolsPreferences
    {
        private const string OutputFormatKey = "AudioTools.OutputFormat";
        private const string SavesKey = "AudioTools.SuccessfulSaves";
        private const string RatePromptKey = "AudioTools.RatePromptHandled";
        private const string ToolId = "AudioTools";
        private const int SavesBeforeRatePrompt = 3;

        private static bool? _standalone;

        internal static AudioOutputFormat OutputFormat
        {
            get
            {
                int value = EditorPrefs.GetInt(OutputFormatKey, (int)AudioOutputFormat.MatchSource);
                return Enum.IsDefined(typeof(AudioOutputFormat), value) ? (AudioOutputFormat)value : AudioOutputFormat.MatchSource;
            }
            set => EditorPrefs.SetInt(OutputFormatKey, (int)value);
        }

        /// <summary>Store page for ratings, or null when the catalog has none.</summary>
        internal static string StoreLink
        {
            get
            {
                string link = ToolCatalog.Load()?.GetTool(ToolId)?.assetStoreLink;
                return string.IsNullOrEmpty(link) ? null : link;
            }
        }

        /// <summary>False when this copy ships inside another Wetzold tool, which owns its own store page.</summary>
        internal static bool IsStandaloneInstallation
        {
            get
            {
                if (_standalone.HasValue) return _standalone.Value;
                bool standalone = true;
                foreach (string guid in AssetDatabase.FindAssets("AudioToolsPreferences t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (!path.EndsWith("/AudioToolsPreferences.cs", StringComparison.OrdinalIgnoreCase)) continue;
                    standalone = path.IndexOf("/Reuse/", StringComparison.OrdinalIgnoreCase) < 0;
                    break;
                }

                _standalone = standalone;
                return standalone;
            }
        }

        /// <summary>Counts a completed save and reports whether the one-time rating request should show now.</summary>
        internal static bool RegisterSaveAndCheckRatePrompt()
        {
            int saves = EditorPrefs.GetInt(SavesKey, 0) + 1;
            EditorPrefs.SetInt(SavesKey, saves);
            return ShouldShowRatePrompt(saves);
        }

        internal static bool ShouldShowRatePrompt()
        {
            return ShouldShowRatePrompt(EditorPrefs.GetInt(SavesKey, 0));
        }

        internal static void MarkRatePromptHandled()
        {
            EditorPrefs.SetBool(RatePromptKey, true);
        }

        internal static void OpenStorePage()
        {
            MarkRatePromptHandled();
            string link = StoreLink;
            if (link != null) Application.OpenURL(link);
        }

        private static bool ShouldShowRatePrompt(int saves)
        {
            return saves >= SavesBeforeRatePrompt && !EditorPrefs.GetBool(RatePromptKey, false) && StoreLink != null && IsStandaloneInstallation;
        }
    }
}
