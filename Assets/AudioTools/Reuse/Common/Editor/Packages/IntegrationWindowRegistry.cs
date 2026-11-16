using System;
using System.Collections.Generic;
using UnityEditor;

namespace ImpossibleRobert.Common.Editor.Packages
{
    /// <summary>Connects optional integration setup windows without references between their owning tools.</summary>
    [InitializeOnLoad]
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class IntegrationWindowRegistry
    {
        static readonly Dictionary<string, Action> s_windows = new Dictionary<string, Action>(StringComparer.Ordinal);

        static IntegrationWindowRegistry()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
        }

        /// <summary>Registers a window opener under the assembly name of the optional integration it configures.</summary>
        public static void Register(string integrationAssemblyName, Action openWindow)
        {
            if (string.IsNullOrWhiteSpace(integrationAssemblyName))
                throw new ArgumentException("An integration assembly name is required.", nameof(integrationAssemblyName));
            s_windows[integrationAssemblyName] = openWindow ?? throw new ArgumentNullException(nameof(openWindow));
        }

        /// <summary>Reports whether an installed tool can show setup for the specified integration.</summary>
        public static bool HasWindow(string integrationAssemblyName)
        {
            return integrationAssemblyName != null && s_windows.ContainsKey(integrationAssemblyName);
        }

        /// <summary>Opens the registered setup window; returns false when its owning tool is absent.</summary>
        public static bool TryOpen(string integrationAssemblyName)
        {
            if (integrationAssemblyName == null || !s_windows.TryGetValue(integrationAssemblyName, out Action openWindow))
                return false;
            openWindow();
            return true;
        }

        static void Clear()
        {
            s_windows.Clear();
            AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            EditorApplication.quitting -= Clear;
        }
    }
}
