using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Materials for the menu scene components. They run in edit mode too, so everything they
    /// create is DontSave: nothing generated ends up in the prefab, only the settings do.
    /// </summary>
    public static class MenuLook
    {
        public const HideFlags Generated = HideFlags.DontSave | HideFlags.NotEditable;

        public static Material Lit(Color color)
        {
            Material material = ModelLibrary.CreateHullMaterial();
            material.name = "Menu Lit";
            material.mainTexture = null;
            material.color = color;
            material.SetColor("_EmissionColor", Color.black);
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        public static Material Unlit(Color color)
        {
            var material = new Material(ViewUtil.Unlit) { name = "Menu Unlit", color = color, hideFlags = HideFlags.DontSave };
            return material;
        }

        public static void MarkGenerated(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = Generated;
            }
        }
    }
}
