using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Starline.Client;

namespace Starline.Editor
{
    /// <summary>
    /// One-click project setup: the UI Toolkit panel settings (with the default runtime
    /// theme), the localization tables and the Main scene, registered in the build settings.
    /// The panel settings are also created automatically the first time the project opens.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string ThemePath = "Assets/UI/StarlineRuntimeTheme.tss";
        private const string PanelSettingsPath = "Assets/Resources/" + UiRoot.PanelSettingsResource + ".asset";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) == null)
                {
                    CreatePanelSettings();
                }
            };
        }

        [MenuItem("Starline/初始化工程（场景与界面配置）", priority = 0)]
        private static void SetUpProject()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            CreatePanelSettings();
            LocalizationSetup.Import();
            CreateMainScene();
            Debug.Log("Starline：已创建 " + ScenePath + " 并加入构建列表，点击 Play 开始游戏。");
        }

        private static void CreatePanelSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath));
            if (!File.Exists(ThemePath))
            {
                // The same content Unity writes when it creates a default runtime theme.
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }

            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Ui.ConfigurePanel(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("Starline：已生成界面配置 " + PanelSettingsPath);
        }

        private static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera") { tag = "MainCamera" };
            camera.AddComponent<Camera>();
            new GameObject("Game").AddComponent<GameApp>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
