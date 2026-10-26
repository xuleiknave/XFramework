using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>Modal that names a new commander in an empty save slot (layout NewCommander.uxml).</summary>
    public static class NewCommanderDialog
    {
        public static void Show(GameApp app, int slot, Action<CommanderProfile> onCreated)
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
                app.Ui.CloseModals();
                onCreated(profile);
            }

            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Confirm();
                }
            }, TrickleDown.TrickleDown);
            dialog.Find<Button>("confirm").clicked += Confirm;
            dialog.Find<Button>("cancel").clicked += app.Ui.CloseModals;
            app.Ui.ShowModal(dialog);
            field.schedule.Execute(() => field.Focus());
        }
    }
}
