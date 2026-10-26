using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// One full screen of the game: a UXML layout from Resources/StarlineUI plus the code
    /// that fills it in. <see cref="GameApp.Show"/> swaps screens.
    /// </summary>
    public abstract class UiScreen
    {
        protected UiScreen(GameApp app, string layout)
        {
            App = app;
            Root = Ui.Load(layout);
        }

        protected GameApp App { get; }
        public VisualElement Root { get; }

        /// <summary>True when the 3D night scene of the main menu shows behind this screen.</summary>
        public virtual bool UsesMenuScene => false;

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }

        public virtual void Tick(float deltaTime)
        {
        }

        protected T Find<T>(string name) where T : VisualElement => Root.Find<T>(name);

        protected Button Button(string name, System.Action onClick)
        {
            Button button = Find<Button>(name);
            button.clicked += onClick;
            return button;
        }

        /// <summary>
        /// Keeps the 3D camera framing the board inside the layout's "board-area" element (the
        /// part of the screen the panels leave free), following resizes and orientation changes.
        /// </summary>
        protected void FrameBoardArea()
        {
            VisualElement area = Find<VisualElement>("board-area");
            area.RegisterCallback<GeometryChangedEvent>(_ => SendBoardArea(area));
        }

        private void SendBoardArea(VisualElement area)
        {
            if (area.panel == null)
            {
                return;
            }
            Rect screen = area.panel.visualTree.layout;
            Rect r = area.worldBound;
            if (screen.width <= 0f || screen.height <= 0f)
            {
                return;
            }
            // Panel y runs down from the top; the camera wants y up from the bottom.
            App.Stage.Rig.SetViewport(new Rect(
                r.x / screen.width,
                1f - r.yMax / screen.height,
                r.width / screen.width,
                r.height / screen.height));
        }
    }
}
