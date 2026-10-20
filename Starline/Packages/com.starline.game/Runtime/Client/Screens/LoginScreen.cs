using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// First screen after launch: pick one of the save slots (click to select, double-click or
    /// the enter button to play), create a commander in an empty slot, or delete a save.
    /// </summary>
    public sealed class LoginScreen : UiScreen
    {
        private readonly CommanderProfile[] _profiles = new CommanderProfile[ProfileStore.SlotCount];
        private readonly VisualElement[] _cards = new VisualElement[ProfileStore.SlotCount];
        private readonly VisualElement _slots;
        private readonly Button _enter;
        private int _selected = -1;

        public LoginScreen(GameApp app) : base(app, "Login")
        {
            _slots = Find<VisualElement>("slots");
            _enter = Button("enter", () => Enter(_selected));
            Find<Button>("settings").SetEnabled(false);
            Button("quit", App.Quit);
            Find<Label>("version").text = "版本 " + Application.version + " · 第一幕试玩";
            Reload();
        }

        private void Reload()
        {
            int lastPlayed = -1;
            for (int i = 0; i < _profiles.Length; i++)
            {
                _profiles[i] = ProfileStore.Load(i);
                if (_profiles[i] != null && (lastPlayed < 0 || _profiles[i].lastPlayedTicks > _profiles[lastPlayed].lastPlayedTicks))
                {
                    lastPlayed = i;
                }
            }
            if (_selected < 0 || _profiles[_selected] == null)
            {
                _selected = lastPlayed;
            }
            Rebuild();
        }

        private void Rebuild()
        {
            _slots.Clear();
            for (int i = 0; i < _profiles.Length; i++)
            {
                _cards[i] = _profiles[i] != null ? SlotCard(i, _profiles[i]) : EmptyCard(i);
                _slots.Add(_cards[i]);
            }
            Select(_selected);
        }

        private void Select(int slot)
        {
            _selected = slot;
            for (int i = 0; i < _cards.Length; i++)
            {
                _cards[i].EnableInClassList("slot-card--selected", i == slot);
            }
            _enter.SetEnabled(slot >= 0);
        }

        private VisualElement SlotCard(int slot, CommanderProfile profile)
        {
            VisualElement card = Ui.Box("panel", "slot-card");
            card.Add(Ui.Label("存档 " + (slot + 1), "small", "muted", "eyebrow"));
            card.Add(Ui.Label(profile.name, "slot-name"));
            card.Add(Ui.Box("slot-line"));
            card.Add(Row("游戏时长", CommanderProfile.FormatPlayTime(profile.playSeconds)));
            card.Add(Row("最近游玩", profile.LastPlayed.ToString("yyyy-MM-dd HH:mm")));
            card.Add(Row("远征", (profile.runsWon > 0 ? "通关 " + profile.runsWon + " 次" : "还没有通关") + " · 共 " + profile.runsStarted + " 次"));
            card.Add(Ui.Box("spacer"));
            VisualElement foot = Ui.Box("row", "slot-foot");
            foot.Add(Ui.Button("删除存档", () => ConfirmDelete(slot), "btn-small", "btn-text"));
            card.Add(foot);
            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                {
                    return;
                }
                if (evt.clickCount >= 2)
                {
                    Enter(slot);
                    return;
                }
                Select(slot);
            });
            return card;
        }

        private VisualElement EmptyCard(int slot)
        {
            VisualElement card = Ui.Box("panel", "slot-card", "slot-card--empty");
            card.Add(Ui.Label("存档 " + (slot + 1), "small", "muted", "eyebrow"));
            card.Add(Ui.Label("+", "slot-plus"));
            card.Add(Ui.Label("新的指挥官", "slot-new"));
            card.Add(Ui.Label("从灯塔城出发，重新点亮星线", "small", "muted"));
            card.RegisterCallback<ClickEvent>(_ => ShowNewCommander(slot));
            return card;
        }

        private static VisualElement Row(string key, string value)
        {
            VisualElement row = Ui.Box("row", "slot-row");
            row.Add(Ui.Label(key, "muted"));
            row.Add(Ui.Label(value));
            return row;
        }

        private void Enter(int slot)
        {
            if (slot < 0 || _profiles[slot] == null)
            {
                return;
            }
            App.SignIn(_profiles[slot]);
        }

        private void ShowNewCommander(int slot)
        {
            VisualElement dialog = Ui.Load("NewCommander");
            TextField field = dialog.Find<TextField>("name");
            Label count = dialog.Find<Label>("count");
            Label error = dialog.Find<Label>("error");
            dialog.Find<Label>("slot-label").text = "存档 " + (slot + 1);
            count.text = "0 / " + CommanderProfile.MaxNameLength;
            field.maxLength = CommanderProfile.MaxNameLength;
            field.RegisterValueChangedCallback(evt =>
            {
                count.text = evt.newValue.Trim().Length + " / " + CommanderProfile.MaxNameLength;
                error.text = string.Empty;
            });

            void Confirm()
            {
                string name = field.value;
                string problem = CommanderProfile.ValidateName(ref name);
                if (problem != null)
                {
                    error.text = problem;
                    return;
                }
                CommanderProfile profile = CommanderProfile.Create(slot, name);
                ProfileStore.Save(profile);
                App.Ui.CloseModals();
                App.SignIn(profile);
            }

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Confirm();
                }
            }, TrickleDown.TrickleDown);
            dialog.Find<Button>("confirm").clicked += Confirm;
            dialog.Find<Button>("cancel").clicked += App.Ui.CloseModals;
            App.Ui.ShowModal(dialog);
            field.schedule.Execute(() => field.Focus());
        }

        private void ConfirmDelete(int slot)
        {
            CommanderProfile profile = _profiles[slot];
            VisualElement dialog = Ui.Dialog("删除存档",
                "指挥官“" + profile.name + "”的存档（游戏时长 " + CommanderProfile.FormatPlayTime(profile.playSeconds) + "）会被永久删除，无法恢复。",
                ("删除", () =>
                {
                    ProfileStore.Delete(slot);
                    App.Ui.CloseModals();
                    Reload();
                }),
                ("取消", App.Ui.CloseModals));
            dialog.Q(className: "dialog").AddToClassList("dialog--loss");
            App.Ui.ShowModal(dialog);
        }
    }
}
