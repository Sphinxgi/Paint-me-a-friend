using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public enum CommonWelcomeAccent
    {
        Blue,
        Cyan,
        Violet,
        Amber
    }

    public sealed class CommonWelcomeStep
    {
        public CommonWelcomeStep(string title, string description)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string Title { get; }
        public string Description { get; }
    }

    /// <summary>
    /// A capability card: an optional image above a title, a description and actions. Cards sit side by side and stack
    /// in the compact layout. A <see cref="Status"/> explains a capability that is not available.
    /// </summary>
    public sealed class CommonWelcomeCard
    {
        public CommonWelcomeCard(string title, string description, params Button[] actions)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            Actions = actions ?? Array.Empty<Button>();
        }

        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<Button> Actions { get; }

        /// <summary>Optional image shown above the title, cropped to the card's image band.</summary>
        public Texture2D Image { get; set; }

        /// <summary>Optional short note below the description, such as why the capability is not available.</summary>
        public string Status { get; set; }
    }

    public sealed class CommonWelcomeContent
    {
        public Texture Logo { get; set; }
        public string ProductName { get; set; }
        public string Headline { get; set; }
        public string Description { get; set; }

        /// <summary>Heading above the steps and cards; the heading is omitted while both texts are empty.</summary>
        public string SectionTitle { get; set; }
        public string SectionDescription { get; set; }
        public IReadOnlyList<CommonWelcomeStep> Steps { get; set; }
        public CommonWelcomeAccent Accent { get; set; } = CommonWelcomeAccent.Blue;

        /// <summary>Optional capability cards shown below the steps.</summary>
        public IReadOnlyList<CommonWelcomeCard> Cards { get; set; }

        /// <summary>Heading of the resource row, such as "Learn".</summary>
        public string ResourcesTitle { get; set; }

        /// <summary>Optional compact resource buttons, such as manuals and samples, created with <see cref="CommonWelcomeWindow.CreateResource"/>.</summary>
        public IReadOnlyList<Button> Resources { get; set; }
    }

    /// <summary>
    /// Shared branded shell for concise product welcome windows.
    /// </summary>
    public static class CommonWelcomeWindow
    {
        public const float DefaultMinWidth = 520f;
        public const float DefaultMinHeight = 600f;
        public const float DefaultMaxWidth = 720f;
        public const float DefaultMaxHeight = 760f;

        const string AnchorAssetName = "CommonWelcomeWindow";
        const string AnchorPathSuffix = "Editor/UI/UITK/CommonWelcomeWindow.cs";
        const string StylePathSuffix = "Editor/UI/UITK/CommonWelcomeWindow.uss";
        const float CompactWidth = 560f;

        static readonly string[] AccentClasses =
        {
            "common-welcome-accent--blue",
            "common-welcome-accent--cyan",
            "common-welcome-accent--violet",
            "common-welcome-accent--amber"
        };

        public static T ShowUtility<T>(string title) where T : EditorWindow
        {
            return ShowUtility<T>(title, new Vector2(DefaultMinWidth, DefaultMinHeight));
        }

        /// <summary>Opens the utility window centered on the main window at <paramref name="size"/>, kept within the default constraints.</summary>
        public static T ShowUtility<T>(string title, Vector2 size) where T : EditorWindow
        {
            T[] openWindows = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < openWindows.Length; i++)
            {
                if (openWindows[i] != null)
                    openWindows[i].Close();
            }

            T window = ScriptableObject.CreateInstance<T>();
            ApplyDefaultConstraints(window, title);
            Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
            float width = Mathf.Clamp(size.x, DefaultMinWidth, DefaultMaxWidth);
            float height = Mathf.Clamp(size.y, DefaultMinHeight, DefaultMaxHeight);
            Rect centeredPosition = new Rect(
                Mathf.Round(mainWindow.x + (mainWindow.width - width) * 0.5f),
                Mathf.Round(mainWindow.y + (mainWindow.height - height) * 0.5f),
                width,
                height);
            window.ShowUtility();
            window.position = centeredPosition;
            window.Focus();
            return window;
        }

        public static void ApplyDefaultConstraints(EditorWindow window, string title = null)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            window.minSize = new Vector2(DefaultMinWidth, DefaultMinHeight);
            window.maxSize = new Vector2(DefaultMaxWidth, DefaultMaxHeight);
            if (!string.IsNullOrWhiteSpace(title))
                window.titleContent = new GUIContent(title);
        }

        public static Button CreateAction(string text, string tooltip, Action click, bool primary = false)
        {
            Button button = CommonUITK.CreateButton(
                text,
                click,
                "common-welcome-action");
            button.tooltip = tooltip ?? string.Empty;
            button.EnableInClassList("common-welcome-action--primary", primary);
            return button;
        }

        /// <summary>
        /// A compact resource button for <see cref="CommonWelcomeContent.Resources"/>. A non-null
        /// <paramref name="unavailableReason"/> disables it and adds the reason to its tooltip.
        /// </summary>
        public static Button CreateResource(string text, string tooltip, Action click, string unavailableReason = null)
        {
            Button button = CommonUITK.CreateButton(text, click, "common-welcome-resource");
            button.SetEnabled(unavailableReason == null);
            button.tooltip = unavailableReason == null ? tooltip ?? string.Empty : (tooltip ?? string.Empty) + "\nUnavailable: " + unavailableReason;
            return button;
        }

        public static VisualElement Build(
            VisualElement host,
            CommonWelcomeContent content,
            params Button[] actions)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            host.Clear();

            VisualElement root = CommonUITK.CreateContainer();
            CommonUITK.ApplyRoot(
                root,
                LoadStyleSheet(),
                "common-welcome-root",
                "common-welcome-theme-dark",
                "common-welcome-theme-light");
            ApplyAccent(root, content.Accent);
            root.RegisterCallback<GeometryChangedEvent>(evt =>
                root.EnableInClassList("common-welcome--compact", evt.newRect.width < CompactWidth));
            host.Add(root);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("common-welcome-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            VisualElement body = CommonUITK.CreateContainer("common-welcome-content");
            body.Add(CreateHero(content));
            if (!string.IsNullOrEmpty(content.SectionTitle) || !string.IsNullOrEmpty(content.SectionDescription))
                body.Add(CreateSectionHeading(content));
            if (content.Steps != null && content.Steps.Count > 0)
                body.Add(CreateSteps(content.Steps));
            if (content.Cards != null && content.Cards.Count > 0)
                body.Add(CreateCards(content.Cards));
            if (content.Resources != null && content.Resources.Count > 0)
                body.Add(CreateResources(content.ResourcesTitle, content.Resources));
            scroll.Add(body);
            root.Add(scroll);

            if (actions != null && actions.Length > 0)
            {
                VisualElement footer = CommonUITK.CreateWindowFooter(
                    0f,
                    0f,
                    "common-welcome-footer");
                footer.style.marginTop = 0f;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] != null)
                        footer.Add(actions[i]);
                }

                root.Add(footer);
            }

            return root;
        }

        public static StyleSheet LoadStyleSheet()
        {
            return CommonUITK.LoadStyleSheetFromAnchor(
                AnchorAssetName,
                AnchorPathSuffix,
                StylePathSuffix);
        }

        static VisualElement CreateHero(CommonWelcomeContent content)
        {
            VisualElement hero = CommonUITK.CreateContainer("common-welcome-hero");
            if (content.Logo != null)
            {
                Image image = new Image
                {
                    image = content.Logo,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                image.AddToClassList("common-welcome-logo");
                hero.Add(image);
            }

            VisualElement copy = CommonUITK.CreateContainer("common-welcome-hero-copy");
            copy.Add(CommonUITK.CreateLabel(
                content.ProductName ?? string.Empty,
                "common-welcome-eyebrow"));
            copy.Add(CommonUITK.CreateLabel(
                content.Headline ?? string.Empty,
                "common-welcome-title"));
            copy.Add(CommonUITK.CreateLabel(
                content.Description ?? string.Empty,
                "common-welcome-intro"));
            hero.Add(copy);
            return hero;
        }

        static VisualElement CreateSectionHeading(CommonWelcomeContent content)
        {
            VisualElement heading = CommonUITK.CreateContainer("common-welcome-heading");
            heading.Add(CommonUITK.CreateLabel(
                content.SectionTitle ?? string.Empty,
                "common-welcome-section-title"));
            heading.Add(CommonUITK.CreateLabel(
                content.SectionDescription ?? string.Empty,
                "common-welcome-section-description"));
            return heading;
        }

        static VisualElement CreateCards(IReadOnlyList<CommonWelcomeCard> cards)
        {
            VisualElement row = CommonUITK.CreateContainer("common-welcome-cards");
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null)
                    continue;
                VisualElement card = CreateCard(cards[i]);
                card.EnableInClassList("common-welcome-card--following", row.childCount > 0);
                row.Add(card);
            }
            return row;
        }

        static VisualElement CreateCard(CommonWelcomeCard content)
        {
            VisualElement card = CommonUITK.CreateContainer("common-welcome-card");
            if (content.Image != null)
            {
                // A background image follows the card's rounded corners, which an Image element would not.
                VisualElement image = CommonUITK.CreateContainer("common-welcome-card-image");
                image.style.backgroundImage = Background.FromTexture2D(content.Image);
                image.pickingMode = PickingMode.Ignore;
                card.Add(image);
            }

            VisualElement copy = CommonUITK.CreateContainer("common-welcome-card-copy");
            // The text grows, so cards in one row align their actions while the status stays with its description.
            VisualElement text = CommonUITK.CreateContainer("common-welcome-card-text");
            text.Add(CommonUITK.CreateLabel(content.Title, "common-welcome-step-title"));
            text.Add(CommonUITK.CreateLabel(content.Description, "common-welcome-step-description"));
            if (!string.IsNullOrEmpty(content.Status))
                text.Add(CommonUITK.CreateLabel(content.Status, "common-welcome-card-status"));
            copy.Add(text);
            VisualElement actions = CommonUITK.CreateContainer("common-welcome-card-actions");
            for (int i = 0; i < content.Actions.Count; i++)
            {
                if (content.Actions[i] != null)
                    actions.Add(content.Actions[i]);
            }
            if (actions.childCount > 0)
                copy.Add(actions);
            card.Add(copy);
            return card;
        }

        static VisualElement CreateResources(string title, IReadOnlyList<Button> resources)
        {
            VisualElement section = CommonUITK.CreateContainer("common-welcome-resources");
            if (!string.IsNullOrEmpty(title))
                section.Add(CommonUITK.CreateLabel(title, "common-welcome-resources-title"));
            VisualElement row = CommonUITK.CreateContainer("common-welcome-resources-row");
            for (int i = 0; i < resources.Count; i++)
            {
                if (resources[i] != null)
                    row.Add(resources[i]);
            }
            section.Add(row);
            return section;
        }

        static VisualElement CreateSteps(IReadOnlyList<CommonWelcomeStep> steps)
        {
            VisualElement list = CommonUITK.CreateContainer("common-welcome-steps");
            if (steps == null)
                return list;

            for (int i = 0; i < steps.Count; i++)
            {
                CommonWelcomeStep step = steps[i];
                if (step == null)
                    continue;

                VisualElement card = CommonUITK.CreateContainer("common-welcome-step");
                VisualElement badge = CommonUITK.CreateContainer("common-welcome-step-badge");
                badge.Add(CommonUITK.CreateLabel(
                    (i + 1).ToString(),
                    "common-welcome-step-number"));
                card.Add(badge);

                VisualElement copy = CommonUITK.CreateContainer("common-welcome-step-copy");
                copy.Add(CommonUITK.CreateLabel(
                    step.Title,
                    "common-welcome-step-title"));
                copy.Add(CommonUITK.CreateLabel(
                    step.Description,
                    "common-welcome-step-description"));
                card.Add(copy);
                list.Add(card);
            }

            return list;
        }

        static void ApplyAccent(VisualElement root, CommonWelcomeAccent accent)
        {
            int selectedIndex = Mathf.Clamp((int)accent, 0, AccentClasses.Length - 1);
            for (int i = 0; i < AccentClasses.Length; i++)
            {
                root.EnableInClassList(AccentClasses[i], i == selectedIndex);
            }
        }
    }
}
