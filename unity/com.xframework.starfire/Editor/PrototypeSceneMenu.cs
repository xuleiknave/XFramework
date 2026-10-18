using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using XFramework.Client;

namespace XFramework.Editor
{
    public static class PrototypeSceneMenu
    {
        private const string ScenePath = "Assets/Scenes/P0Battle.unity";

        [MenuItem("XFramework/创建 P0 战斗原型场景")]
        private static void CreatePrototypeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("BattleView").AddComponent<BattleView>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("已创建 " + ScenePath + "，点击 Play 开始试玩。");
        }
    }
}
