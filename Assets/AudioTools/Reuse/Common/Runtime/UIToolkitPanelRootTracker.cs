using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Tracks the root visual element of one UI Toolkit panel host. See <see cref="UIToolkitPanelHost"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="UIDocument"/> exposes its root directly. <c>PanelRenderer</c> publishes its root only through
    /// reload callbacks, which run when the tree is created and not again when the renderer is re-enabled, so the
    /// tracker keeps the latest root and re-registers while it has none and the renderer is active.
    /// Call <see cref="Release"/> when the owner is disabled so no callback stays registered on the host.
    /// </remarks>
    public sealed class UIToolkitPanelRootTracker
    {
        Component _host;
        int _rootVersion;
#if UNITY_6000_5_OR_NEWER
        PanelRenderer _panelRenderer;
        VisualElement _panelRendererRoot;
        readonly PanelRenderer.VersionedUIReloadCallback _reloadCallback;
#endif

        public UIToolkitPanelRootTracker()
        {
#if UNITY_6000_5_OR_NEWER
            _reloadCallback = OnUIReload;
#endif
        }

        public Component Host => _host;

        /// <summary>
        /// The latest reload version reported by the host. Read <see cref="Root"/> first to refresh a pending root.
        /// Documents expose no reload version and return zero; their root identity identifies a rebuilt tree.
        /// </summary>
        public int RootVersion => _rootVersion;

        /// <summary>The host's current root, or null when the host is missing or has not built its tree.</summary>
        public VisualElement Root
        {
            get
            {
                if (_host == null) return null;
                if (_host is UIDocument document) return document.rootVisualElement;
#if UNITY_6000_5_OR_NEWER
                if (_panelRenderer != null)
                {
                    if (_panelRendererRoot == null && UIToolkitPanelHost.IsActive(_panelRenderer))
                    {
                        // Registration reports the current root immediately once it is attached to a panel.
                        _panelRenderer.UnregisterUIReloadCallback(_reloadCallback);
                        _panelRenderer.RegisterUIReloadCallback(_reloadCallback);
                    }

                    return _panelRendererRoot;
                }
#endif
                return null;
            }
        }

        /// <exception cref="System.ArgumentException">The host is not null and not a supported panel host.</exception>
        public void SetHost(Component host)
        {
            UIToolkitPanelHost.EnsureSupported(host, nameof(host));
            if ((object)_host == host) return;

            Release();
            _host = host;
#if UNITY_6000_5_OR_NEWER
            _panelRenderer = host as PanelRenderer;
            if (_panelRenderer != null)
                _panelRenderer.RegisterUIReloadCallback(_reloadCallback);
#endif
        }

        public void Release()
        {
#if UNITY_6000_5_OR_NEWER
            if (_panelRenderer != null)
                _panelRenderer.UnregisterUIReloadCallback(_reloadCallback);

            _panelRenderer = null;
            _panelRendererRoot = null;
#endif
            _host = null;
            _rootVersion = 0;
        }

#if UNITY_6000_5_OR_NEWER
        void OnUIReload(PanelRenderer panelRenderer, VisualElement rootElement, int version)
        {
            if ((object)panelRenderer == _panelRenderer)
            {
                _panelRendererRoot = rootElement;
                _rootVersion = version;
            }
        }
#endif
    }
}
