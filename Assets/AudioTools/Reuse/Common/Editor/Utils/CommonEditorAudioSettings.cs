using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ImpossibleRobert.Common
{
    /// <summary>Checks the project audio setting used by Unity audio loading and editor previews.</summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class CommonEditorAudioSettings
    {
        public const string DisabledMessage = "Unity audio is disabled. Open Project Settings > Audio, clear Disable Unity Audio, then retry.";

        private static bool _warningLogged;

        /// <summary>Gets whether the project has disabled Unity's built-in audio system.</summary>
        public static bool IsDisabled
        {
            get
            {
                Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
                if (settings.Length == 0 || settings[0] == null) return false;

                using (SerializedObject serialized = new SerializedObject(settings[0]))
                {
                    SerializedProperty disabled = serialized.FindProperty("m_DisableAudio");
                    return disabled != null && disabled.boolValue;
                }
            }
        }

        /// <summary>Returns true when audio is disabled and logs one warning until an enabled check is observed.</summary>
        public static bool WarnIfDisabled()
        {
            if (!IsDisabled)
            {
                _warningLogged = false;
                return false;
            }

            if (!_warningLogged)
            {
                _warningLogged = true;
                Debug.LogWarning(DisabledMessage);
            }
            return true;
        }

        /// <summary>Opens the project's Audio settings without changing them.</summary>
        public static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Audio");
        }
    }
}
