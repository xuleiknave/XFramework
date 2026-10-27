using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// Save slots, opened from the main menu: pick a slot (click to select, double-click or the
    /// use button to switch to it), create a commander in an empty slot, or delete a save.
    /// </summary>
    public sealed class SaveSlotsScreen : UiScreen
    {
        private readonly CommanderProfile[] _profiles = new CommanderProfile[SaveRepository.SlotCount];
        private readonly VisualElement[] _cards = new VisualElement[SaveRepository.SlotCount];
        private readonly VisualElement _slots;
        private readonly Button _enter;
        private int _selected = -1;

        public override bool UsesMenuScene => true;

        public SaveSlotsScreen(GameApp app) : base(app, "SaveSlots")
        {
            _slots = Find<VisualElement>("slots");
            _enter = Button("enter", () => Enter(_selected));
            Button("back", App.ShowMainMenu);
            _selected = App.Profile != null ? App.Profile.slot : -1;
            Reload();
        }

        private void Reload()
        {
            int lastPlayed = -1;
            for (int i = 0; i < _profiles.Length; i++)
            {
                _profiles[i] = App.Saves.Get(i);
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
            card.Add(Ui.Label(Loc.Ui("slots.slot", slot + 1), "small", "muted", "eyebrow"));
            card.Add(Ui.Label(profile.name, "slot-name"));
            card.Add(Ui.Box("slot-line"));
            card.Add(Row(Loc.Ui("slots.play_time"), CommanderProfile.FormatPlayTime(profile.playSeconds)));
            card.Add(Row(Loc.Ui("slots.last_played"), profile.LastPlayed.ToString("yyyy-MM-dd HH:mm")));
            card.Add(Row(Loc.Ui("slots.runs"), profile.runsWon > 0
                ? Loc.Ui("slots.runs_won", profile.runsWon, profile.runsStarted)
                : Loc.Ui("slots.runs_none_won", profile.runsStarted)));
            card.Add(Ui.Box("spacer"));
            VisualElement foot = Ui.Box("row", "slot-foot");
            foot.Add(Ui.Button(Loc.Ui("slots.delete"), () => ConfirmDelete(slot), "btn-small", "btn-text"));
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
            card.Add(Ui.Label(Loc.Ui("slots.slot", slot + 1), "small", "muted", "eyebrow"));
            card.Add(Ui.Label("+", "slot-plus"));
            card.Add(Ui.Label(Loc.Ui("slots.new"), "slot-new"));
            card.Add(Ui.Label(Loc.Ui("slots.new_hint"), "small", "muted"));
            card.RegisterCallback<ClickEvent>(_ => NewCommanderDialog.Show(App, slot, profile =>
            {
                App.SignIn(profile);
                App.ShowMainMenu();
            }));
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
            App.ShowMainMenu();
        }

        private void ConfirmDelete(int slot)
        {
            CommanderProfile profile = _profiles[slot];
            bool busy = false;
            VisualElement dialog = null;
            dialog = Ui.Dialog(Loc.Ui("slots.delete_title"),
                Loc.Ui("slots.delete_body", profile.name, CommanderProfile.FormatPlayTime(profile.playSeconds)),
                (Loc.Ui("slots.delete_confirm"), async () =>
                {
                    if (busy)
                    {
                        return;
                    }
                    busy = true;
                    try
                    {
                        await App.Saves.Delete(slot);
                    }
                    catch (ServerCallException e)
                    {
                        busy = false;
                        dialog.Q<Label>(className: "dialog-body").text = Loc.Ui("slots.delete_failed", e.Describe());
                        return;
                    }
                    if (App.Profile != null && App.Profile.slot == slot)
                    {
                        App.SignOut();
                    }
                    App.Ui.CloseModals();
                    Reload();
                }),
                (Loc.Ui("common.cancel"), App.Ui.CloseModals));
            dialog.Q(className: "dialog").AddToClassList("dialog--loss");
            App.Ui.ShowModal(dialog);
        }
    }
}
