using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonTabbedPane : VisualElement
    {
        public sealed class TabbedPaneClasses
        {
            public string RootClass;
            public string HeaderClass;
            public string LeadingClass;
            public string TabStripClass;
            public string TabClass;
            public string SelectedTabClass;
            public string TrailingClass;
            public string BodyClass;
        }

        // Tabs whose label is wider than its text by more than this keep their full label when space runs out.
        private const float TabWidthSlack = 4f;

        private readonly List<Button> _tabs = new List<Button>();
        private readonly List<Image> _tabIcons = new List<Image>();
        private readonly List<bool> _tabVisible = new List<bool>();
        private readonly List<int> _overflowTabs = new List<int>();
        private readonly TabbedPaneClasses _classes;
        private Action<int> _onTabChanged;
        private Button _overflow;
        private float _availableWidth = -1f;
        // Tab display stays the caller's until a tab is hidden or the overflow menu is enabled.
        private bool _managesTabDisplay;

        public VisualElement Header { get; }
        public VisualElement Leading { get; }
        public VisualElement TabStrip { get; }
        public VisualElement Trailing { get; }
        public VisualElement Body { get; }
        public int SelectedIndex { get; private set; } = -1;

        public CommonTabbedPane(TabbedPaneClasses classes = null)
        {
            _classes = classes ?? new TabbedPaneClasses();
            CommonUITK.AddClasses(this, _classes.RootClass);

            Header = CommonUITK.CreateContainer(_classes.HeaderClass);
            Add(Header);

            Leading = CommonUITK.CreateContainer(_classes.LeadingClass);
            Header.Add(Leading);

            TabStrip = CommonUITK.CreateContainer(_classes.TabStripClass);
            Header.Add(TabStrip);

            Trailing = CommonUITK.CreateContainer(_classes.TrailingClass);
            Header.Add(Trailing);

            Body = CommonUITK.CreateContainer(_classes.BodyClass);
            Add(Body);
        }

        public void SetTabs(IReadOnlyList<string> labels, int selectedIndex, Action<int> onTabChanged)
        {
            int count = labels?.Count ?? 0;
            List<GUIContent> content = new List<GUIContent>(count);
            for (int i = 0; i < count; i++)
            {
                content.Add(new GUIContent(labels[i] ?? string.Empty));
            }

            SetTabs(content, selectedIndex, onTabChanged);
        }

        public void SetTabs(IReadOnlyList<GUIContent> content, int selectedIndex, Action<int> onTabChanged)
        {
            _onTabChanged = onTabChanged;
            int count = content?.Count ?? 0;
            EnsureTabCount(count);
            for (int i = 0; i < count; i++)
            {
                GUIContent item = content[i] ?? GUIContent.none;
                _tabs[i].text = item.text ?? string.Empty;
                _tabs[i].tooltip = item.tooltip ?? string.Empty;
                _tabIcons[i].image = item.image;
                _tabIcons[i].style.display = item.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            SetSelectedIndexWithoutNotify(selectedIndex);
        }

        public int TabCount => _tabs.Count;

        public void SetTabEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _tabs[index].SetEnabled(enabled);
        }

        /// <summary>
        /// Shows or removes a tab, for tabs that apply only in some states. A hidden tab is neither in the strip nor in
        /// the overflow menu; the caller selects another tab when it hides the selected one.
        /// </summary>
        public void SetTabVisible(int index, bool visible)
        {
            if (index < 0 || index >= _tabs.Count || _tabVisible[index] == visible) return;
            _tabVisible[index] = visible;
            _managesTabDisplay = true;
            RefreshTabDisplay();
        }

        /// <summary>True while the tab applies, whether it shows in the strip or in the overflow menu.</summary>
        public bool IsTabVisible(int index)
        {
            return index >= 0 && index < _tabs.Count && _tabVisible[index];
        }

        public void SetSelectedIndexWithoutNotify(int selectedIndex)
        {
            SelectedIndex = selectedIndex >= 0 && selectedIndex < _tabs.Count ? selectedIndex : -1;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].EnableInClassList(_classes.SelectedTabClass, i == SelectedIndex);
            }
            RefreshTabDisplay();
        }

        /// <summary>Selects the tab and raises the tab change, as a click on it does.</summary>
        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == SelectedIndex) return;

            SetSelectedIndexWithoutNotify(index);
            _onTabChanged?.Invoke(index);
        }

        /// <summary>
        /// Moves the tabs that do not fit the strip's width into a menu behind a trailing button. The selected tab always
        /// stays in the strip. Without this, every tab stays in the strip at any width.
        /// </summary>
        public Button EnableOverflowMenu(string buttonClass, string tooltip = "Show the tabs that do not fit.")
        {
            if (_overflow != null) return _overflow;

            _overflow = CommonUITK.CreateButton("...", ShowOverflowMenu, buttonClass);
            _overflow.name = "common-tabbed-pane-overflow";
            _overflow.tooltip = tooltip;
            Trailing.Add(_overflow);
            _managesTabDisplay = true;
            Header.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (Mathf.Approximately(evt.oldRect.width, evt.newRect.width)) return;
                _availableWidth = evt.newRect.width;
                RefreshTabDisplay();
            });
            RefreshTabDisplay();
            return _overflow;
        }

        private void RefreshTabDisplay()
        {
            if (!_managesTabDisplay) return;

            _overflowTabs.Clear();
            bool[] shown = CalculateShownTabs();
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].style.display = shown[i] ? DisplayStyle.Flex : DisplayStyle.None;
                if (_tabVisible[i] && !shown[i]) _overflowTabs.Add(i);
            }
            if (_overflow != null) Trailing.style.display = _overflowTabs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool[] CalculateShownTabs()
        {
            int count = _tabs.Count;
            bool[] shown = new bool[count];
            for (int i = 0; i < count; i++)
            {
                shown[i] = _tabVisible[i];
            }
            if (_overflow == null || _availableWidth <= 0f) return shown;

            float total = 0f;
            float[] widths = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!_tabVisible[i]) continue;
                widths[i] = PreferredWidth(_tabs[i]);
                total += widths[i];
            }
            float available = _availableWidth - Leading.layout.width;
            if (total <= available) return shown;

            float remaining = available - Mathf.Max(PreferredWidth(_overflow), 24f);
            if (SelectedIndex >= 0 && _tabVisible[SelectedIndex]) remaining -= widths[SelectedIndex];
            for (int i = 0; i < count; i++)
            {
                if (!_tabVisible[i] || i == SelectedIndex) continue;
                shown[i] = widths[i] <= remaining;
                if (shown[i]) remaining -= widths[i];
            }
            return shown;
        }

        private static float PreferredWidth(Button button)
        {
            IResolvedStyle style = button.resolvedStyle;
            float chrome = style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth +
                           style.marginLeft + style.marginRight;
            float text = button.panel != null
                ? button.MeasureTextSize(button.text ?? string.Empty, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined).x
                : 0f;
            // Before the first layout the font is unknown; an average glyph width keeps the estimate close.
            if (text <= 0f) text = (button.text ?? string.Empty).Length * 7f;
            return text + chrome + TabWidthSlack;
        }

        private void ShowOverflowMenu()
        {
            if (_overflow == null || _overflowTabs.Count == 0) return;

            GenericMenu menu = new GenericMenu();
            foreach (int index in _overflowTabs)
            {
                int tab = index;
                GUIContent content = new GUIContent(_tabs[tab].text, _tabs[tab].tooltip);
                if (_tabs[tab].enabledSelf) menu.AddItem(content, false, () => SelectTab(tab));
                else menu.AddDisabledItem(content);
            }
            CommonUITK.ShowGenericMenu(menu, _overflow);
        }

        private void EnsureTabCount(int count)
        {
            while (_tabs.Count < count)
            {
                int index = _tabs.Count;
                Button tab = CommonUITK.CreateButton(string.Empty, () => SelectTab(index), _classes.TabClass);
                Image icon = new Image
                {
                    name = "common-tabbed-pane-icon",
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.ScaleToFit
                };
                icon.AddToClassList("common-tabbed-pane__icon");
                icon.style.display = DisplayStyle.None;
                tab.Insert(0, icon);
                _tabs.Add(tab);
                _tabIcons.Add(icon);
                _tabVisible.Add(true);
                TabStrip.Add(tab);
            }

            while (_tabs.Count > count)
            {
                int index = _tabs.Count - 1;
                _tabs[index].RemoveFromHierarchy();
                _tabs.RemoveAt(index);
                _tabIcons.RemoveAt(index);
                _tabVisible.RemoveAt(index);
            }
        }
    }
}
