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
    }
}
