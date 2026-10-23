using System.IO;
using UnityEditor;
using UnityEngine;
using Starline.Client;

namespace Starline.Editor
{
    /// <summary>
    /// Creates the editable menu night scene, Assets/Resources/StarlineMenuScene.prefab, from
    /// the default layout. Double-click the prefab to edit it: move the models, change their
    /// unit id and glow in the Inspector, and aim the camera with the "Camera Anchor" object.
    /// </summary>
    [InitializeOnLoad]
    internal static class MenuScenePrefab
    {
        public const string PrefabPath = "Assets/Resources/" + MenuScene.PrefabResource + ".prefab";

        static MenuScenePrefab()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                {
                    Create();
                }
            };
        }

        [MenuItem("Starline/生成主菜单夜景预制体", priority = 20)]
        private static void CreateFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
                && !EditorUtility.DisplayDialog("主菜单夜景", PrefabPath + " 已经存在。重新生成会覆盖你对它做过的修改，确定吗？", "覆盖", "取消"))
            {
                return;
            }
            Create();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        internal static void Create()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            MenuSceneRoot scene = MenuSceneDefaults.Build();
            scene.gameObject.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(scene.gameObject, PrefabPath);
            Object.DestroyImmediate(scene.gameObject);
            Debug.Log("Starline：已生成主菜单夜景 " + PrefabPath + "，双击即可编辑。");
        }
    }
}
