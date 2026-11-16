using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonScrollViewState
    {
        private readonly Dictionary<string, Vector2> _offsets = new Dictionary<string, Vector2>();
        private readonly Dictionary<ScrollView, PendingRestore> _pendingRestores = new Dictionary<ScrollView, PendingRestore>();

        public void Capture(string key, ScrollView scrollView)
        {
            if (string.IsNullOrEmpty(key) || scrollView == null) return;

            if (_pendingRestores.TryGetValue(scrollView, out PendingRestore pending))
            {
                _offsets[key] = pending.Offset;
                pending.Cancel();
                _pendingRestores.Remove(scrollView);
                return;
            }

            _offsets[key] = scrollView.scrollOffset;
        }

        public void Restore(string key, ScrollView scrollView)
        {
            if (string.IsNullOrEmpty(key) || scrollView == null || !_offsets.TryGetValue(key, out Vector2 offset)) return;

            if (_pendingRestores.TryGetValue(scrollView, out PendingRestore previous)) previous.Cancel();
            PendingRestore pending = new PendingRestore(this, scrollView, offset);
            _pendingRestores[scrollView] = pending;
            pending.Schedule();
            scrollView.scrollOffset = offset;
        }

        public Vector2 GetOffset(string key)
        {
            return !string.IsNullOrEmpty(key) && _offsets.TryGetValue(key, out Vector2 offset)
                ? offset
                : Vector2.zero;
        }

        private sealed class PendingRestore
        {
            private readonly CommonScrollViewState _owner;
            private readonly ScrollView _scrollView;
            private IVisualElementScheduledItem _scheduled;

            public Vector2 Offset { get; }

            public PendingRestore(CommonScrollViewState owner, ScrollView scrollView, Vector2 offset)
            {
                _owner = owner;
                _scrollView = scrollView;
                Offset = offset;
            }

            public void Schedule()
            {
                _scheduled = _scrollView.schedule.Execute(Apply);
                _scrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _scrollView.RegisterCallback<AttachToPanelEvent>(OnAttached);
            }

            public void Cancel()
            {
                _scheduled.Pause();
                _scrollView.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _scrollView.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _scrollView.UnregisterCallback<AttachToPanelEvent>(OnAttached);
            }

            private void OnGeometryChanged(GeometryChangedEvent evt) => _scheduled.ExecuteLater(0);
            private void OnAttached(AttachToPanelEvent evt) => _scheduled.ExecuteLater(0);

            private void Apply()
            {
                if (!_owner._pendingRestores.TryGetValue(_scrollView, out PendingRestore current) || current != this) return;
                if (_scrollView.panel == null || !(_scrollView.contentViewport.resolvedStyle.height > 0f) ||
                    !(_scrollView.contentViewport.resolvedStyle.width > 0f)) return;

                // A tab can stay collapsed for multiple scheduler passes. Wait for layout without polling hidden views.
                _owner._pendingRestores.Remove(_scrollView);
                Cancel();
                _scrollView.scrollOffset = Offset;
            }
        }
    }
}
