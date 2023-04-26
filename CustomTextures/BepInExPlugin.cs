using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RewiredConsts;
using SodaDen.Pacha;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace CustomTextures
{
    [BepInPlugin("aedenthorn.CustomTextures", "Custom Textures", "0.6.1")]
    public partial class BepInExPlugin : BaseUnityPlugin
    {
        private static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<KeyboardShortcut> reloadKey;

        public static Dictionary<string, string> customTextureDict = new Dictionary<string, string>();
        public static Dictionary<string, Texture2D> cachedTextureDict = new Dictionary<string, Texture2D>();
        public static Dictionary<string, Sprite> cachedSprites = new Dictionary<string, Sprite>();
        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                context.Logger.LogInfo(str);
        }
        private void Awake()
        {

            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", true, "Enable debug logs");
            reloadKey = Config.Bind<KeyboardShortcut>("General", "ReloadKey", new KeyboardShortcut(KeyCode.F5), "Key to press to reload textures from disk");

            var harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), Info.Metadata.GUID);

            LoadCustomTextures();

            SceneManager.sceneLoaded += SceneManager_sceneLoaded;

        }

        [HarmonyPatch(typeof(PlayerState), "Update")]
        static class PlayerState_Update_Patch
        {
            static void Postfix()
            {
                if (!modEnabled.Value)
                    return;

                if (reloadKey.Value.IsDown())
                {
                    cachedTextureDict.Clear();
                    LoadCustomTextures();
                }
            }
        }
        private static void LoadCustomTextures()
        {
            customTextureDict.Clear();
            string path = AedenthornUtils.GetAssetPath(context, true);
            foreach (string file in Directory.GetFiles(path, "*.png", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                customTextureDict.Add(name, file);
                Dbgl($"Loaded texture path for: {name}");
            }
            Dbgl($"Loaded {customTextureDict.Count} textures");
        }
        private static Texture2D GetTexture(string path)
        {
            if (cachedTextureDict.TryGetValue(path, out var tex))
                return tex;
            TextureCreationFlags flags = new TextureCreationFlags();
            tex = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_UNorm, flags);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.wrapModeU = TextureWrapMode.Clamp;
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.wrapModeW = TextureWrapMode.Clamp;
            cachedTextureDict[path] = tex;
            return tex;
        }
        private static void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            if (!modEnabled.Value)
                return;
            Stopwatch s = new Stopwatch();
            Dbgl($"Replacing scene textures");
            s.Start(); 
            foreach (var c in FindObjectsOfType<Component>())
            {
                if(c is SpriteRenderer)
                {
                    (c as SpriteRenderer).sprite = TryGetReplacementSprite((c as SpriteRenderer).sprite);
                }
                else if (c is Renderer)
                {
                    foreach (var m in (c as Renderer).materials)
                    {
                        foreach (var n in m.GetTexturePropertyNames())
                        {
                            if (m.HasProperty(n) && m.GetTexture(n) is Texture2D)
                            {
                                m.SetTexture(n, TryGetReplacementTexture(m.name, (Texture2D)m.GetTexture(n)));
                            }
                        }
                    }
                }
                else if (c is MonoBehaviour)
                {
                    FindSpritesInObject(c);
                }
            }
            Dbgl($"Time to replace textures: {s.ElapsedMilliseconds}ms");
        }

        private static void FindSpritesInObject(object c)
        {
            foreach (var f in AccessTools.GetDeclaredFields(c.GetType()))
            {
                var fo = f.GetValue(c);
                if (f.FieldType == typeof(Sprite))
                {
                    //Dbgl($"found Sprite {c.GetType().Name} {f.Name}");

                    f.SetValue(c, TryGetReplacementSprite((Sprite)fo));
                }
                else if (f.FieldType == typeof(List<Sprite>))
                {
                    var field = (List<Sprite>)fo;
                    if (field is null)
                        continue;
                    //Dbgl($"found List<Sprite> {c.GetType().Name} {f.Name}");
                    for (int i = 0; i < field.Count; i++)
                    {
                        field[i] = TryGetReplacementSprite(field[i]);
                    }
                }
                else if (f.FieldType == typeof(Sprite[]))
                {
                    var field = (Sprite[])fo;
                    if (field is null)
                        continue;
                    //Dbgl($"found Sprite[] {c.GetType().Name} {f.Name}");
                    for (int i = 0; i < field.Length; i++)
                    {
                        field[i] = TryGetReplacementSprite(field[i]);
                    }
                }
                else if (f.FieldType == typeof(SpriteRenderer))
                {
                    var field = (SpriteRenderer)fo;
                    if (field is null)
                        continue;
                    Dbgl($"found SpriteRenderer {c.GetType().Name} {f.Name} {field.sprite?.texture?.name}");
                    field.sprite = TryGetReplacementSprite(field.sprite);

                }
                else if (f.FieldType == typeof(SpriteRenderer[]))
                {
                    var field = (SpriteRenderer[])fo;
                    if (field is null)
                        continue;
                    Dbgl($"found SpriteRenderer[] {c.GetType().Name} {f.Name}");
                    for (int i = 0; i < field.Length; i++)
                    {
                        Dbgl($"SpriteRenderer {field[i].sprite?.name} {field[i].sprite?.texture?.name}");
                        field[i].sprite = TryGetReplacementSprite(field[i].sprite);
                    }
                }
                else if (f.FieldType == typeof(Dictionary<string, Sprite>))
                {
                    var field = (Dictionary<string, Sprite>)fo;
                    if (field is null)
                        continue;
                    //Dbgl($"found Sprite[] {c.GetType().Name} {f.Name}");
                    foreach (var k in field.Keys.ToArray())
                    {
                        field[k] = TryGetReplacementSprite(field[k]);
                    }
                }
                else if (f.FieldType == typeof(Dictionary<string, List<Sprite>>))
                {
                    var field = (Dictionary<string, List<Sprite>>)fo;
                    if (field is null)
                        continue;
                    //Dbgl($"found Sprite[] {c.GetType().Name} {f.Name}");
                    foreach (var k in field.Keys.ToArray())
                    {
                        for (int i = 0; i < field[k].Count; i++)
                        {
                            field[k][i] = TryGetReplacementSprite(field[k][i]);
                        }
                    }
                }
                else if (fo != null && !(fo is Enum) && !(fo is MonoBehaviour) && !(fo is IEnumerable) && f.FieldType.Namespace == "SodaDen.Pacha")
                {
                    //Dbgl($"checking field {c.GetType().Name} {f.Name}");

                    FindSpritesInObject(fo);
                }
            }
        }

        private static Sprite TryGetReplacementSprite(Sprite oldSprite)
        {
            if (oldSprite == null)
                return null;
            var textureName = oldSprite.texture?.name;
            if (textureName == null)
                return oldSprite;
            if (cachedSprites.TryGetValue(oldSprite.name + "_" + textureName, out Sprite newSprite))
                return newSprite;
            if (!customTextureDict.TryGetValue(textureName, out string path))
                return oldSprite;

            Dbgl($"replacing sprite {oldSprite.texture.name}");
            var newTex = GetTexture(path);
            newTex.name = oldSprite.texture.name;
            newSprite = Sprite.Create(newTex, oldSprite.rect, new Vector2(oldSprite.pivot.x / oldSprite.rect.width, oldSprite.pivot.y / oldSprite.rect.height), oldSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, oldSprite.border, true);
            newSprite.name = oldSprite.name;
            cachedSprites.Add(newSprite.name + "_" + textureName, newSprite);
            return newSprite;
        }
        private static Texture2D TryGetReplacementTexture(string objectName, Texture2D oldTexture)
        {
            var textureName = oldTexture?.name;
            if (textureName == null)
                return oldTexture;
            if (!customTextureDict.TryGetValue(textureName, out string path) && !customTextureDict.TryGetValue(objectName + "_" + textureName, out path))
                return oldTexture;

            Dbgl($"replacing texture {oldTexture.name} on object {objectName}");
            var newTex = GetTexture(path);
            newTex.name = oldTexture.name;
            return newTex;
        }

        //[HarmonyPatch(typeof(GameObject), nameof(GameObject.SetActive))]
        static class GameObject_SetActive_Patch
        {
            static void Prefix(GameObject __instance, bool value)
            {
                if (!modEnabled.Value || !value)
                    return;

                foreach (var c in __instance.GetComponents(typeof(Component)))
                {
                    if (c is SpriteRenderer && (c as SpriteRenderer).sprite != null)
                    {
                        Dbgl($"Found sprite renderer {c.name} with sprite {(c as SpriteRenderer).sprite} and texture {(c as SpriteRenderer).sprite.texture.name}");

                        (c as SpriteRenderer).sprite = TryGetReplacementSprite((c as SpriteRenderer).sprite);
                    }
                    else if (c is Renderer)
                    {
                        foreach (var m in (c as Renderer).materials)
                        {
                            foreach (var n in m.GetTexturePropertyNames())
                            {
                                if(m.HasProperty(n) && m.GetTexture(n) is Texture2D)
                                {
                                    m.SetTexture(n, TryGetReplacementTexture(m.name, (Texture2D)m.GetTexture(n)));
                                }
                            }
                        }
                    }
                    else if (c is MonoBehaviour)
                    {
                        FindSpritesInObject(c);
                    }
                }
            }
        }
        [HarmonyPatch(typeof(PlayerBodyFrame), nameof(PlayerBodyFrame.GetSprites), new Type[] { typeof(int), typeof(int), typeof(int) })]
        static class PlayerBodyFrame_GetSprites_Patch
        {
            static void Postfix(PlayerBodyFrame __instance, ref PlayerBodyFrame.FrameSprites __result)
            {
                if (!modEnabled.Value)
                    return;
                Dbgl($"Checking frame sprites");

                FindSpritesInObject(__result);
            }
        }
        [HarmonyPatch(typeof(AnimalRenderer), "Awake")]
        static class AnimalRenderer_Awake_Patch
        {
            static void Postfix(AnimalRenderer __instance, SpriteRenderer[] ___allRenderers)
            {
                if (!modEnabled.Value)
                    return;
                foreach (SpriteRenderer sr in ___allRenderers)
                {
                    sr.sprite = TryGetReplacementSprite(sr.sprite);
                }
            }
        }
    }
}
