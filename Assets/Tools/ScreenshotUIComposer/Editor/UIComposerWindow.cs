using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ScreenshotUIComposer
{
    public sealed partial class UIComposerWindow : EditorWindow
    {
        [SerializeField] Texture2D reference;
        [SerializeField] string spriteFolder = "Assets/Sprite", fontFolder = "Assets/Font", spriteFilter = "";
        [SerializeField] TMP_FontAsset fallbackFont;
        [SerializeField] UILayout layout = new UILayout();
        [SerializeField] GameObject builtPrefab;
        [SerializeField] bool advanced;
        [SerializeField] string json = "", status = "Chon anh mau va thu muc asset de bat dau.";
        [SerializeField] int selected = -1;
        [SerializeField] bool settings, jsonFoldout;
        [SerializeField] float overlay = .5f;

        AssetCatalog catalog;
        CancellationTokenSource cancellation;
        PreviewRenderUtility preview;
        GameObject previewRoot;
        Texture2D externalReference;
        Vector2 scroll, elementScroll;
        bool previewDirty = true;
        MessageType statusType = MessageType.Info;

        [MenuItem("Tools/UI Composer/From Screenshot")]
        public static void Open()
        {
            var window = GetWindow<UIComposerWindow>("UI Composer");
            window.minSize = new Vector2(920, 680);
        }

        void OnEnable()
        {
            spriteFolder = EditorPrefs.GetString(Application.dataPath + ".UIComposer.Icons",spriteFolder);
            fontFolder = EditorPrefs.GetString(Application.dataPath + ".UIComposer.Fonts",fontFolder);
            LoadRouterPreferences();
            previewDirty = true;
        }

        void OnDisable()
        {
            if (cancellation != null) cancellation.Cancel();
            ClearPreview();
            if (externalReference != null) DestroyImmediate(externalReference);
        }

        void ClearPreview()
        {
            if (previewRoot != null)
                foreach (var t in previewRoot.GetComponentsInChildren<TextMeshProUGUI>())
                    if (t.fontSharedMaterial != null && !EditorUtility.IsPersistent(t.fontSharedMaterial))
                        DestroyImmediate(t.fontSharedMaterial);
            if (preview != null) { preview.Cleanup(); preview = null; }
            else if (previewRoot != null) DestroyImmediate(previewRoot);
            previewRoot = null;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("Ảnh → Popup Prefab", EditorStyles.boldLabel);
            GUILayout.Label("Kéo ảnh · Dựng tự động · Mở prefab", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(status, statusType);
            if (cancellation != null)
            {
                EditorGUILayout.HelpBox("Dang phan tich anh... ban co the huy yeu cau.", MessageType.Info);
                if (GUILayout.Button("Huy phan tich")) cancellation.Cancel();
            }
            using (new EditorGUI.DisabledScope(cancellation != null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
                    {
                        scroll = EditorGUILayout.BeginScrollView(scroll);
                        DrawInputs();
                        DrawActions();
                        if (advanced) { DrawElements(); DrawJson(); }
                        EditorGUILayout.EndScrollView();
                    }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawPreview();
                        if (advanced) DrawElementEditor();
                    }
                }
            }
        }

        void DrawInputs()
        {
            GUILayout.Label("Ảnh popup", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            reference = (Texture2D)EditorGUILayout.ObjectField("Ảnh mẫu", reference, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck()) previewDirty = true;
            Rect drop = GUILayoutUtility.GetRect(100, 90, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Kéo ảnh PNG / JPG vào đây\nTool tự tìm icon, đọc chữ và bổ sung asset thiếu");
            var ev = Event.current;
            if (drop.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (ev.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (DragAndDrop.objectReferences.OfType<Texture2D>().Any())
                        reference = DragAndDrop.objectReferences.OfType<Texture2D>().First();
                    else if (DragAndDrop.paths.Length > 0) Guard(() => LoadReference(DragAndDrop.paths[0]));
                    previewDirty = true; ev.Use();
                }
            }
            if (GUILayout.Button("Chọn ảnh từ máy"))
            {
                string path = EditorUtility.OpenFilePanel("Ảnh popup", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(path)) Guard(() => LoadReference(path));
            }
            settings = EditorGUILayout.Foldout(settings, "Nguồn icon / font · chỉ chọn một lần", true);
            if (settings)
            {
                FolderField("Icon", ref spriteFolder); FolderField("Font", ref fontFolder);
                if (GUILayout.Button("Ghi nhớ nguồn asset"))
                {
                    Guard(() => {
                        spriteFolder = AssetCatalog.AssetFolder(spriteFolder);
                        fontFolder = AssetCatalog.AssetFolder(fontFolder);
                        EditorPrefs.SetString(Application.dataPath + ".UIComposer.Icons",spriteFolder);
                        EditorPrefs.SetString(Application.dataPath + ".UIComposer.Fonts",fontFolder);
                        SetStatus("Đã ghi nhớ. Lần sau chỉ cần kéo ảnh và dựng popup.",MessageType.Info);
                    });
                }
                if (GUILayout.Button("Import thư mục ngoài project"))
                {
                    string path = EditorUtility.OpenFolderPanel("Icon / font", "", "");
                    if (!string.IsNullOrEmpty(path)) Guard(() => {
                        spriteFolder = fontFolder = AssetCatalog.ImportFolder(path);
                        EditorPrefs.SetString(Application.dataPath + ".UIComposer.Icons",spriteFolder);
                        EditorPrefs.SetString(Application.dataPath + ".UIComposer.Fonts",fontFolder);
                    });
                }
            }
            EditorGUILayout.HelpBox("Offline: tách asset từ ảnh. Bật 9Router bên dưới để AI dựng thêm text và đồ họa thiếu.",MessageType.Info);
            DrawRouterSettings();
        }

        static void FolderField(string label, ref string folder)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                folder = EditorGUILayout.TextField(label, folder);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string path = EditorUtility.OpenFolderPanel(label, Application.dataPath, "");
                    if (!string.IsNullOrEmpty(path)) folder = path;
                }
            }
        }

        void LoadReference(string path)
        {
            if (new FileInfo(path).Length > 30 * 1024 * 1024) throw new IOException("Image must be smaller than 30 MB.");
            var image = new Texture2D(2, 2);
            if (!image.LoadImage(File.ReadAllBytes(path))) { DestroyImmediate(image); throw new IOException("Cannot decode image."); }
            if (externalReference != null) DestroyImmediate(externalReference);
            externalReference = image; reference = image; previewDirty = true;
            SetStatus("Da nap anh " + image.width + " x " + image.height, MessageType.Info);
        }

        void Scan()
        {
            var result = AssetCatalog.Scan(spriteFolder, fontFolder, spriteFilter);
            catalog = result;
            if (fallbackFont == null) fallbackFont = catalog.Fonts.FirstOrDefault() ?? TMP_Settings.defaultFontAsset;
            // Stable keys prevent stale numeric IDs from binding to different assets after a rescan.
            previewDirty = true;
            SetStatus("Tim thay " + catalog.Sprites.Count + " sprites va " + catalog.Fonts.Count + " TMP fonts.", MessageType.Info);
        }

        void EnsureInputs()
        {
            if (reference == null) throw new InvalidOperationException("Chon anh mau truoc.");
            if (catalog == null) Scan();
        }

        void DrawActions()
        {
            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(reference == null))
                if (GUILayout.Button(routerEnabled ? "Dựng popup · 9Router" : "Dựng popup · Offline", GUILayout.Height(42))) BuildOffline();
            if (builtPrefab != null)
            {
                if (GUILayout.Button("Mở prefab vừa dựng")) AssetDatabase.OpenAsset(builtPrefab);
                if (GUILayout.Button("Chọn thư mục kết quả")) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<DefaultAsset>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(builtPrefab))));
            }
            advanced = EditorGUILayout.Foldout(advanced, "Chỉnh thêm (tùy chọn)", true);
            if (advanced && GUILayout.Button("Khớp ảnh Shuffle mẫu (giữ đồ họa gốc)")) Guard(() => {
                if (reference == null) throw new InvalidOperationException("Chọn đúng ảnh Shuffle mẫu trước.");
                builtPrefab = ShuffleReferenceBuilder.Build(reference);
                EditorGUIUtility.PingObject(builtPrefab);
                AssetDatabase.OpenAsset(builtPrefab);
                SetStatus("Đã dựng bản Shuffle theo ảnh. Chữ trang trí giữ bằng sprite; nhóm EditableTextAlternatives tắt mặc định.",MessageType.Info);
            });
            if (advanced && GUILayout.Button("Lưu prefab đã chỉnh")) Guard(() => {
                if (catalog == null) Scan();
                string path = EditorUtility.SaveFilePanelInProject("Lưu prefab", "Popup", "prefab", "Chọn nơi lưu");
                if (!string.IsNullOrEmpty(path)) builtPrefab = UIPrefabBuilder.Save(layout,catalog,fallbackFont,path);
            });
        }

        async void BuildOffline()
        {
            if (cancellation != null) return;
            cancellation = new CancellationTokenSource();
            try
            {
                EditorPrefs.SetString(Application.dataPath + ".UIComposer.Icons",spriteFolder);
                EditorPrefs.SetString(Application.dataPath + ".UIComposer.Fonts",fontFolder);
                if (routerEnabled)
                {
                    SetStatus("Đang kiểm tra URL và loại model 9Router...",MessageType.Info);
                    await NineRouterComposer.VerifySettings(router,routerKey,cancellation.Token);
                }
                var result = await OfflinePopupComposer.Build(reference,spriteFolder,fontFolder,cancellation.Token,
                    message => { if (this != null) SetStatus(message,MessageType.Info); });
                if (this == null) return;
                // Publish the offline result first so API failure/cancellation leaves a usable prefab.
                layout = result.Layout; catalog = result.Catalog; builtPrefab = result.Prefab;
                fallbackFont = catalog.Fonts.FirstOrDefault(); previewDirty = true;
                if (routerEnabled)
                {
                    SaveRouterPreferences();
                    result = await NineRouterComposer.Complete(reference,result,router,routerKey,cancellation.Token,
                        message => { if(this != null) SetStatus(message,MessageType.Info); });
                    if (this == null) return;
                }
                layout = result.Layout; catalog = result.Catalog; builtPrefab = result.Prefab;
                fallbackFont = catalog.Fonts.FirstOrDefault(); selected = -1; overlay = 0; previewDirty = true;
                json = JsonConvert.SerializeObject(layout,Formatting.Indented);
                SetStatus("Đã dựng popup: " + result.Reused + " icon nguồn, " + result.Generated +
                    " asset bổ sung, " + result.Texts + " lớp chữ. Lưu tại " + result.Folder,MessageType.Info);
                EditorGUIUtility.PingObject(builtPrefab);
            }
            catch (OperationCanceledException) { if(this != null) SetStatus("Đã hủy dựng popup.",MessageType.Info); }
            catch (Exception ex) { if(this != null) SetStatus(ex.Message,MessageType.Error); }
            finally { cancellation?.Dispose(); cancellation = null; if(this != null) Repaint(); }
        }

        void DrawElements()
        {
            EditorGUILayout.Space();
            GUILayout.Label("Layers (duoi danh sach ve tren)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Image")) Add("image");
                if (GUILayout.Button("+ Text")) Add("text");
                if (GUILayout.Button("+ Panel")) Add("panel");
            }
            elementScroll = EditorGUILayout.BeginScrollView(elementScroll, GUILayout.Height(160));
            for (int i = 0; i < layout.elements.Count; i++)
                if (GUILayout.Toggle(selected == i, i + "  " + layout.elements[i].name + " [" + layout.elements[i].kind + "]", "Button"))
                    selected = i;
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(selected < 0 || selected >= layout.elements.Count))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Len") && selected > 0)
                { var e = layout.elements[selected]; layout.elements.RemoveAt(selected); layout.elements.Insert(--selected, e); previewDirty = true; }
                if (GUILayout.Button("Xuong") && selected >= 0 && selected < layout.elements.Count - 1)
                { var e = layout.elements[selected]; layout.elements.RemoveAt(selected); layout.elements.Insert(++selected, e); previewDirty = true; }
                if (GUILayout.Button("Xoa") && selected >= 0)
                { layout.elements.RemoveAt(selected); selected = -1; previewDirty = true; }
            }
        }

        void Add(string kind)
        {
            if (reference != null && layout.elements.Count == 0)
            { layout.width = reference.width; layout.height = reference.height; }
            layout.elements.Add(new UIElement { name = kind + "_" + layout.elements.Count, kind = kind, text = kind == "text" ? "Text" : "" });
            selected = layout.elements.Count - 1; previewDirty = true;
        }

        void DrawElementEditor()
        {
            if (selected < 0 || selected >= layout.elements.Count) return;
            var e = layout.elements[selected];
            GUILayout.Label("Chỉnh phần tử", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            e.name = EditorGUILayout.TextField("Ten", e.name);
            using (new EditorGUILayout.HorizontalScope())
            {
                e.x = EditorGUILayout.FloatField("X", e.x); e.y = EditorGUILayout.FloatField("Y", e.y);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                e.width = EditorGUILayout.FloatField("Width", e.width); e.height = EditorGUILayout.FloatField("Height", e.height);
            }
            Color color; ColorUtility.TryParseHtmlString(e.color, out color);
            e.color = "#" + ColorUtility.ToHtmlStringRGBA(EditorGUILayout.ColorField("Color", color));
            if (e.kind == "image" && catalog != null)
            {
                Sprite old = catalog.SpriteFor(e);
                var sprite = (Sprite)EditorGUILayout.ObjectField("Sprite", old, typeof(Sprite), false);
                if (sprite != old)
                {
                    if (sprite != null && !catalog.Sprites.Contains(sprite))
                    { catalog.Sprites.Add(sprite); catalog.SpriteKeys.Add(AssetCatalog.Key(sprite)); }
                    e.spriteKey = sprite == null ? "" : AssetCatalog.Key(sprite); e.spriteId = catalog.Sprites.IndexOf(sprite);
                }
                e.sliced = EditorGUILayout.Toggle("9-slice", e.sliced);
                e.preserveAspect = EditorGUILayout.Toggle("Preserve aspect", e.preserveAspect);
            }
            if (e.kind == "text")
            {
                e.text = EditorGUILayout.TextField("Text", e.text);
                e.fontSize = EditorGUILayout.FloatField("Font size", e.fontSize);
                e.bold = EditorGUILayout.Toggle("Bold", e.bold);
                string[] alignments = { "left", "center", "right" };
                e.alignment = alignments[EditorGUILayout.Popup("Alignment", Mathf.Max(0, Array.IndexOf(alignments, e.alignment)), alignments)];
                var old = catalog?.FontFor(e, fallbackFont);
                var font = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP font", old, typeof(TMP_FontAsset), false);
                if (font != old && catalog != null)
                {
                    if (font != null && !catalog.Fonts.Contains(font))
                    { catalog.Fonts.Add(font); catalog.FontKeys.Add(AssetCatalog.Key(font)); }
                    e.fontKey = font == null ? "" : AssetCatalog.Key(font); e.fontId = catalog.Fonts.IndexOf(font);
                }
            }
            if (EditorGUI.EndChangeCheck()) previewDirty = true;
        }

        void DrawJson()
        {
            jsonFoldout = EditorGUILayout.Foldout(jsonFoldout, "Layout JSON / Luu & mo bo cuc", true);
            if (!jsonFoldout) return;
            json = EditorGUILayout.TextArea(json, GUILayout.Height(100));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ap dung JSON")) Guard(() => ApplyJson(json));
                if (GUILayout.Button("Mo JSON"))
                {
                    string path = EditorUtility.OpenFilePanel("Layout JSON", "", "json");
                    if (!string.IsNullOrEmpty(path)) Guard(() => { json = File.ReadAllText(path); ApplyJson(json); });
                }
                if (GUILayout.Button("Luu JSON")) Guard(() => {
                    layout.Validate(); string path = EditorUtility.SaveFilePanel("Layout JSON", "", "layout.json", "json");
                    if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, JsonConvert.SerializeObject(layout, Formatting.Indented));
                });
            }
        }

        void ApplyJson(string value)
        {
            var result = UILayout.Parse(value);
            if (catalog == null) Scan();
            catalog.Bind(result); layout = result; selected = -1; previewDirty = true;
            SetStatus("Da nhap " + layout.elements.Count + " phan tu.", MessageType.Info);
        }

        void DrawPreview()
        {
            GUILayout.Label("Ảnh mẫu / Prefab preview", EditorStyles.boldLabel);
            overlay = EditorGUILayout.Slider("Chồng ảnh mẫu", overlay, 0, 1);
            Rect area = GUILayoutUtility.GetRect(100, 330, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(.12f, .12f, .12f));
            int w = layout.elements.Count > 0 ? layout.width : reference != null ? reference.width : 1080;
            int h = layout.elements.Count > 0 ? layout.height : reference != null ? reference.height : 1920;
            float scale = Mathf.Min(area.width / Mathf.Max(1, w), area.height / Mathf.Max(1, h));
            Rect rect = new Rect(area.center.x - w * scale / 2, area.center.y - h * scale / 2, w * scale, h * scale);
            if (Event.current.type != EventType.Repaint) return;
            if (previewDirty)
            {
                ClearPreview(); previewDirty = false;
                if (catalog != null && layout.elements.Count > 0)
                {
                    try
                    {
                        previewRoot = UIPrefabBuilder.Create(layout, catalog, fallbackFont);
                        previewRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                        previewRoot.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false;
                        var rt = previewRoot.GetComponent<RectTransform>();
                        rt.sizeDelta = new Vector2(w, h); rt.position = Vector3.zero; rt.localScale = Vector3.one;
                        preview = new PreviewRenderUtility(); preview.AddSingleGO(previewRoot);
                        preview.camera.cameraType = CameraType.Game;
                        previewRoot.GetComponent<Canvas>().worldCamera = preview.camera;
                        preview.camera.orthographic = true; preview.camera.orthographicSize = h / 2f;
                        preview.camera.transform.position = new Vector3(0, 0, -10);
                        preview.camera.transform.rotation = Quaternion.identity;
                        preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 100;
                        preview.camera.clearFlags = CameraClearFlags.SolidColor;
                        preview.camera.backgroundColor = new Color(.15f, .15f, .15f);
                        Canvas.ForceUpdateCanvases();
                        foreach (var text in previewRoot.GetComponentsInChildren<TextMeshProUGUI>())
                        {
                            text.ForceMeshUpdate();
                            text.canvasRenderer.SetMaterial(text.fontSharedMaterial, 0);
                            text.canvasRenderer.SetTexture(text.font.atlasTexture);
                        }
                    }
                    catch (Exception ex) { ClearPreview(); SetStatus("Preview: " + ex.Message, MessageType.Warning); }
                }
            }
            if (preview != null)
            {
                preview.BeginPreview(rect, GUIStyle.none);
                preview.camera.aspect = w / (float)h;
                preview.camera.Render();
                GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill);
            }
            if (reference != null)
            {
                Color old = GUI.color;
                GUI.color = new Color(1, 1, 1, preview == null ? 1 : overlay);
                GUI.DrawTexture(rect, reference, ScaleMode.StretchToFill); GUI.color = old;
            }
            if (selected >= 0 && selected < layout.elements.Count)
            {
                var e = layout.elements[selected];
                Rect box = new Rect(rect.x + e.x * scale, rect.y + e.y * scale, e.width * scale, e.height * scale);
                Handles.BeginGUI(); Handles.color = Color.cyan;
                Handles.DrawAAPolyLine(2, new Vector3(box.xMin, box.yMin), new Vector3(box.xMax, box.yMin),
                    new Vector3(box.xMax, box.yMax), new Vector3(box.xMin, box.yMax), new Vector3(box.xMin, box.yMin));
                Handles.EndGUI();
            }
        }

        void Guard(Action action) { try { action(); } catch (Exception ex) { SetStatus(ex.Message, MessageType.Error); } }
        void SetStatus(string message, MessageType type) { status = message; statusType = type; Repaint(); }
    }
}
