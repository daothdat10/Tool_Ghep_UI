using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ScreenshotUIComposer
{
    public sealed class OfflinePopupResult
    {
        public UILayout Layout;
        public AssetCatalog Catalog;
        public GameObject Prefab;
        public string Folder;
        public int Reused, Generated, Texts;
        public string Warning;
    }

    // Local CPU segmentation and matching. No network requests or model downloads.
    public static class OfflinePopupComposer
    {
        sealed class Surface
        {
            public readonly int W, H;
            public readonly Color32[] Pixels;
            public Surface(Texture source, int max = 0)
            {
                var copy = ComposerImages.Read(source, max > 0 ? max : Mathf.Max(source.width, source.height));
                W = copy.width; H = copy.height;
                var bottomUp = copy.GetPixels32(); Pixels = new Color32[bottomUp.Length];
                for (int y = 0; y < H; y++) Array.Copy(bottomUp, (H - 1 - y) * W, Pixels, y * W, W);
                Object.DestroyImmediate(copy);
            }
            public Surface(int w, int h, Color32[] pixels) { W = w; H = h; Pixels = pixels; }
            public Color32 At(int x, int y) { return Pixels[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)]; }
            public Texture2D Texture()
            {
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                var data = new Color32[Pixels.Length];
                for (int y = 0; y < H; y++) Array.Copy(Pixels, y * W, data, (H - 1 - y) * W, W);
                t.SetPixels32(data); t.Apply(); return t;
            }
        }

        sealed class Region
        {
            public string Name;
            public RectInt Rect;
            public bool Button;
            public Region(string name, RectInt rect, bool button = false) { Name = name; Rect = rect; Button = button; }
        }
        sealed class TextLine
        {
            public string Text;
            public RectInt Rect;
        }

        static float Value(Color32 p) { return Mathf.Max(p.r, Mathf.Max(p.g, p.b)) / 255f; }
        static float Saturation(Color32 p)
        {
            int max = Mathf.Max(p.r, Mathf.Max(p.g, p.b)), min = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
            return max == 0 ? 0 : (max - min) / (float)max;
        }
        static float Distance(Color32 a, Color32 b)
        { return (Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b)) / 765f; }
        static RectInt Expand(RectInt r, int amount, int w, int h)
        {
            int x = Mathf.Max(0, r.x - amount), y = Mathf.Max(0, r.y - amount);
            return new RectInt(x, y, Mathf.Min(w, r.xMax + amount) - x, Mathf.Min(h, r.yMax + amount) - y);
        }
        static bool Contains(RectInt r, RectInt b) { return r.Contains(new Vector2Int(b.x+b.width/2,b.y+b.height/2)); }

        public static async Task<OfflinePopupResult> Build(Texture2D image, string spriteFolder, string fontFolder,
            CancellationToken cancellation, Action<string> progress)
        {
            if (image == null) throw new InvalidOperationException("Chọn ảnh mẫu trước.");
            if (image.width > 4096 || image.height > 4096)
                throw new InvalidOperationException("Ảnh offline hiện hỗ trợ tối đa 4096px mỗi cạnh.");
            progress("1/5 · Đang tìm popup và đọc chữ trên máy...");
            var source = new Surface(image);
            RectInt popup = FindPopup(source);
            if (popup.width < source.W * .2f || popup.height < source.H * .15f)
                throw new InvalidDataException("Không xác định được popup. Hãy cắt ảnh gần popup hơn.");
            string folder = "Assets/GeneratedUI/Popup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(folder);
            var result = new OfflinePopupResult { Folder = folder };
            var basePixels = (Color32[])source.Pixels.Clone();
            var mainMask = Silhouette(source, popup);
            var textLines = await ReadText(source, popup, folder, cancellation);
            cancellation.ThrowIfCancellationRequested();

            progress("2/5 · Đang dò icon và font nguồn...");
            string fonts = AssetCatalog.AssetFolder(fontFolder);
            if (AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { fonts }).Length == 0)
                AssetCatalog.CreateTMPFonts(fonts);
            var catalog = AssetCatalog.Scan(spriteFolder, fonts, "");
            result.Catalog = catalog;
            if (catalog.Fonts.Count == 0 && TMP_Settings.defaultFontAsset != null)
            {
                catalog.Fonts.Add(TMP_Settings.defaultFontAsset);
                catalog.FontKeys.Add(AssetCatalog.Key(TMP_Settings.defaultFontAsset));
            }
            var layout = new UILayout { width = source.W, height = source.H };
            result.Layout = layout;
            layout.elements.Add(new UIElement { name="DimOverlay", kind="panel", width=source.W, height=source.H, color="#000000A6" });
            var regions = FindRegions(source, popup);
            // Small OCR hits inside the illustration are often decorative baked lettering.
            textLines.RemoveAll(t => regions.Any(r => r.Name == "Illustration" && Contains(r.Rect,t.Rect)));
            foreach (var t in textLines) Fill(basePixels, source, Expand(t.Rect, Mathf.Max(5,t.Rect.height/4), source.W, source.H));

            progress("3/5 · Đang tách lớp và tạo phần đồ họa còn thiếu...");
            var layers = new List<UIElement>();
            var textureCache = new Dictionary<Texture, Surface>();
            try
            {
                foreach (var region in regions)
                {
                    cancellation.ThrowIfCancellationRequested();
                    await Task.Yield();
                    progress("3/5 · Tách " + region.Name + " và đối chiếu asset nguồn...");
                    var artPixels = (Color32[])source.Pixels.Clone();
                    foreach (var t in textLines.Where(t => Contains(region.Rect,t.Rect)))
                        Fill(artPixels, source, Expand(t.Rect, Mathf.Max(5,t.Rect.height/4),source.W,source.H));
                    foreach(var nested in regions.Where(r=>r!=region && Contains(region.Rect,r.Rect) && r.Rect.width<region.Rect.width*.5f))
                        Fill(artPixels,source,nested.Rect);
                    var art = Cut(new Surface(source.W,source.H,artPixels),region.Rect,region.Name=="CloseButton",false);
                    int match = Match(art,catalog,textureCache);
                    UIElement element;
                    if (match >= 0)
                    {
                        element = new UIElement { name=region.Name,kind="image",spriteId=match,spriteKey=catalog.SpriteKeys[match] };
                        result.Reused++;
                    }
                    else
                    {
                        element = SaveArt(art,region.Name,folder,catalog);
                        result.Generated++;
                    }
                    element.x=region.Rect.x; element.y=region.Rect.y;
                    element.width=region.Rect.width; element.height=region.Rect.height; element.button=region.Button;
                    layers.Add(element);
                    Fill(basePixels,source,region.Rect);
                }
            }
            finally { textureCache.Clear(); }
            // The silhouette excludes the dimmed game background; holes inside the popup are filled.
            var background = new Color32[popup.width*popup.height];
            for(int y=0;y<popup.height;y++)
                for(int x=0;x<popup.width;x++)
                {
                    var p=basePixels[(popup.y+y)*source.W+popup.x+x];
                    p.a=mainMask[y*popup.width+x]; background[y*popup.width+x]=p;
                }
            var backElement=SaveArt(new Surface(popup.width,popup.height,background),"PopupBackground",folder,catalog);
            backElement.x=popup.x;backElement.y=popup.y;backElement.width=popup.width;backElement.height=popup.height;
            layout.elements.Add(backElement);layout.elements.AddRange(layers);result.Generated++;

            progress("4/5 · Đang dựng chữ chỉnh sửa được...");
            foreach(var t in textLines)
            {
                if(catalog.Fonts.Count==0) { result.Warning="Không có TMP font; phần chữ cần kiểm tra lại."; continue; }
                Color32 color=TextColor(source,t);
                float measuredSize;
                int fontId=ChooseFont(catalog,t,source,out measuredSize);
                var font=catalog.Fonts[fontId];
                float cap=Mathf.Max(1,font.faceInfo.capLine);
                float fontSize=measuredSize;
                bool outlined = Value(color)>.7f;
                var e=new UIElement {
                    name="Text_"+t.Text.Substring(0,Math.Min(24,t.Text.Length)),kind="text",text=t.Text,
                    x=t.Rect.x-6,y=t.Rect.y-t.Rect.height*.23f,width=t.Rect.width+12,height=t.Rect.height*1.5f,
                    fontId=fontId,fontKey=catalog.FontKeys[fontId],fontSize=fontSize,
                    color="#"+ColorUtility.ToHtmlStringRGBA(color),alignment="center",fitText=false,
                    outlineWidth=outlined?.24f:0, outlineColor=outlined?OutlineColor(source,t):"#00000000"
                };
                layout.elements.Add(e);result.Texts++;
            }
            cancellation.ThrowIfCancellationRequested();
            progress("5/5 · Đang lưu prefab...");
            result.Prefab=UIPrefabBuilder.Save(layout,catalog,catalog.Fonts.FirstOrDefault(),folder+"/Popup.prefab");
            File.WriteAllText(folder+"/build-report.json",JsonConvert.SerializeObject(new{
                mode="offline", sourceWidth=source.W,sourceHeight=source.H,popup=new {popup.x,popup.y,popup.width,popup.height},
                reused=result.Reused,generated=result.Generated,texts=result.Texts,
                note="Generated sprites are extracted/restored from the reference. Hidden details are estimated, not newly illustrated.",
                warning=result.Warning
            },Formatting.Indented));
            AssetDatabase.Refresh();
            return result;
        }

        static RectInt FindPopup(Surface source)
        {
            int step=Mathf.Max(2,Mathf.CeilToInt(source.W/360f));
            int w=source.W/step,h=source.H/step;
            var mask=new bool[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                mask[y*w+x]=Value(source.At(x*step,y*step))>.49f;
            var components=Components(mask,w,h);
            if(components.Count==0)return new RectInt();
            var best=components.OrderByDescending(r=>r.width*r.height).First();
            return Expand(new RectInt(best.x*step,best.y*step,best.width*step,best.height*step),step*3,source.W,source.H);
        }

        static List<RectInt> Components(bool[] mask,int w,int h)
        {
            var visited=new bool[mask.Length];var queue=new int[mask.Length];var result=new List<RectInt>();
            for(int i=0;i<mask.Length;i++)
            {
                if(!mask[i]||visited[i])continue;
                int start=0,end=0;queue[end++]=i;visited[i]=true;
                int minX=i%w,maxX=minX,minY=i/w,maxY=minY,count=0;
                while(start<end)
                {
                    int p=queue[start++],x=p%w,y=p/w;count++;
                    minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
                    if(x>0)Visit(p-1,mask,visited,queue,ref end);
                    if(x<w-1)Visit(p+1,mask,visited,queue,ref end);
                    if(y>0)Visit(p-w,mask,visited,queue,ref end);
                    if(y<h-1)Visit(p+w,mask,visited,queue,ref end);
                }
                if(count>=12)result.Add(new RectInt(minX,minY,maxX-minX+1,maxY-minY+1));
            }
            return result;
        }
        static void Visit(int p,bool[] mask,bool[] visited,int[] queue,ref int end)
        {if(mask[p]&&!visited[p]){visited[p]=true;queue[end++]=p;}}

        static List<Region> FindRegions(Surface s,RectInt popup)
        {
            int step=Mathf.Max(2,s.W/400),w=s.W/step,h=s.H/step;
            var result=new List<Region>();
            for(int channel=0;channel<3;channel++)
            {
                var mask=new bool[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    int px=x*step,py=y*step;if(!popup.Contains(new Vector2Int(px,py)))continue;
                    Color32 p=s.At(px,py);
                    bool bright=Value(p)>.48f&&Saturation(p)>.45f;
                    mask[y*w+x]=bright&&(channel==0?p.b>p.r*1.3f&&p.g>p.r*1.2f:
                        channel==1?p.g>p.r*1.12f&&p.g>p.b*1.35f:
                        p.r>p.g*1.6f&&p.r>p.b*1.4f);
                }
                foreach(var r in Components(mask,w,h))
                {
                    var rect=Expand(new RectInt(r.x*step,r.y*step,r.width*step,r.height*step),Mathf.Max(8,s.W/85),s.W,s.H);
                    if(rect.width*rect.height<popup.width*popup.height*.004f)continue;
                    float cy=(rect.y+rect.height*.5f-popup.y)/popup.height;
                    if(channel==0&&rect.width>popup.width*.6f&&rect.height<popup.height*.3f&&cy<.35f)
                        result.Add(new Region("Header",rect));
                    else if(channel==1&&rect.width>popup.width*.3f&&cy>.65f)
                        result.Add(new Region("PurchaseButton",rect,true));
                    else if(channel==2&&rect.width<popup.width*.22f&&rect.height<popup.height*.22f&&cy<.2f)
                        result.Add(new Region("CloseButton",rect,true));
                }
            }
            // Main colored illustration inside the card, excluding large structural pieces.
            int minX=s.W,minY=s.H,maxX=0,maxY=0;
            var artMask=new bool[w*h];
            for(int y=popup.y+popup.height/4;y<popup.y+popup.height*3/4;y+=step)
                for(int x=popup.x+popup.width/5;x<popup.xMax-popup.width/5;x+=step)
                {
                    if(result.Any(r=>r.Rect.Contains(new Vector2Int(x,y))))continue;
                    var p=s.At(x,y);
                    if(Value(p)<.5f||Saturation(p)<.62f)continue;
                    // Warm beige panels are not illustration pixels.
                    if(p.r>p.g&&p.g>p.b&&p.r-p.b<155)continue;
                    artMask[(y/step)*w+x/step]=true;
                }
            foreach(var r in Components(artMask,w,h))
            {
                if(r.width*r.height*step*step<popup.width*popup.height*.006f || r.height*step<popup.height*.04f)continue;
                minX=Mathf.Min(minX,r.x*step);maxX=Mathf.Max(maxX,r.xMax*step);
                minY=Mathf.Min(minY,r.y*step);maxY=Mathf.Max(maxY,r.yMax*step);
            }
            if(maxX-minX>popup.width*.1f&&maxY-minY>popup.height*.06f)
                result.Add(new Region("Illustration",Expand(new RectInt(minX,minY,maxX-minX+step,maxY-minY+step),s.W/35,s.W,s.H)));
            foreach(var button in result.Where(r=>r.Name=="PurchaseButton").ToArray())
            {
                var gold=new bool[w*h];
                for(int y=button.Rect.y/step;y<Mathf.Min(h,button.Rect.yMax/step);y++)
                    for(int x=button.Rect.x/step;x<Mathf.Min(w,button.Rect.xMax/step);x++)
                    {
                        var p=s.At(x*step,y*step);
                        gold[y*w+x]=p.r>150&&p.r>p.g*.9f&&p.g>p.b*1.3f&&Saturation(p)>.48f;
                    }
                foreach(var r in Components(gold,w,h))
                {
                    if(r.width*step<button.Rect.height*.2f||r.height*step<button.Rect.height*.2f)continue;
                    float ratio=r.width/(float)r.height;if(ratio<.65f||ratio>1.5f)continue;
                    result.Add(new Region("CoinIcon",Expand(new RectInt(r.x*step,r.y*step,r.width*step,r.height*step),step*2,s.W,s.H)));
                    break;
                }
            }
            return result;
        }

        static byte[] Silhouette(Surface source,RectInt r)
        {
            int w=r.width,h=r.height;var mask=new bool[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)mask[y*w+x]=Value(source.At(r.x+x,r.y+y))>.38f;
            // Flood from edges through dark pixels, preserving enclosed holes (text, icon shadows).
            var exterior=new bool[w*h];var queue=new int[w*h];int end=0,start=0;
            var outside=Invert(mask);
            for(int x=0;x<w;x++){Visit(x,outside,exterior,queue,ref end);Visit((h-1)*w+x,outside,exterior,queue,ref end);}
            for(int y=0;y<h;y++){Visit(y*w,outside,exterior,queue,ref end);Visit(y*w+w-1,outside,exterior,queue,ref end);}
            while(start<end){int p=queue[start++],x=p%w,y=p/w;
                if(x>0)Visit(p-1,outside,exterior,queue,ref end);if(x<w-1)Visit(p+1,outside,exterior,queue,ref end);
                if(y>0)Visit(p-w,outside,exterior,queue,ref end);if(y<h-1)Visit(p+w,outside,exterior,queue,ref end);}
            var alpha=new byte[w*h];for(int i=0;i<alpha.Length;i++)alpha[i]=exterior[i]?(byte)0:(byte)255;
            return alpha;
        }
        static bool[] Invert(bool[] data){var r=new bool[data.Length];for(int i=0;i<data.Length;i++)r[i]=!data[i];return r;}

        static Surface Cut(Surface source,RectInt rect,bool darkExterior,bool solid)
        {
            var pixels=new Color32[rect.width*rect.height];
            var background=new[]{source.At(rect.x,rect.y),source.At(rect.xMax-1,rect.y),source.At(rect.x,rect.yMax-1),source.At(rect.xMax-1,rect.yMax-1)};
            for(int y=0;y<rect.height;y++)for(int x=0;x<rect.width;x++)
            {
                var p=source.At(rect.x+x,rect.y+y);
                float difference=background.Min(c=>Distance(c,p));
                float alpha=solid?1:darkExterior?Mathf.InverseLerp(.17f,.38f,Value(p)):Mathf.InverseLerp(.025f,.12f,difference);
                p.a=(byte)Mathf.RoundToInt(alpha*255);pixels[y*rect.width+x]=p;
            }
            return new Surface(rect.width,rect.height,pixels);
        }

        static void Fill(Color32[] target,Surface source,RectInt rect)
        {
            // Restore a smooth local background using left/right border samples for each scanline.
            for(int y=rect.y;y<rect.yMax;y++)
            {
                Color32 left=source.At(rect.x-2,y),right=source.At(rect.xMax+2,y);
                if (Distance(left,right)>.22f) { if(Saturation(left)<Saturation(right))right=left;else left=right; }
                Color a=left,b=right;
                for(int x=rect.x;x<rect.xMax;x++)target[y*source.W+x]=Color.Lerp(a,b,(x-rect.x)/(float)Mathf.Max(1,rect.width-1));
            }
        }

        static UIElement SaveArt(Surface image,string name,string folder,AssetCatalog catalog)
        {
            string path=folder+"/"+name+".png";var texture=image.Texture();
            try{File.WriteAllBytes(path,texture.EncodeToPNG());}finally{Object.DestroyImmediate(texture);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.maxTextureSize=4096;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            catalog.Sprites.Add(sprite);catalog.SpriteKeys.Add(AssetCatalog.Key(sprite));
            return new UIElement{name=name,kind="image",spriteId=catalog.Sprites.Count-1,spriteKey=AssetCatalog.Key(sprite)};
        }

        static int Match(Surface crop,AssetCatalog catalog,Dictionary<Texture,Surface> cache)
        {
            int winner=-1;float best=float.MaxValue;
            for(int i=0;i<catalog.Sprites.Count;i++)
            {
                var sprite=catalog.Sprites[i];
                // Generated output from prior builds is excluded from source matching.
                if(AssetDatabase.GetAssetPath(sprite).StartsWith("Assets/GeneratedUI/"))continue;
                Rect rect;
                try{rect=sprite.textureRect;}catch{continue;}
                if(rect.width<8||rect.height<8)continue;
                float ratio=(rect.width/rect.height)/(crop.W/(float)crop.H);
                if(ratio<.72f||ratio>1.4f)continue;
                Surface source;
                if(!cache.TryGetValue(sprite.texture,out source)){source=new Surface(sprite.texture,256);cache.Add(sprite.texture,source);}
                float sx=source.W/(float)sprite.texture.width,sy=source.H/(float)sprite.texture.height;
                rect=new Rect(rect.x*sx,rect.y*sy,rect.width*sx,rect.height*sy);
                float error=0;int samples=0,intersection=0,union=0;
                for(int y=0;y<20;y++)for(int x=0;x<20;x++)
                {
                    var a=crop.At((int)((x+.5f)*crop.W/20),(int)((y+.5f)*crop.H/20));
                    var b=source.At((int)(rect.x+(x+.5f)*rect.width/20),(int)(source.H-rect.y-rect.height+(y+.5f)*rect.height/20));
                    if(a.a>128||b.a>128)union++;
                    if(a.a>128&&b.a>128)intersection++;
                    error+=Mathf.Abs(a.a-b.a)/255f*.35f;
                    if(a.a>128&&b.a>128)error+=Distance(a,b)*.65f;
                    samples++;
                }
                if(union==0||intersection/(float)union<.65f)continue;
                error/=samples;if(error<best){best=error;winner=i;}
            }
            return best<.095f?winner:-1;
        }

        static async Task<List<TextLine>> ReadText(Surface image,RectInt popup,string folder,CancellationToken cancellation)
        {
            var lines=new List<TextLine>();
            if(Application.platform!=RuntimePlatform.WindowsEditor)return lines;
            string temp=Path.Combine(Path.GetTempPath(),"UIComposer-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string input=Path.Combine(temp,"input.png"),contrast=Path.Combine(temp,"contrast.png"),output=Path.Combine(temp,"ocr.json");
            var t=image.Texture();try{File.WriteAllBytes(input,t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}
            var filtered=(Color32[])image.Pixels.Clone();
            for(int i=0;i<filtered.Length;i++){byte c=filtered[i].r>210?(byte)255:(byte)0;filtered[i]=new Color32(c,c,c,255);}
            t=new Surface(image.W,image.H,filtered).Texture();
            try{File.WriteAllBytes(contrast,t.EncodeToPNG());}finally{Object.DestroyImmediate(t);}
            string script=Path.Combine(temp,"ocr.ps1");
            File.Copy("Assets/Tools/ScreenshotUIComposer/Editor/UIComposerOcr.ps1.txt",script);
            using(var process=new Process())
            {
                process.StartInfo=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -InputPath \""+input+"\" -ContrastPath \""+contrast+"\" -OutputPath \""+output+"\"")
                    {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                process.Start();var started=DateTime.UtcNow;
                try{
                    while(!process.HasExited){cancellation.ThrowIfCancellationRequested();if((DateTime.UtcNow-started).TotalSeconds>45)throw new TimeoutException("Windows OCR timeout.");await Task.Delay(80);}
                }finally{if(!process.HasExited)process.Kill();}
            }
            if(!File.Exists(output))throw new IOException("Windows OCR không trả kết quả. Kiểm tra gói ngôn ngữ Windows.");
            var json=JObject.Parse(File.ReadAllText(output));
            if(!string.IsNullOrEmpty((string)json["error"]))throw new IOException("Windows OCR: "+(string)json["error"]);
            File.Copy(output,folder+"/ocr.json",true);
            foreach(var line in (JArray)json["lines"])
            {
                int pass=(int)line["pass"];
                var words=((JArray)line["words"]).Where(w=>pass==0||System.Text.RegularExpressions.Regex.IsMatch((string)w["text"],@"^\d{2,}$")).ToArray();
                if(words.Length==0)continue;
                int x=(int)words.Min(w=>(float)w["x"]),y=(int)words.Min(w=>(float)w["y"]);
                int xmax=(int)words.Max(w=>(float)w["x"]+(float)w["width"]),ymax=(int)words.Max(w=>(float)w["y"]+(float)w["height"]);
                var rect=new RectInt(x,y,xmax-x,ymax-y);
                if(!Contains(popup,rect)||rect.height<12)continue;
                if(lines.Any(l=>Mathf.Abs(l.Rect.y-rect.y)<Mathf.Min(l.Rect.height,rect.height)*.6f))continue;
                lines.Add(new TextLine{Text=string.Join(" ",words.Select(w=>(string)w["text"]).ToArray()),Rect=rect});
            }
            return lines;
        }

        static Color32 TextColor(Surface source,TextLine line)
        {
            var colors=new Dictionary<int,int>();
            var rect=line.Rect;
            for(int y=rect.y;y<rect.yMax;y+=2)for(int x=rect.x;x<rect.xMax;x+=2)
            {var p=source.At(x,y);int key=(p.r/16<<8)|(p.g/16<<4)|p.b/16;int count;colors.TryGetValue(key,out count);colors[key]=count+1;}
            // Discard the dominant background bin; choose a frequent contrasting foreground color.
            var ordered=colors.OrderByDescending(p=>p.Value).Take(12).ToArray();
            Color32 bg=source.At(rect.x-10,rect.y+rect.height/2);
            var winner=ordered.OrderByDescending(p=>{
                var c=new Color32((byte)(((p.Key>>8)&15)*16+8),(byte)(((p.Key>>4)&15)*16+8),(byte)((p.Key&15)*16+8),255);
                return p.Value*Mathf.Pow(Distance(c,bg),2);
            }).First();
            return new Color32((byte)(((winner.Key>>8)&15)*16+8),(byte)(((winner.Key>>4)&15)*16+8),(byte)((winner.Key&15)*16+8),255);
        }
        static string OutlineColor(Surface source,TextLine line)
        {
            Color32 dark=new Color32(40,80,140,255);float best=1;
            for(int y=line.Rect.y;y<line.Rect.yMax;y+=3)for(int x=line.Rect.x;x<line.Rect.xMax;x+=3)
            {var c=source.At(x,y);if(Value(c)<best){best=Value(c);dark=c;}}
            return "#"+ColorUtility.ToHtmlStringRGBA(dark);
        }
        static int ChooseFont(AssetCatalog catalog,TextLine line,Surface reference,out float fontSize)
        {
            var foreground=TextColor(reference,line);
            var background=reference.At(line.Rect.x-10,line.Rect.y+line.Rect.height/2);
            RectInt targetBounds;
            var target=OfflineGlyphMask.Build(reference.Pixels,reference.W,line.Rect,foreground,background,out targetBounds);
            var preview=new PreviewRenderUtility();
            var root=new GameObject("FontComparison",typeof(RectTransform),typeof(Canvas));
            var rt=root.GetComponent<RectTransform>();rt.sizeDelta=new Vector2(512,160);
            root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var child=new GameObject("Glyphs",typeof(RectTransform));child.transform.SetParent(root.transform,false);
            child.GetComponent<RectTransform>().sizeDelta=new Vector2(500,150);
            var text=child.AddComponent<TextMeshProUGUI>();text.text=line.Text;text.color=Color.white;
            text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Overflow;
            text.alignment=TextAlignmentOptions.Center;text.enableAutoSizing=false;
            preview.AddSingleGO(root);preview.camera.cameraType=CameraType.Game;
            root.GetComponent<Canvas>().worldCamera=preview.camera;
            preview.camera.orthographic=true;preview.camera.orthographicSize=80;
            preview.camera.transform.position=new Vector3(0,0,-10);preview.camera.transform.rotation=Quaternion.identity;
            preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=100;
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=Color.black;
            int best=0;float error=float.MaxValue;float chosenSize=line.Rect.height*1.4f;
            try
            {
                for(int i=0;i<catalog.Fonts.Count;i++)
                {
                    text.font=catalog.Fonts[i];text.fontSharedMaterial=text.font.material;text.fontSize=90;
                    float natural=text.GetPreferredValues(line.Text).x;
                    float sampleSize=90*Mathf.Min(1,470/Mathf.Max(1,natural));
                    text.fontSize=sampleSize;
                    Canvas.ForceUpdateCanvases();text.ForceMeshUpdate();
                    text.canvasRenderer.SetMaterial(text.fontSharedMaterial,0);text.canvasRenderer.SetTexture(text.font.atlasTexture);
                    preview.BeginStaticPreview(new Rect(0,0,512,160));preview.camera.aspect=512f/160;
                    preview.camera.Render();var image=preview.EndStaticPreview();
                    Surface candidate;
                    try{candidate=new Surface(image);}finally{Object.DestroyImmediate(image);}
                    RectInt bounds;var mask=OfflineGlyphMask.Build(candidate.Pixels,candidate.W,new RectInt(0,0,512,160),new Color32(255,255,255,255),new Color32(0,0,0,255),out bounds);
                    if(bounds.width<2||bounds.height<2)continue;
                    float score=0;for(int p=0;p<mask.Length;p++)score+=Mathf.Abs(mask[p]-target[p]);
                    score/=mask.Length;
                    float ratio=(bounds.width/(float)bounds.height)/(Mathf.Max(1,targetBounds.width)/(float)Mathf.Max(1,targetBounds.height));
                    score+=Mathf.Abs(Mathf.Log(ratio))*.12f;
                    if(score<error){error=score;best=i;chosenSize=sampleSize*targetBounds.height/bounds.height;}
                }
            }
            finally{preview.Cleanup();}
            fontSize=chosenSize;return best;
        }
    }
}
