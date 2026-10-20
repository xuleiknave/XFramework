using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Labels that follow the ships: name and star during deployment; hull and shield bars,
    /// supply and ammunition state in battle. Lives in the UI's world layer.
    /// </summary>
    public sealed class UnitOverlay
    {
        private readonly UiRoot _ui;
        private readonly BattleStage _stage;
        private readonly Dictionary<int, Tag> _tags = new Dictionary<int, Tag>();
        private bool _battleMode;

        private sealed class Tag
        {
            public VisualElement Root;
            public VisualElement Bars;
            public VisualElement Hp;
            public VisualElement Shield;
            public Label Status;
            public Label Name;
        }

        public UnitOverlay(UiRoot ui, BattleStage stage)
        {
            _ui = ui;
            _stage = stage;
            _stage.ViewsRebuilt += Rebuild;
        }

        /// <summary>Battle mode shows bars and ammo; deployment mode shows names.</summary>
        public void SetBattleMode(bool battle)
        {
            _battleMode = battle;
            Rebuild();
        }

        public void Detach()
        {
            _stage.ViewsRebuilt -= Rebuild;
            Clear();
        }

        public void Clear()
        {
            foreach (Tag tag in _tags.Values)
            {
                tag.Root.RemoveFromHierarchy();
            }
            _tags.Clear();
        }

        private void Rebuild()
        {
            Clear();
            foreach (KeyValuePair<int, UnitView> kv in _stage.Views)
            {
                _tags.Add(kv.Key, CreateTag(kv.Value));
            }
        }

        private Tag CreateTag(UnitView view)
        {
            var tag = new Tag { Root = Ui.Box("unit-tag") };
            tag.Root.pickingMode = PickingMode.Ignore;
            tag.Status = Ui.Label("", "unit-tag-status");
            tag.Root.Add(tag.Status);

            tag.Bars = Ui.Box("unit-bars", view.Def.Size >= 2 ? "unit-bars--large" : "unit-bars--small");
            tag.Hp = Ui.Box("unit-bar-hp", view.Side == BoardLayout.PlayerSide ? "unit-bar-hp--player" : "unit-bar-hp--enemy");
            tag.Shield = Ui.Box("unit-bar-shield");
            tag.Bars.Add(tag.Hp);
            tag.Bars.Add(tag.Shield);
            tag.Root.Add(tag.Bars);

            bool named = view.Side == BoardLayout.PlayerSide || view.Star > 1 || view.Def.Size >= 2;
            tag.Name = Ui.Label(view.Def.DisplayName + (view.Star > 1 ? " " + Ui.Stars(view.Star) : ""), "unit-tag-name");
            tag.Root.Add(tag.Name);

            tag.Bars.SetVisible(_battleMode);
            tag.Name.SetVisible(!_battleMode && named);
            SetPicking(tag.Root);
            _ui.WorldLayer.Add(tag.Root);
            return tag;
        }

        /// <summary>Moves every tag to its ship and refreshes the numbers. Call every frame.</summary>
        public void Update()
        {
            IPanel panel = _ui.Panel;
            if (panel == null)
            {
                return;
            }
            Camera camera = _stage.Camera;
            var gone = new List<int>();
            foreach (KeyValuePair<int, Tag> kv in _tags)
            {
                if (!_stage.Views.TryGetValue(kv.Key, out UnitView view) || view == null)
                {
                    gone.Add(kv.Key);
                    continue;
                }
                Tag tag = kv.Value;
                Vector3 head = view.HeadPosition;
                bool inFront = camera.WorldToScreenPoint(head).z > 0f;
                tag.Root.SetVisible(inFront);
                if (!inFront)
                {
                    continue;
                }
                Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, head, camera);
                tag.Root.style.left = p.x;
                tag.Root.style.top = p.y;

                if (_battleMode)
                {
                    tag.Hp.style.width = Length.Percent(view.HpFraction * 100f);
                    tag.Shield.style.width = Length.Percent(view.ShieldFraction * 100f);
                }
                UpdateStatus(tag, view);
            }
            foreach (int id in gone)
            {
                _tags[id].Root.RemoveFromHierarchy();
                _tags.Remove(id);
            }
        }

        private void UpdateStatus(Tag tag, UnitView view)
        {
            string text = null;
            string kind = null;
            if (view.Def.HasWeapon && !view.Supplied)
            {
                text = _battleMode ? "断补给" : "✕ 未接入";
                kind = "bad";
            }
            else if (view.InGrace)
            {
                text = "离网 " + Mathf.CeilToInt(view.GraceSecondsLeft) + " 秒";
                kind = "warn";
            }
            if (_battleMode && view.Def.MaxAmmo > 0 && (view.Ammo < view.Def.MaxAmmo || !view.Supplied))
            {
                text = (text != null ? text + " " : "") + "弹 " + view.Ammo + "/" + view.Def.MaxAmmo;
                if (view.Ammo == 0)
                {
                    kind = "bad";
                }
            }
            tag.Status.text = text ?? "";
            tag.Status.SetVisible(text != null);
            tag.Status.EnableInClassList("bad", kind == "bad");
            tag.Status.EnableInClassList("warn", kind == "warn");
        }

        private static void SetPicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in element.Children())
            {
                SetPicking(child);
            }
        }
    }
}
