using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// The single UI Toolkit document. Three layers, back to front: world labels over the
    /// ships, the current screen, and modal dialogs.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        /// <summary>Panel settings created by the editor setup (Starline &gt; 初始化工程), with the default runtime theme.</summary>
        public const string PanelSettingsResource = "StarlinePanelSettings";

        private UIDocument _document;
        private VisualElement _screen;

        public VisualElement Root { get; private set; }
        public VisualElement WorldLayer { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement ModalLayer { get; private set; }
        public IPanel Panel => Root.panel;

        public static UiRoot Create(Transform parent)
        {
            var go = new GameObject("UI");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<UiRoot>();
            ui._document = go.AddComponent<UIDocument>();
            ui._document.panelSettings = LoadPanelSettings();
            go.SetActive(true);
            ui.Build();
            return ui;
        }

        private static PanelSettings LoadPanelSettings()
        {
            PanelSettings settings = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (settings != null)
            {
                return settings;
            }
            // Fallback so the game still runs before the editor setup has been done; without a
            // theme only our own stylesheet applies, which covers every control we use.
            Debug.LogWarning("Starline：没有找到 Resources/" + PanelSettingsResource + "，使用临时界面设置。请点菜单 Starline > 初始化工程。");
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            Ui.ConfigurePanel(settings);
            return settings;
        }

        private void Build()
        {
            Root = _document.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            Root.AddToClassList("starline");
            StyleSheet theme = Resources.Load<StyleSheet>(Ui.ResourceFolder + "/Theme");
            if (theme != null)
            {
                Root.styleSheets.Add(theme);
            }
            else
            {
                Debug.LogError("Starline：找不到界面样式 Resources/" + Ui.ResourceFolder + "/Theme.uss");
            }
            UiFont.Apply(Root);

            WorldLayer = Layer("world-layer");
            ScreenLayer = Layer("screen-layer");
            ModalLayer = Layer("modal-layer");
        }

        private VisualElement Layer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            Root.Add(layer);
            return layer;
        }

        public void SetScreen(VisualElement screen)
        {
            _screen?.RemoveFromHierarchy();
            CloseModals();
            _screen = screen;
            if (screen != null)
            {
                ScreenLayer.Add(screen);
            }
        }

        public void ShowModal(VisualElement modal) => ModalLayer.Add(modal);

        public void CloseModals() => ModalLayer.Clear();

        public bool HasModal => ModalLayer.childCount > 0;

        /// <summary>
        /// True when the pointer is over an interactive part of the UI, so world clicks
        /// (placing ships, aiming the orbital strike) should be ignored. Full-screen layout
        /// containers use picking-mode Ignore, so only real panels count.
        /// </summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            IPanel panel = Panel;
            if (panel == null)
            {
                return false;
            }
            if (HasModal)
            {
                return true;
            }
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPosition) != null;
        }
    }
}
