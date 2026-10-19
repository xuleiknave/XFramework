using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Starline.Client;

namespace Starline.Editor
{
    public static class PrototypeSceneMenu
    {
        private const string ScenePath = "Assets/Scenes/PlayablePrototype.unity";

        [MenuItem("Starline/创建可玩原型场景")]
        private static void CreatePrototypeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Game").AddComponent<GameController>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("已创建 " + ScenePath + "，点击 Play 开始试玩。");
        }
    }
}
