using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Starline.Battle;
using Starline.Expedition;

namespace Starline.Client
{
    /// <summary>
    /// Entry point of the game. Owns the long-lived pieces (camera, 3D stage, UI root) and
    /// the current expedition, and switches between screens. The Main scene contains just
    /// this component; everything else is created at runtime.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        private UiScreen _screen;

        public BattleStage Stage { get; private set; }
        public UiRoot Ui { get; private set; }
        public UnitOverlay Overlay { get; private set; }
        /// <summary>The expedition in progress, or null on the main menu.</summary>
        public ExpeditionRun Run { get; private set; }
        /// <summary>The save slot in use, or null on the login screen.</summary>
        public CommanderProfile Profile { get; private set; }

        private void Awake()
        {
            ClientData.EnsureLoaded();
            Application.targetFrameRate = 120;

            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            }
            camera.fieldOfView = 50f;
            SceneLook.Apply(camera);
            EnsureEventSystem();

            Stage = new BattleStage(transform, camera);
            Ui = UiRoot.Create(transform);
            Overlay = new UnitOverlay(Ui, Stage);
        }

        private void Start() => ShowLogin();

        private void Update()
        {
            if (Profile != null)
            {
                Profile.playSeconds += Time.unscaledDeltaTime;
            }
            _screen?.Tick(Time.deltaTime);
            Stage.Tick();
            Overlay.Update();
        }

        /// <summary>UI Toolkit receives Input System events through the event system.</summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveProfile();
            }
        }

        private void OnApplicationQuit() => SaveProfile();

        /// <summary>Writes the current profile with an updated "last played" time.</summary>
        public void SaveProfile()
        {
            if (Profile == null)
            {
                return;
            }
            Profile.lastPlayedTicks = System.DateTime.Now.Ticks;
            ProfileStore.Save(Profile);
        }

        public bool IsPointerOverUi(Vector2 screenPosition) => Ui.IsPointerOverUi(screenPosition);

        // ------------------------------------------------------------------
        // Navigation

        public void Show(UiScreen screen)
        {
            _screen?.Exit();
            _screen = screen;
            Ui.SetScreen(screen.Root);
            screen.Enter();
        }

        /// <summary>The save-slot screen shown at launch and from the main menu.</summary>
        public void ShowLogin()
        {
            SaveProfile();
            Profile = null;
            Run = null;
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new LoginScreen(this));
        }

        public void SignIn(CommanderProfile profile)
        {
            Profile = profile;
            SaveProfile();
            ShowMainMenu();
        }

        public void ShowMainMenu()
        {
            SaveProfile();
            Run = null;
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new MainMenuScreen(this));
        }

        public void StartRun()
        {
            Run = new ExpeditionRun((ulong)System.DateTime.Now.Ticks % 1000000007UL + 1UL);
            Profile.runsStarted++;
            SaveProfile();
            ShowMap(showIntro: true);
        }

        public void ShowMap(bool showIntro = false)
        {
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new MapScreen(this, showIntro));
        }

        /// <summary>After choosing a node on the map: a battle goes to deployment, a supply station opens its dialog.</summary>
        public void EnterNode(MapNode node)
        {
            Run.Enter(node);
            if (Run.State == RunState.Battle)
            {
                ShowDeploy();
            }
        }

        public void ShowDeploy()
        {
            Stage.SetVisible(true);
            Stage.Rig.GoToDeploy();
            Show(new DeployScreen(this));
        }

        public void ShowBattle(Encounter encounter, Formation formation)
        {
            Show(new BattleScreen(this, new BattleController(encounter, formation, Stage)));
        }

        /// <summary>Where to go after the battle result, depending on the run state.</summary>
        public void AfterBattle()
        {
            switch (Run.State)
            {
                case RunState.Reward:
                    Stage.Clear();
                    Stage.SetVisible(false);
                    Show(new RewardScreen(this));
                    break;
                case RunState.Battle:
                    // Lost against the boss with durability left: try again.
                    ShowDeploy();
                    break;
                case RunState.Victory:
                case RunState.Defeat:
                    ShowRunEnd();
                    break;
                default:
                    ShowMap();
                    break;
            }
        }

        public void ShowRunEnd()
        {
            if (Run.State == RunState.Victory)
            {
                Profile.runsWon++;
            }
            SaveProfile();
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new RunEndScreen(this));
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
