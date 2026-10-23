using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Small helpers for building UI Toolkit elements in code.</summary>
    public static class Ui
    {
        /// <summary>UXML and USS live in Resources/StarlineUI.</summary>
        public const string ResourceFolder = "StarlineUI";

        /// <summary>
        /// Reference resolution the layouts are designed for: 4K, scaled to the screen height, so
        /// every px value in Theme.uss is a 4K pixel and lower resolutions shrink evenly.
        /// </summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(3840, 2160);

        public static void ConfigurePanel(PanelSettings settings)
        {
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
        }

        /// <summary>Instantiates Resources/StarlineUI/{name}.uxml, with its "#key" texts translated.</summary>
        public static VisualElement Load(string name)
        {
            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>(ResourceFolder + "/" + name);
            if (asset == null)
            {
                throw new InvalidOperationException("找不到界面布局 Resources/" + ResourceFolder + "/" + name + ".uxml");
            }
            TemplateContainer tree = asset.Instantiate();
            tree.pickingMode = PickingMode.Ignore;
            tree.AddToClassList("fill");
            Loc.Localize(tree);
            return tree;
        }

        /// <summary>Finds a named child or fails loudly, so a typo in UXML is caught on first use.</summary>
        public static T Find<T>(this VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException("界面里缺少元素 #" + name + "（" + typeof(T).Name + "）");
            }
            return element;
        }

        public static Label Label(string text, params string[] classes)
        {
            var label = new Label(text);
            AddClasses(label, classes);
            return label;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("btn");
            AddClasses(button, classes);
            return button;
        }

        public static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            AddClasses(box, classes);
            return box;
        }

        public static void SetVisible(this VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        /// A centred dialog on a dimmed backdrop. Buttons are (text, action) pairs; the first
        /// is styled as the primary action.
        /// </summary>
        public static VisualElement Dialog(string title, string body, params (string text, Action action)[] buttons)
        {
            VisualElement backdrop = Box("modal-backdrop");
            VisualElement dialog = Box("panel", "dialog");
            backdrop.Add(dialog);
            dialog.Add(Label(title, "h2"));
            dialog.Add(Label(body, "body", "dialog-body"));
            VisualElement row = Box("dialog-buttons");
            for (int i = 0; i < buttons.Length; i++)
            {
                row.Add(Button(buttons[i].text, buttons[i].action, i == 0 ? "btn-primary" : "btn-secondary"));
            }
            dialog.Add(row);
            return backdrop;
        }

        public static string Stars(int star) => new string('★', star);

        public static string Hearts(int value, int max) => new string('■', value) + new string('□', Math.Max(0, max - value));

        private static void AddClasses(VisualElement element, string[] classes)
        {
            foreach (string c in classes)
            {
                element.AddToClassList(c);
            }
        }
    }
}
