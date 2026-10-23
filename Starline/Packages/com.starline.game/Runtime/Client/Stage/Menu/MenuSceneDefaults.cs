using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// The default layout of the menu scene: night over 灯塔城. Used by the editor menu
    /// (Starline &gt; 生成主菜单夜景预制体) to create the prefab, and at runtime when the prefab is
    /// missing. After the prefab exists, edit the prefab; this code is only the starting point.
    /// </summary>
    public static class MenuSceneDefaults
    {
        /// <summary>Builds the scene, inactive; activate it (or save it as a prefab) afterwards.</summary>
        public static MenuSceneRoot Build(string name = "StarlineMenuScene")
        {
            // Inactive while being built, so components only wake up once their settings are filled in.
            var root = new GameObject(name);
            root.SetActive(false);
            MenuSceneRoot scene = root.AddComponent<MenuSceneRoot>();

            // Camera pose: the left third of the screen is covered by the menu, so the subject sits right of centre.
            Transform anchor = Child(root.transform, "Camera Anchor", new Vector3(0f, 3.2f, -12f));
            anchor.localRotation = Quaternion.LookRotation(new Vector3(3.5f, 2.2f, 6f) - anchor.localPosition, Vector3.up);
            Camera preview = anchor.gameObject.AddComponent<Camera>();
            preview.fieldOfView = scene.fieldOfView;
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = scene.sky;
            scene.cameraAnchor = anchor;

            // Moonlight from behind, up and to the left.
            Light moon = Child(root.transform, "Moonlight", Vector3.zero).gameObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.66f, 1f);
            moon.intensity = 0.6f;
            moon.shadows = LightShadows.Soft;
            moon.transform.localRotation = Quaternion.Euler(28f, 160f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            ViewUtil.DestroySafe(ground.GetComponent<Collider>());
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.05f, 20f);
            ground.transform.localScale = new Vector3(240f, 0.05f, 240f);
            ground.AddComponent<MenuTint>().color = new Color(0.05f, 0.07f, 0.11f);

            // The source tower of 灯塔城 and the light it throws on the expedition.
            Transform tower = Prop(root.transform, "源晶塔", "supply_ship", new Vector3(5f, 0f, 4f), 200f, 1.5f, ViewUtil.SupplyColor, 3f).transform;
            Light towerLight = Child(root.transform, "源晶塔灯光", new Vector3(5f, 5.5f, 3f)).gameObject.AddComponent<Light>();
            towerLight.type = LightType.Point;
            towerLight.color = ViewUtil.SupplyColor;
            towerLight.intensity = 6f;
            towerLight.range = 16f;
            towerLight.shadows = LightShadows.None;
            towerLight.gameObject.AddComponent<MenuLightPulse>();

            // The expedition (cartoon characters idling on their glow rings), facing the way out along the dark crystals.
            Transform army = Child(root.transform, "远征军", Vector3.zero);
            Hero(army, "布兰 · 盾卫", "shield_guard", 3, new Vector3(2.4f, 0f, 1.6f), 30f);
            Hero(army, "塞德里克 · 长枪手", "lancer", 2, new Vector3(3.9f, 0f, 0.2f), 15f);
            Hero(army, "艾琳 · 牧师", "priest", 3, new Vector3(5.4f, 0f, -0.4f), 25f);
            Hero(army, "泽恩 · 风暴法师", "storm_mage", 4, new Vector3(6.9f, 0f, 0.6f), 40f);
            Hero(army, "格雷 · 猎人", "hunter", 2, new Vector3(8.3f, 0f, 2.3f), 50f);
            Hero(army, "菲恩 · 狮鹫侍从", "griffin_squire", 3, new Vector3(1.0f, 2.2f, 4.5f), 35f).bobHeight = 0.18f;

            // The mana network: lit up to the first crystal, dark beyond it.
            Transform network = Child(root.transform, "魔网", Vector3.zero);
            Vector3[] crystals =
            {
                new Vector3(8.5f, 0f, 10f), new Vector3(11f, 0f, 17f), new Vector3(13f, 0f, 25f),
                new Vector3(14.2f, 0f, 34f), new Vector3(15f, 0f, 44f), new Vector3(15.4f, 0f, 55f),
            };
            MenuProp lit = Prop(network, "法力水晶（亮）", "relay", crystals[0], 0f, 1f, ViewUtil.SupplyColor, 2.4f);
            lit.glowPulse = 0.6f;
            for (int i = 1; i < crystals.Length; i++)
            {
                Prop(network, "法力水晶（暗）" + i, "relay", crystals[i], 0f, 1f, ViewUtil.SupplyColor, 0.04f);
            }
            MenuGlowLine line = Child(network, "魔网光带", Vector3.zero).gameObject.AddComponent<MenuGlowLine>();
            line.from = tower;
            line.to = lit.transform;

            // Far away, the undead are waiting.
            Prop(root.transform, "亡灵祭坛", "hive", new Vector3(19f, 0f, 70f), 200f, 1.6f, ViewUtil.UndeadGlowColor, 3f);

            Child(root.transform, "山影", Vector3.zero).gameObject.AddComponent<MenuMountains>();
            Child(root.transform, "星空", anchor.localPosition).gameObject.AddComponent<MenuSky>();

            GameObject moonDisc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            moonDisc.name = "月亮";
            ViewUtil.DestroySafe(moonDisc.GetComponent<Collider>());
            moonDisc.transform.SetParent(root.transform, false);
            moonDisc.transform.localPosition = new Vector3(-60f, 75f, 200f);
            moonDisc.transform.localScale = Vector3.one * 14f;
            MenuTint moonTint = moonDisc.AddComponent<MenuTint>();
            moonTint.color = new Color(0.9f, 0.93f, 1f);
            moonTint.unlit = true;
            moonTint.brightness = 1.8f;

            return scene;
        }

        private static Transform Child(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private static MenuProp Hero(Transform parent, string name, string unitId, int star, Vector3 localPos, float yaw)
        {
            MenuProp prop = Prop(parent, name, unitId, localPos, yaw, 1f, ViewUtil.PlayerColor, 2.5f);
            prop.star = star;
            prop.groundRing = 0.7f;
            return prop;
        }

        private static MenuProp Prop(Transform parent, string name, string unitId, Vector3 localPos, float yaw, float scale, Color glow, float strength)
        {
            Transform t = Child(parent, name, localPos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one * scale;
            MenuProp prop = t.gameObject.AddComponent<MenuProp>();
            prop.unitId = unitId;
            prop.glowColor = glow;
            prop.glowStrength = strength;
            return prop;
        }
    }
}
