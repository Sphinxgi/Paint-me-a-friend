using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonPaginationControl : VisualElement
    {
        public sealed class PaginationClasses
        {
            public string RootClass;
            public string ButtonBaseClass;
            public string ButtonStyleClass;
            public string ButtonClass;
            public string PageButtonClass;
        }

        private readonly EditorWindow _owner;
        private readonly Button _previous;
        private readonly Button _page;
        private readonly Button _next;
        private int _currentPage = 1;
        private int _pageCount = 1;
        private bool _hasMore;
        private Action<int> _onPageChanged;

        public int CurrentPage => _currentPage;
        public int PageCount => _pageCount;
        public Button PreviousButton => _previous;
        public Button PageButton => _page;
        public Button NextButton => _next;

        public CommonPaginationControl(EditorWindow owner, PaginationClasses classes)
        {
            _owner = owner;
            PaginationClasses safeClasses = classes ?? new PaginationClasses();
            CommonUITK.AddClasses(this, safeClasses.RootClass);

            _previous = CommonUITK.CreateButton("<", () => ChangePage(_currentPage - 1), safeClasses.ButtonBaseClass, safeClasses.ButtonStyleClass, safeClasses.ButtonClass);
            _page = CommonUITK.CreateButton(string.Empty, ShowPageMenu, safeClasses.ButtonBaseClass, safeClasses.ButtonStyleClass, safeClasses.PageButtonClass);
            _next = CommonUITK.CreateButton(">", () => ChangePage(_currentPage + 1), safeClasses.ButtonBaseClass, safeClasses.ButtonStyleClass, safeClasses.ButtonClass);
            Add(_previous);
            Add(_page);
            Add(_next);
            SetState(1, 1, null, null, false);
        }

        public void SetState(int currentPage, int pageCount, string tooltip, Action<int> onPageChanged, bool visible = true)
        {
            SetState(currentPage, pageCount, tooltip, onPageChanged, visible, false);
        }

        /// <summary>Displays loaded pages and optionally allows requesting another page when the total is not known.</summary>
        public void SetState(int currentPage, int pageCount, string tooltip, Action<int> onPageChanged, bool visible, bool hasMore)
        {
            _pageCount = Math.Max(1, pageCount);
            _hasMore = hasMore;
            _currentPage = Math.Max(1, Math.Min(currentPage, _pageCount));
            _onPageChanged = onPageChanged;

            _previous.SetEnabled(_currentPage > 1);
            _next.SetEnabled(_currentPage < _pageCount || _hasMore);
            _page.SetEnabled(_pageCount > 1);
            _page.text = _hasMore ? $"Page {_currentPage:N0}/…" : $"Page {_currentPage:N0}/{_pageCount:N0}";
            _page.tooltip = tooltip ?? string.Empty;
            style.display = visible && (_pageCount > 1 || _hasMore) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ChangePage(int page)
        {
            if (page < 1 || page > _pageCount + (_hasMore ? 1 : 0) || page == _currentPage) return;
            _onPageChanged?.Invoke(page);
        }

        private void ShowPageMenu()
        {
            if (_owner == null || _pageCount <= 1) return;

            DropDownWindow.ShowAsDropDown(
                CommonUITK.ToScreenDropdownAnchor(_owner, _page),
                1,
                _pageCount,
                _currentPage,
                "Page ",
                null,
                ChangePage);
        }
    }
}
