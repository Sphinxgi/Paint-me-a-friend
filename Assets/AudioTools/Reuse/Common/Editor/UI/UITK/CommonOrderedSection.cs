using System;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonOrderedSection : VisualElement
    {
        public sealed class OrderedSectionClasses
        {
            public string RootClass;
            public string CustomizationClass;
            public string ControlsClass;
            public string MoveButtonClass;
            public string MoveIconClass;
            public string BodyClass;
        }

        public VisualElement Controls { get; }
        public VisualElement Body { get; }

        public CommonOrderedSection(
            VisualElement content,
            bool customizationMode,
            bool canMoveUp,
            bool canMoveDown,
            Action moveUp,
            Action moveDown,
            OrderedSectionClasses classes = null)
        {
            OrderedSectionClasses safeClasses = classes ?? new OrderedSectionClasses();
            CommonUITK.AddClasses(this, safeClasses.RootClass);

            Body = CommonUITK.CreateContainer(safeClasses.BodyClass);
            if (content != null) Body.Add(content);
            Add(Body);

            // Follows the body so that controls positioned over it stay on top and receive clicks.
            Controls = CommonUITK.CreateContainer(safeClasses.ControlsClass);
            Controls.style.display = customizationMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (customizationMode)
            {
                CommonUITK.AddClasses(this, safeClasses.CustomizationClass);

                Button up = CommonUITK.CreateIconButton("Move section up", "scrollup", moveUp, safeClasses.MoveButtonClass, null, safeClasses.MoveIconClass);
                up.SetEnabled(canMoveUp);
                Controls.Add(up);

                Button down = CommonUITK.CreateIconButton("Move section down", "scrolldown", moveDown, safeClasses.MoveButtonClass, null, safeClasses.MoveIconClass);
                down.SetEnabled(canMoveDown);
                Controls.Add(down);
            }
            Add(Controls);
        }
    }
}
