// Editor-only: screenshot layout, asset indexing and prefab generation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ScreenshotUIComposer
{
    [Serializable]
    public sealed class UILayout
    {
        public int width = 1080;
        public int height = 1920;
        public List<UIElement> elements = new List<UIElement>();

        public static UILayout Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Layout JSON is empty.");
            int first = json.IndexOf('{'), last = json.LastIndexOf('}');
            if (first < 0 || last <= first) throw new InvalidDataException("No JSON object in response.");
            var result = JsonConvert.DeserializeObject<UILayout>(json.Substring(first, last - first + 1),
                new JsonSerializerSettings { MaxDepth = 32, TypeNameHandling = TypeNameHandling.None });
            result.Validate();
            return result;
        }

        public void Validate()
        {
            if (width < 1 || height < 1 || width > 16384 || height > 16384)
                throw new InvalidDataException("Canvas dimensions must be 1..16384.");
            if (elements == null || elements.Count > 1000) throw new InvalidDataException("Invalid element list (maximum 1000).");
            foreach (var e in elements)
            {
                if (e == null || (e.kind != "image" && e.kind != "text" && e.kind != "panel"))
                    throw new InvalidDataException("Element kind must be image, text or panel.");
                if (!Finite(e.x) || !Finite(e.y) || !Finite(e.width) || !Finite(e.height) ||
                    !Finite(e.fontSize) || e.width <= 0 || e.height <= 0 || e.fontSize <= 0)
                    throw new InvalidDataException("Element has invalid bounds or font size.");
                Color c;
                if (!ColorUtility.TryParseHtmlString(e.color, out c))
                    throw new InvalidDataException("Invalid HTML color: " + e.color);
            }
        }

        static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }

    [Serializable]
    public sealed class UIElement
    {
        public string name = "Element";
        public string kind = "image";
        // Absolute screenshot pixels, origin top left. List order = back to front.
        public float x, y, width = 100, height = 100;
        public int spriteId = -1, fontId = -1;
        public string spriteKey = "", fontKey = "";
        public string text = "", color = "#FFFFFFFF";
        public float fontSize = 32;
        public bool sliced, preserveAspect, bold, button, fitText;
        public float outlineWidth;
        public string outlineColor = "#00000000";
        public string alignment = "center";
        public string imagePrompt = "";
    }

    public sealed class AssetCatalog
    {
        public readonly List<Sprite> Sprites = new List<Sprite>();
        public readonly List<TMP_FontAsset> Fonts = new List<TMP_FontAsset>();
        public readonly List<string> SpriteKeys = new List<string>();
        public readonly List<string> FontKeys = new List<string>();

        public static string AssetFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Choose an asset folder.");
            string full = Path.GetFullPath(value).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!full.Equals(assets, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Folder must be inside this project's Assets. Use Import external folder first.");
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
            return "Assets" + full.Substring(assets.Length);
        }

        public static AssetCatalog Scan(string spriteFolder, string fontFolder, string filter)
        {
            var c = new AssetCatalog();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { AssetFolder(spriteFolder) }).OrderBy(g => g))
            {
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>())
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        sprite.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    c.Sprites.Add(sprite);
                    c.SpriteKeys.Add(Key(sprite));
                }
            }
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { AssetFolder(fontFolder) }).OrderBy(g => g))
            {
                foreach (var font in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<TMP_FontAsset>())
                { c.Fonts.Add(font); c.FontKeys.Add(Key(font)); }
            }
            return c;
        }

        public static int CreateTMPFonts(string folder)
        {
            string assetFolder = AssetFolder(folder);
            var guids = AssetDatabase.FindAssets("t:Font", new[] { assetFolder });
            int created = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                if (font == null) continue;
                string output = Path.ChangeExtension(path, null) + " UIComposer SDF.asset";
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(output) != null) continue;
                var tmp = TMP_FontAsset.CreateFontAsset(font);
                if (tmp == null) throw new InvalidDataException("Cannot create TMP font: " + path);
                tmp.name = font.name + " UIComposer SDF";
                if (TMP_Settings.defaultFontAsset != null)
                    tmp.material.shader = TMP_Settings.defaultFontAsset.material.shader;
                tmp.material.name = tmp.name + " Material";
                tmp.atlasTexture.name = tmp.name + " Atlas";
                tmp.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                tmp.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(tmp, output);
                tmp.material.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(tmp.material, tmp);
                foreach (var atlas in tmp.atlasTextures)
                { atlas.hideFlags = HideFlags.None; AssetDatabase.AddObjectToAsset(atlas, tmp); }
                EditorUtility.SetDirty(tmp);
                created++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created;
        }

        public static string Key(Object asset)
        {
            string guid; long id;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out id) ? guid + ":" + id : "";
        }

        public void Bind(UILayout layout)
        {
            foreach (var e in layout.elements)
            {
                if (string.IsNullOrEmpty(e.spriteKey) && e.spriteId >= 0 && e.spriteId < Sprites.Count)
                    e.spriteKey = SpriteKeys[e.spriteId];
                if (string.IsNullOrEmpty(e.fontKey) && e.fontId >= 0 && e.fontId < Fonts.Count)
                    e.fontKey = FontKeys[e.fontId];
            }
        }

        public Sprite SpriteFor(UIElement e)
        {
            int index = SpriteKeys.IndexOf(e.spriteKey ?? "");
            return index >= 0 ? Sprites[index] : null;
        }

        public TMP_FontAsset FontFor(UIElement e, TMP_FontAsset fallback)
        {
            int index = FontKeys.IndexOf(e.fontKey ?? "");
            return index >= 0 ? Fonts[index] : fallback;
        }

        public string Describe()
        {
            var data = new
            {
                sprites = Sprites.Select((s, i) => new { id = i, name = s.name, key = SpriteKeys[i],
                    width = s.rect.width, height = s.rect.height }),
                fonts = Fonts.Select((f, i) => new { id = i, name = f.name, key = FontKeys[i] })
            };
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        public static string ImportFolder(string source)
        {
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            string destination = AssetDatabase.GenerateUniqueAssetPath("Assets/UIComposerImported");
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".ttf", ".otf" };
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(p => allowed.Contains(Path.GetExtension(p))).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No PNG/JPG/TTF/OTF files found.");
            if (files.Length > 5000) throw new InvalidDataException("Too many files. Select a smaller folder (maximum 5000).");
            foreach (var file in files)
            {
                string target = Path.Combine(destination, file.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
            AssetDatabase.Refresh();
            foreach (var file in files)
            {
                string target = Path.Combine(destination, file.Substring(root.Length)).Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(target) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            return destination;
        }
    }

    public static class ComposerImages
    {
        public static Texture2D Read(Texture source, int maxSize = 1600)
        {
            float scale = Mathf.Min(1, maxSize / (float)Mathf.Max(source.width, source.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt); RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0); copy.Apply();
                return copy;
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }

        public static Texture2D ContactSheet(AssetCatalog catalog, int start, int count)
        {
            const int cell = 112, columns = 8;
            int rows = Mathf.CeilToInt(count / (float)columns);
            var sheet = new Texture2D(columns * cell, rows * cell, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(new Color32(45, 45, 45, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(pixels);
            for (int i = 0; i < count; i++)
            {
                var s = catalog.Sprites[start + i];
                var copy = Read(s.texture, Mathf.Max(s.texture.width, s.texture.height));
                try
                {
                    Rect rect = s.textureRect;
                    float scale = Mathf.Min(96f / rect.width, 88f / rect.height);
                    int w = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
                    int left = (i % columns) * cell + (cell - w) / 2;
                    int bottom = sheet.height - (i / columns + 1) * cell + 18 + (88 - h) / 2;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            Color p = copy.GetPixelBilinear((rect.x + (x + .5f) * rect.width / w) / copy.width,
                                (rect.y + (y + .5f) * rect.height / h) / copy.height);
                            Color bg = ((x / 8 + y / 8) % 2 == 0) ? Color.gray : new Color(.65f, .65f, .65f);
                            sheet.SetPixel(left + x, bottom + y, Color.Lerp(bg, p, p.a));
                        }
                    DrawNumber(sheet, start + i, (i % columns) * cell + 8, sheet.height - (i / columns + 1) * cell + 3);
                }
                finally { Object.DestroyImmediate(copy); }
            }
            sheet.Apply(); return sheet;
        }

        static void DrawNumber(Texture2D texture, int number, int x, int y)
        {
            string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
            foreach (char c in number.ToString())
            {
                string glyph = glyphs[c - '0'];
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                        if (glyph[row * 3 + col] == '1')
                            for (int yy = 0; yy < 2; yy++)
                                for (int xx = 0; xx < 2; xx++)
                                    texture.SetPixel(x + col * 2 + xx, y + (4 - row) * 2 + yy, Color.white);
                x += 8;
            }
        }
    }

    public static class UIPrefabBuilder
    {
        public static GameObject Create(UILayout layout, AssetCatalog catalog, TMP_FontAsset fallback)
        {
            layout.Validate();
            foreach (var e in layout.elements)
            {
                if (e.kind == "image" && catalog.SpriteFor(e) == null)
                    throw new InvalidDataException("Assign a sprite for " + e.name);
                if (e.kind == "text" && catalog.FontFor(e, fallback) == null)
                    throw new InvalidDataException("Assign a TMP font for " + e.name);
            }
            var root = new GameObject("ScreenshotUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(layout.width, layout.height);
                scaler.matchWidthOrHeight = .5f;
                root.GetComponent<RectTransform>().sizeDelta = scaler.referenceResolution;
                foreach (var e in layout.elements)
                {
                    var go = new GameObject(string.IsNullOrWhiteSpace(e.name) ? e.kind : e.name, typeof(RectTransform));
                    go.transform.SetParent(root.transform, false);
                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                    rt.anchoredPosition = new Vector2(e.x, -e.y); rt.sizeDelta = new Vector2(e.width, e.height);
                    Color color; ColorUtility.TryParseHtmlString(e.color, out color);
                    if (e.kind == "text")
                    {
                        var text = go.AddComponent<TextMeshProUGUI>();
                        text.font = catalog.FontFor(e, fallback);
                        text.fontSharedMaterial = text.font.material;
                        text.text = e.text;
                        text.fontSize = e.fontSize; text.enableAutoSizing = e.fitText;
                        text.fontSizeMin = e.fontSize * .4f; text.fontSizeMax = e.fontSize; text.richText = false;
                        text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
                        if (e.outlineWidth > 0)
                        {
                            var material = new Material(text.fontSharedMaterial) { name = go.name + " Outline" };
                            Color outline; ColorUtility.TryParseHtmlString(e.outlineColor, out outline);
                            material.SetFloat("_OutlineWidth", e.outlineWidth); material.SetColor("_OutlineColor", outline); material.EnableKeyword("OUTLINE_ON");
                            text.fontSharedMaterial = material; text.UpdateMeshPadding();
                        }
                        text.fontStyle = e.bold ? FontStyles.Bold : FontStyles.Normal;
                        text.alignment = e.alignment == "left" ? TextAlignmentOptions.MidlineLeft :
                            e.alignment == "right" ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline;
                        text.margin = Vector4.zero; text.color = color; text.raycastTarget = false;
                    }
                    else
                    {
                        var image = go.AddComponent<Image>(); image.color = color; image.raycastTarget = e.button;
                        if (e.button) go.AddComponent<Button>().targetGraphic = image;
                        if (e.kind == "image")
                        {
                            image.sprite = catalog.SpriteFor(e);
                            image.type = e.sliced ? Image.Type.Sliced : Image.Type.Simple;
                            image.preserveAspect = e.preserveAspect;
                        }
                    }
                }
                return root;
            }
            catch { Object.DestroyImmediate(root); throw; }
        }

        public static GameObject Save(UILayout layout, AssetCatalog catalog, TMP_FontAsset fallback, string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output must be an Assets/*.prefab path.");
            string output = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/') + "/";
            if (!output.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Output must stay inside Assets.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            if (string.IsNullOrEmpty(path)) throw new IOException("Cannot determine a unique output path.");
            var root = Create(layout, catalog, fallback);
            try
            {
                root.hideFlags = HideFlags.None;
                foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>())
                {
                    text.ForceMeshUpdate();
                    if (!EditorUtility.IsPersistent(text.fontSharedMaterial))
                    {
                        string materialPath = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(path).Replace('\\','/') + "/TextOutline.mat");
                        AssetDatabase.CreateAsset(text.fontSharedMaterial, materialPath);
                    }
                    EditorUtility.SetDirty(text.font);
                }
                AssetDatabase.SaveAssets();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (prefab == null) throw new IOException("Unity could not save prefab.");
                File.WriteAllText(Path.ChangeExtension(path, ".layout.json"), JsonConvert.SerializeObject(layout, Formatting.Indented));
                AssetDatabase.Refresh();
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
