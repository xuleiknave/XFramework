using System.Linq;
using Starline.Online;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starline.Client
{
    /// <summary>
    /// Links the WeChat mini game and the app to one account (layout AccountLink.uxml): one device shows
    /// a six-digit code, the other enters it. When both sides already have saves the player picks which
    /// saves stay; the server moves the others to the deleted saves (server/README.md, link codes).
    /// </summary>
    public static class AccountLinkDialog
    {
        public static void Show(GameApp app)
        {
            VisualElement dialog = Ui.Load("AccountLink");
            VisualElement start = dialog.Find<VisualElement>("start");
            VisualElement choice = dialog.Find<VisualElement>("choice");
            Label code = dialog.Find<Label>("code");
            Label codeHint = dialog.Find<Label>("code-hint");
            TextField input = dialog.Find<TextField>("input");
            Label error = dialog.Find<Label>("error");
            Button create = dialog.Find<Button>("create");
            Button redeem = dialog.Find<Button>("redeem");
            input.maxLength = ServerLimits.LinkCodeLength;
            input.RegisterValueChangedCallback(_ => error.text = string.Empty);
            choice.style.display = DisplayStyle.None;
            bool busy = false;

            void SetBusy(bool value)
            {
                busy = value;
                create.SetEnabled(!value);
                redeem.SetEnabled(!value);
                dialog.Find<Button>("keep-mine").SetEnabled(!value);
                dialog.Find<Button>("keep-linked").SetEnabled(!value);
            }

            async void Create()
            {
                if (busy)
                {
                    return;
                }
                SetBusy(true);
                error.text = string.Empty;
                try
                {
                    ServerResponse r = await GameServerClient.Call(new ServerRequest { op = ServerOps.CreateLinkCode });
                    code.text = r.linkCode;
                    long minutes = (r.linkExpiresAt - r.serverTime + 59_999) / 60_000;
                    codeHint.text = Loc.Ui("link.code_hint", minutes);
                }
                catch (ServerCallException e)
                {
                    error.text = e.Describe();
                }
                SetBusy(false);
            }

            async void Redeem(string keep)
            {
                string entered = new string((input.value ?? string.Empty).Where(char.IsDigit).ToArray());
                if (entered.Length != ServerLimits.LinkCodeLength)
                {
                    error.text = Loc.Ui("link.need_code", ServerLimits.LinkCodeLength);
                    return;
                }
                if (busy)
                {
                    return;
                }
                SetBusy(true);
                error.text = string.Empty;
                ServerResponse linked;
                try
                {
                    linked = await GameServerClient.Call(new ServerRequest { op = ServerOps.RedeemLinkCode, code = entered, keep = keep });
                }
                catch (ServerCallException e) when (e.Code == ServerErrors.LinkChoice && e.Response != null)
                {
                    dialog.Find<Label>("mine-saves").text = Describe(e.Response.saves);
                    dialog.Find<Label>("linked-saves").text = Describe(e.Response.linkedSaves);
                    start.style.display = DisplayStyle.None;
                    choice.style.display = DisplayStyle.Flex;
                    SetBusy(false);
                    return;
                }
                catch (ServerCallException e)
                {
                    error.text = e.Describe();
                    SetBusy(false);
                    return;
                }
                // This device now uses the other account: its session and saves replace ours.
                app.SaveProfile(force: true);
                AccountService.Relinked(linked);
                try
                {
                    await app.LoadSaves();
                }
                catch (ServerCallException e)
                {
                    error.text = e.Describe();
                    SetBusy(false);
                    return;
                }
                app.Ui.CloseModals();
                app.ShowMainMenu();
            }

            create.clicked += Create;
            redeem.clicked += () => Redeem(null);
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Redeem(null);
                }
            }, TrickleDown.TrickleDown);
            dialog.Find<Button>("keep-mine").clicked += () => Redeem(ServerOps.KeepMine);
            dialog.Find<Button>("keep-linked").clicked += () => Redeem(ServerOps.KeepLinked);
            dialog.Find<Button>("close").clicked += app.Ui.CloseModals;
            app.Ui.ShowModal(dialog);
        }

        /// <summary>One line per save: name, slot and play time.</summary>
        private static string Describe(SaveInfo[] saves)
        {
            if (saves == null || saves.Length == 0)
            {
                return Loc.Ui("link.no_saves");
            }
            return string.Join("\n", saves.Select(s =>
                Loc.Ui("link.save_line", s.name, s.slot + 1, CommanderProfile.FormatPlayTime(s.playMs / 1000.0))));
        }
    }
}
