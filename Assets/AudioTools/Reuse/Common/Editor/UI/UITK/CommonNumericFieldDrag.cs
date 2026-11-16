using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>Opt-in horizontal adjustment of idle numeric inputs, preserving native text editing after a click.</summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class CommonNumericFieldDrag : IDisposable
    {
        static readonly ConditionalWeakTable<VisualElement, CommonNumericFieldDrag> Attachments = new ConditionalWeakTable<VisualElement, CommonNumericFieldDrag>();
        const float DragThreshold = 4f;
        const string Hint = "Drag horizontally to adjust; click to type. While editing, drag to select text. Shift: faster; Alt: finer.";

        readonly VisualElement _field;
        readonly VisualElement _input;
        readonly VisualElement _label;
        readonly Func<bool> _canDrag;
        readonly Func<bool> _readOnly;
        readonly Func<double?> _read;
        readonly Action<double> _apply;
        readonly Action _restore;
        readonly Action _beginDrag;
        readonly Action<bool> _endDrag;
        readonly FloatField _floatCalculator;
        readonly DoubleField _doubleCalculator;
        VisualElement _root;
        PointerDownEvent _replayedDown;
        Vector2 _downPosition;
        Vector2 _lastPosition;
        EventModifiers _modifiers;
        int _clickCount;
        int _pointerId;
        double _start;
        double _lastApplied;
        string _baseTooltip;
        IVisualElementScheduledItem _startRequest;
        Vector2 _queuedPosition;
        DeltaSpeed _queuedSpeed;
        bool _waitingForCommit;
        bool _pending;
        bool _dragging;
        bool _disposed;
#if !UNITY_2023_2_OR_NEWER
        bool _suppressLegacyClick;
        bool _legacyReplayClick;
        VisualElement _legacyEventRoot;
#endif

        CommonNumericFieldDrag(VisualElement field, VisualElement input, VisualElement label,
            Func<bool> canDrag, Func<bool> readOnly, Func<double?> read, Action<double> apply, Action restore,
            Action beginDrag, Action<bool> endDrag, bool useFloat)
        {
            _field = field;
            _input = input;
            _label = label;
            _canDrag = canDrag;
            _readOnly = readOnly;
            _read = read;
            _apply = apply;
            _restore = restore;
            _beginDrag = beginDrag;
            _endDrag = endDrag;
            // Native fields provide Unity's sensitivity and rounding without changing the displayed field's typing delay.
            if (useFloat) _floatCalculator = new FloatField();
            else _doubleCalculator = new DoubleField();
            Attachments.Add(field, this);
            field.RegisterCallback<DetachFromPanelEvent>(OnDetach);
#if !UNITY_2023_2_OR_NEWER
            field.RegisterCallback<AttachToPanelEvent>(OnAttach);
            AttachLegacyRoot();
#else
            field.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
#endif
            UpdateTooltip(field.tooltip);
        }

        /// <summary>Updates the existing tooltip when a consumer changes whether its value is numeric.</summary>
        public void UpdateTooltip(string tooltip, bool numeric = true)
        {
            _baseTooltip = tooltip;
            _field.tooltip = numeric ? (string.IsNullOrEmpty(tooltip) ? Hint : tooltip + "\n" + Hint) : tooltip;
        }

        /// <summary>Enables dragging on a float input and its label. Owner callbacks can supply one Undo record per gesture.</summary>
        public static CommonNumericFieldDrag Attach(FloatField field, Action beginDrag = null,
            Action<bool> endDrag = null, Action<float> applyValue = null, Func<bool> canDrag = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (Attachments.TryGetValue(field, out CommonNumericFieldDrag existing)) return existing;
            float original = 0f;
            Action<float> apply = applyValue ?? (value => field.value = value);
            return new CommonNumericFieldDrag(field, field.Q(TextInputBaseField<float>.textInputUssName), field.labelElement,
                canDrag, () => field.isReadOnly || field.showMixedValue,
                () => { original = field.value; return IsFinite(original) ? original : (double?)null; },
                value => apply((float)value), () => apply(original), beginDrag, endDrag, true);
        }

        /// <summary>Enables numeric dragging without replacing a raw text binding. Cancellation restores its exact original text.</summary>
        public static CommonNumericFieldDrag Attach(TextField field, Func<bool> canDrag = null,
            Action beginDrag = null, Action<bool> endDrag = null, Action<string> applyValue = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (Attachments.TryGetValue(field, out CommonNumericFieldDrag existing)) return existing;
            string original = null;
            Action<string> apply = applyValue ?? (value => field.value = value);
            return new CommonNumericFieldDrag(field, field.Q(TextInputBaseField<string>.textInputUssName), field.labelElement,
                canDrag, () => field.isReadOnly || field.showMixedValue,
                () =>
                {
                    original = field.value;
                    return double.TryParse(original, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && IsFinite(value)
                        ? value : (double?)null;
                }, value => apply(value.ToString("R", CultureInfo.InvariantCulture)), () => apply(original),
                beginDrag, endDrag, false);
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        bool Eligible => _field.enabledInHierarchy && (_input == null || _input.enabledInHierarchy) && !_readOnly() && (_canDrag == null || _canDrag());

        void OnDown(PointerDownEvent evt)
        {
            if (ReferenceEquals(evt, _replayedDown))
            {
                _replayedDown = null;
                return;
            }
#if !UNITY_2023_2_OR_NEWER
            _suppressLegacyClick = _legacyReplayClick = false;
#endif
            if (_disposed || _pending || evt.button != 0 || evt.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            VisualElement hit = evt.target as VisualElement;
            bool label = _label != null && (hit == _label || _label.Contains(hit));
            if (!label && (_input == null || (hit != _input && !_input.Contains(hit)))) return;
            if (!Eligible) return;
            VisualElement focused = _field.panel?.focusController.focusedElement as VisualElement;
            if (!label && focused != null && (focused == _field || _field.Contains(focused))) return;
            double? number = _read();
            if (!number.HasValue) return;

            _start = _lastApplied = number.Value;
            _downPosition = _lastPosition = evt.position;
            _pointerId = evt.pointerId;
            _modifiers = evt.modifiers;
            _clickCount = evt.clickCount;
            _root = _field.panel.visualTree;
            _pending = true;
#if !UNITY_2023_2_OR_NEWER
            _suppressLegacyClick = true;
#endif
            _root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _field.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _field.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            _field.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            _field.CapturePointer(_pointerId);
            Consume(evt);
        }

        static void Consume(EventBase evt)
        {
            evt.StopImmediatePropagation();
#if !UNITY_2023_2_OR_NEWER
            // Older dispatchers generate compatibility mouse events unless their default action is prevented.
            evt.PreventDefault();
#endif
            // The deferred native click alone must enter Unity's post-dispatch focus and click tracking.
            evt.target = null;
        }

        void OnMove(PointerMoveEvent evt)
        {
            if (!_pending || evt.pointerId != _pointerId) return;
            if (!Eligible) { Finish(false); return; }
            Vector2 position = evt.position;
            _queuedPosition = position;
            _queuedSpeed = evt.shiftKey ? DeltaSpeed.Fast : evt.altKey ? DeltaSpeed.Slow : DeltaSpeed.Normal;
            if (_waitingForCommit) { Consume(evt); return; }
            Vector2 total = position - _downPosition;
            if (!_dragging)
            {
                if (Mathf.Abs(total.y) >= DragThreshold && Mathf.Abs(total.y) > Mathf.Abs(total.x))
                {
                    Finish(false);
                    return;
                }
                if (Mathf.Abs(total.x) < DragThreshold) { Consume(evt); return; }
                VisualElement focused = _field.panel.focusController.focusedElement as VisualElement;
                if (IsTextEditor(focused))
                {
                    // Blur and its value-change events are queued until pointer dispatch finishes. Begin on the next UI
                    // tick so both the native value and the owner's serialized document include the committed text.
                    _waitingForCommit = true;
                    focused.Blur();
                    _startRequest = _field.schedule.Execute(() =>
                    {
                        if (_pending && BeginAdjustment()) ApplyDelta(_queuedPosition, _queuedSpeed);
                    });
                    Consume(evt);
                    return;
                }
                if (!BeginAdjustment()) { Consume(evt); return; }
            }
            ApplyDelta(position, _queuedSpeed);
            Consume(evt);
        }

        static bool IsTextEditor(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element is TextInputBaseField<string> || element is TextInputBaseField<float> ||
                    element is TextInputBaseField<double> || element is TextInputBaseField<int> || element is TextInputBaseField<long>) return true;
            return false;
        }

        bool BeginAdjustment()
        {
            _startRequest?.Pause();
            _startRequest = null;
            _waitingForCommit = false;
            if (!_pending || _field.panel == null || !Eligible) { Finish(false); return false; }
            double? committed = _read();
            if (!committed.HasValue) { Finish(false); return false; }
            _start = _lastApplied = committed.Value;
            _dragging = true;
            _beginDrag?.Invoke();
            if (_floatCalculator != null) { _floatCalculator.value = (float)_start; _floatCalculator.StartDragging(); }
            else { _doubleCalculator.value = _start; _doubleCalculator.StartDragging(); }
            return true;
        }

        void ApplyDelta(Vector2 position, DeltaSpeed speed)
        {
            Vector3 delta = new Vector3(position.x - _lastPosition.x, 0f, 0f);
            _lastPosition = position;
            double next;
            if (_floatCalculator != null)
            {
                _floatCalculator.ApplyInputDeviceDelta(delta, speed, (float)_start);
                next = _floatCalculator.value;
            }
            else
            {
                _doubleCalculator.ApplyInputDeviceDelta(delta, speed, _start);
                next = _doubleCalculator.value;
            }
            if (IsFinite(next) && !next.Equals(_lastApplied))
            {
                _apply(next);
                _lastApplied = next;
            }
        }

        void OnUp(PointerUpEvent evt)
        {
            if (!_pending || evt.pointerId != _pointerId || evt.button != 0) return;
            bool click = !_dragging && !_waitingForCommit;
            VisualElement root = _root;
            Vector2 release = evt.position;
            if (_waitingForCommit && BeginAdjustment()) ApplyDelta(release, _queuedSpeed);
            Consume(evt);
            Finish(false);
            if (!click || _field.panel == null) return;

#if !UNITY_2023_2_OR_NEWER
            // Legacy dispatch restores the physical target before click tracking. Suppress that click, then reset its
            // count before the queued native replay so a first click is not mistaken for a double click.
            _legacyReplayClick = true;
            using (PointerCancelEvent cancel = PointerCancelEvent.GetPooled(evt)) root.SendEvent(cancel);
#endif
            // Only the deferred click reaches native focus/selection handlers. Actual drags never become text selections.
            using (PointerDownEvent down = PointerDownEvent.GetPooled(new Event
                   { type = EventType.MouseDown, mousePosition = _downPosition, button = 0, clickCount = _clickCount, modifiers = _modifiers }))
            {
                _replayedDown = down;
                root.SendEvent(down);
            }
            using (PointerUpEvent up = PointerUpEvent.GetPooled(new Event
                   { type = EventType.MouseUp, mousePosition = release, button = 0, clickCount = _clickCount, modifiers = _modifiers }))
                root.SendEvent(up);
        }

        void OnKey(KeyDownEvent evt)
        {
            if (_pending && evt.keyCode == KeyCode.Escape)
            {
                evt.StopImmediatePropagation();
                Finish(true);
            }
        }

        void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_pending && evt.pointerId == _pointerId) Finish(false);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            Finish(false);
#if !UNITY_2023_2_OR_NEWER
            DetachLegacyRoot();
#endif
        }

#if !UNITY_2023_2_OR_NEWER
        void OnAttach(AttachToPanelEvent evt) => AttachLegacyRoot();

        void AttachLegacyRoot()
        {
            VisualElement root = _field.panel?.visualTree;
            if (root == _legacyEventRoot) return;
            DetachLegacyRoot();
            _legacyEventRoot = root;
            if (root == null) return;
            // Legacy BaseField is a composite root: its own callbacks see a retargeted field after native leaf handlers.
            // The panel's trickle phase sees the original input/label before that retargeting.
            root.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(OnLegacyClick, TrickleDown.TrickleDown);
        }

        void DetachLegacyRoot()
        {
            if (_legacyEventRoot == null) return;
            _legacyEventRoot.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _legacyEventRoot.UnregisterCallback<ClickEvent>(OnLegacyClick, TrickleDown.TrickleDown);
            _legacyEventRoot = null;
        }

        void OnLegacyClick(ClickEvent evt)
        {
            if (!_suppressLegacyClick) return;
            VisualElement hit = evt.target as VisualElement;
            if (hit != _field && (hit == null || !_field.Contains(hit))) return;
            // The consumed physical release targets this capturing field; the replay targets its native text or label.
            // Keep suppression across queued events, whose click notifications can arrive after the replayed down.
            if (_legacyReplayClick && !ReferenceEquals(evt.target, _field))
            {
                _legacyReplayClick = false;
                return;
            }
            evt.StopImmediatePropagation();
        }
#endif

        void Finish(bool canceled)
        {
            if (!_pending) return;
            _startRequest?.Pause();
            _startRequest = null;
            _waitingForCommit = false;
            bool dragged = _dragging;
            _pending = _dragging = false;
            _root.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _field.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _field.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _root.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            _field.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            _field.ReleasePointer(_pointerId);
            try
            {
                if (dragged && canceled) _restore();
            }
            finally
            {
                if (dragged)
                {
                    _floatCalculator?.StopDragging();
                    _doubleCalculator?.StopDragging();
                    _endDrag?.Invoke(canceled);
                }
                _root = null;
            }
        }

        /// <summary>Removes this opt-in interaction and ends any captured gesture at its last applied value.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Finish(false);
            _disposed = true;
            _field.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
#if !UNITY_2023_2_OR_NEWER
            _field.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            DetachLegacyRoot();
#else
            _field.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
#endif
            _field.tooltip = _baseTooltip;
            Attachments.Remove(_field);
        }
    }
}
