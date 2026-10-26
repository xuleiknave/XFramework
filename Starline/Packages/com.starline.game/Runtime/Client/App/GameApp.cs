using System.Collections;
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
        public MusicPlayer Music { get; private set; }
        /// <summary>3D night scene behind the main menu and the save slots.</summary>
        public MenuScene Menu { get; private set; }
        /// <summary>The expedition in progress, or null on the main menu.</summary>
        public ExpeditionRun Run { get; private set; }
        /// <summary>The save slot in use, or null before the first commander is created.</summary>
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
            Music = MusicPlayer.Create(transform);
            Menu = new MenuScene(transform, camera, Stage.Rig);
        }

        /// <summary>The first screen waits until the language and string tables are ready.</summary>
        private IEnumerator Start()
        {
            yield return Loc.Initialize();
            ShowMainMenu();
        }

        private void Update()
        {
            if (Profile != null)
            {
                Profile.playSeconds += Time.unscaledDeltaTime;
            }
            _screen?.Tick(Time.deltaTime);
            Menu.Tick(Time.unscaledDeltaTime);
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
            Menu.SetVisible(screen.UsesMenuScene);
            Ui.SetScreen(screen.Root);
            screen.Enter();
        }

        /// <summary>The save slots, opened from the main menu.</summary>
        public void ShowSaveSlots()
        {
            SaveProfile();
            Run = null;
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new SaveSlotsScreen(this));
        }

        /// <summary>Switches to a save slot (saving the one in use first).</summary>
        public void SignIn(CommanderProfile profile)
        {
            SaveProfile();
            Profile = profile;
            SaveProfile();
        }

        /// <summary>Drops the current profile without saving it, e.g. when its slot is being deleted.</summary>
        public void SignOut() => Profile = null;

        /// <summary>
        /// Starts an expedition; without a commander yet, first names one in the first empty slot
        /// (or opens the save slots when all are taken but none could be read).
        /// </summary>
        public void StartRunOrCreateCommander()
        {
            if (Profile != null)
            {
                StartRun();
                return;
            }
            int slot = ProfileStore.FirstEmptySlot();
            if (slot < 0)
            {
                ShowSaveSlots();
                return;
            }
            NewCommanderDialog.Show(this, slot, profile =>
            {
                SignIn(profile);
                StartRun();
            });
        }

        /// <summary>The home screen; picks up the most recently played save when none is in use.</summary>
        public void ShowMainMenu()
        {
            if (Profile == null)
            {
                Profile = ProfileStore.LoadLastPlayed();
            }
            SaveProfile();
            Run = null;
            Stage.Clear();
            Stage.SetVisible(false);
            Show(new MainMenuScreen(this));
            Music.PlayScreen("menu");
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
            Music.PlayScreen("map");
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
            Music.PlayScreen("deploy");
        }

        public void ShowBattle(Encounter encounter, Formation formation)
        {
            Show(new BattleScreen(this, new BattleController(encounter, formation, Stage)));
            bool boss = Run.Current != null && Run.Current.Type == NodeType.Boss;
            var acts = ExpeditionConfig.Current.Acts;
            Music.PlayBattle(boss, boss && Run.Act == acts[acts.Count - 1] && acts.Count > 1, Run.Act.Id);
        }

        /// <summary>Plays a replay on the battle screen; when it ends, <paramref name="exitText"/> runs <paramref name="exit"/>.</summary>
        public void ShowReplay(Replay replay, string exitText, System.Action exit)
        {
            Stage.SetVisible(true);
            Encounter stage = EncounterLibrary.Create(replay.Stage);
            Show(new BattleScreen(this, new BattleController(replay, stage, Stage), (exitText, exit)));
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
                    Music.PlayScreen("reward");
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
            Music.PlayScreen(Run.State == RunState.Victory ? "runEndVictory" : "runEndDefeat");
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
