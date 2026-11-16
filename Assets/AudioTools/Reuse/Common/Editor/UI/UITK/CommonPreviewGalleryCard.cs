using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>Shared preview card presentation and input, without texture ownership or catalog state.</summary>
    public sealed class CommonPreviewGalleryCard : VisualElement
    {
        const string ActionsVisibleClass = "common-preview-gallery-card--actions-visible";

        readonly Action _select;
        readonly Action _activate;
        VisualElement _badges;
        VisualElement _actions;
        float _previewRatio;

        public Image Preview { get; }
        public Label Title { get; }
        public Label Description { get; }

        /// <summary>The badge row below the title, or null until the first <see cref="AddBadge"/>.</summary>
        public VisualElement Badges => _badges;

        /// <summary>The action rail over the preview, or null until <see cref="SetActions"/>.</summary>
        public VisualElement Actions => _actions;

        public CommonPreviewGalleryCard(string title, string description, Action select, Action activate)
        {
            _select = select;
            _activate = activate;
            focusable = true;
            AddToClassList("common-inspector-section");
            AddToClassList("common-preview-gallery-card");
            Preview = new Image { name = "common-preview-gallery-card-preview", scaleMode = ScaleMode.ScaleToFit };
            Preview.AddToClassList("common-preview-gallery-card__preview");
            Preview.RegisterCallback<GeometryChangedEvent>(evt => FitPreviewHeight(evt.newRect.width));
            Add(Preview);
            Title = CommonUITK.CreateLabel(title, "common-preview-gallery-card__title");
            Add(Title);
            Description = CommonInspectorElements.CreateMutedText(description);
            Description.AddToClassList("common-preview-gallery-card__description");
            Add(Description);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if ((evt.button != 0 && evt.button != 1) || IsWithinActions(evt.target))
                    return;
                Focus();
                _select?.Invoke();
                if (evt.button == 0 && evt.clickCount >= 2)
                {
                    _activate?.Invoke();
                    evt.StopPropagation();
                }
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (IsWithinActions(evt.target))
                    return;
                if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                _select?.Invoke();
                if (evt.keyCode != KeyCode.Space)
                    _activate?.Invoke();
                CommonUITK.ConsumeEvent(evt);
            });
            RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (IsWithinActions(evt.target))
                    return;
                _select?.Invoke();
                _activate?.Invoke();
                evt.StopPropagation();
            });
        }

        public void SetSelected(bool selected)
        {
            EnableInClassList("common-preview-gallery-card--selected", selected);
        }

        /// <summary>
        /// Sizes the card to <paramref name="width"/> and its preview to the ratio of <paramref name="previewHeight"/> to that
        /// width. The ratio holds at the width the preview gets inside the card's padding and border, so an image of that
        /// ratio fills the preview without bars.
        /// </summary>
        public void SetPresentation(float width, float previewHeight, bool showDescription)
        {
            style.width = width;
            _previewRatio = width > 0f ? previewHeight / width : 0f;
            float previewWidth = Preview.layout.width;
            if (float.IsNaN(previewWidth) || previewWidth <= 0f)
            {
                Preview.style.height = previewHeight;
            }
            else
            {
                FitPreviewHeight(previewWidth);
            }
            Description.style.display = showDescription && !string.IsNullOrWhiteSpace(Description.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        void FitPreviewHeight(float previewWidth)
        {
            if (_previewRatio <= 0f || float.IsNaN(previewWidth) || previewWidth <= 0f)
            {
                return;
            }
            float height = Mathf.Round(previewWidth * _previewRatio);
            if (!Mathf.Approximately(Preview.resolvedStyle.height, height))
            {
                Preview.style.height = height;
            }
        }

        /// <summary>
        /// Adds a neutral badge below the title and returns it, so the caller can update its text later. Badges stay on
        /// one line and truncate when the card is narrow; the tooltip carries the full meaning.
        /// </summary>
        public Label AddBadge(string text, string tooltip)
        {
            if (_badges == null)
            {
                _badges = CommonUITK.CreateContainer("common-preview-gallery-card__badges");
                Insert(IndexOf(Title) + 1, _badges);
            }

            Label badge = CommonUITK.CreateLabel(text ?? string.Empty, "common-preview-gallery-card__badge");
            badge.tooltip = tooltip ?? string.Empty;
            _badges.Add(badge);
            return badge;
        }

        /// <summary>
        /// Replaces the card's actions with <paramref name="actions"/>, shown as stable trailing buttons over the top of the
        /// preview while the pointer is over the card or the card or one of its actions has keyboard focus. Hidden actions
        /// stay in the layout and focus order, so they never shift and Tab reaches them from the card.
        /// </summary>
        public void SetActions(params Button[] actions)
        {
            if (_actions == null)
            {
                _actions = CommonUITK.CreateContainer("common-preview-gallery-card__actions");
                Add(_actions);
                RegisterCallback<FocusInEvent>(_ => EnableInClassList(ActionsVisibleClass, true));
                RegisterCallback<FocusOutEvent>(evt =>
                {
                    VisualElement next = evt.relatedTarget as VisualElement;
                    EnableInClassList(ActionsVisibleClass, next != null && (next == this || Contains(next)));
                });
            }

            _actions.Clear();
            if (actions == null)
                return;
            for (int i = 0; i < actions.Length; i++)
            {
                Button action = actions[i];
                if (action == null)
                    continue;
                action.AddToClassList("common-preview-gallery-card__action");
                _actions.Add(action);
            }
        }

        bool IsWithinActions(IEventHandler target)
        {
            return _actions != null && target is VisualElement element && (element == _actions || _actions.Contains(element));
        }
    }
}
