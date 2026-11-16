using System;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Resolves the scene components that host a runtime UI Toolkit panel: <see cref="UIDocument"/> and,
    /// on Unity 6.5 or newer, <c>PanelRenderer</c>.
    /// </summary>
    /// <remarks>
    /// Tools store a host as a <see cref="Component"/> so one serialized reference covers every supported
    /// host type without referencing <c>PanelRenderer</c> on older Unity versions.
    /// </remarks>
    public static class UIToolkitPanelHost
    {
#if UNITY_6000_5_OR_NEWER
        public const string SupportedTypesDescription = "UIDocument or PanelRenderer";
#else
        public const string SupportedTypesDescription = "UIDocument";
#endif

        public static bool IsSupported(Component component)
        {
            if (component == null) return false;
            if (component is UIDocument) return true;
#if UNITY_6000_5_OR_NEWER
            if (component is PanelRenderer) return true;
#endif
            return false;
        }

        /// <summary>Returns the panel host on the GameObject, preferring <see cref="UIDocument"/>.</summary>
        public static Component Find(GameObject gameObject)
        {
            if (gameObject == null) return null;
            if (gameObject.TryGetComponent(out UIDocument document)) return document;
#if UNITY_6000_5_OR_NEWER
            if (gameObject.TryGetComponent(out PanelRenderer panelRenderer)) return panelRenderer;
#endif
            return null;
        }

        /// <summary>
        /// Returns the candidate when it is a panel host, otherwise the panel host on the candidate's GameObject.
        /// </summary>
        /// <remarks>
        /// Use this to repair inspector assignments, where dropping a GameObject on a component field assigns its Transform.
        /// </remarks>
        public static Component Normalize(Object candidate)
        {
            if (candidate is Component component)
                return IsSupported(component) ? component : component != null ? Find(component.gameObject) : null;

            return Find(candidate as GameObject);
        }

        /// <exception cref="ArgumentException">The host is not null and not a supported panel host.</exception>
        public static void EnsureSupported(Component host, string parameterName)
        {
            if (host == null || IsSupported(host)) return;

            throw new ArgumentException(
                host.GetType().Name + " is not a UI Toolkit panel host. Use a " + SupportedTypesDescription + ".",
                parameterName);
        }

        public static PanelSettings GetPanelSettings(Component host)
        {
            if (host == null) return null;
            if (host is UIDocument document) return document.panelSettings;
#if UNITY_6000_5_OR_NEWER
            if (host is PanelRenderer panelRenderer) return panelRenderer.panelSettings;
#endif
            return null;
        }

        public static VisualTreeAsset GetVisualTreeAsset(Component host)
        {
            if (host == null) return null;
            if (host is UIDocument document) return document.visualTreeAsset;
#if UNITY_6000_5_OR_NEWER
            if (host is PanelRenderer panelRenderer) return panelRenderer.visualTreeAsset;
#endif
            return null;
        }

        /// <summary>Returns whether the host component is enabled on an active GameObject.</summary>
        public static bool IsActive(Component host)
        {
            if (host == null) return false;
            if (host is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (host is Renderer renderer) return renderer.enabled && renderer.gameObject.activeInHierarchy;
            return host.gameObject.activeInHierarchy;
        }

        /// <summary>Enables or disables a supported host without changing its GameObject's active state.</summary>
        /// <exception cref="ArgumentException">The host is not a supported panel host.</exception>
        public static void SetEnabled(Component host, bool enabled)
        {
            EnsureSupported(host, nameof(host));
            if (host is Behaviour behaviour)
                behaviour.enabled = enabled;
            else if (host is Renderer renderer)
                renderer.enabled = enabled;
        }
    }
}
