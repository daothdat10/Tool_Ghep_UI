using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ScreenshotUIComposer
{
    // A deliberate reference-specific reconstruction, never used as a general popup detector.
    public static class ShuffleReferenceBuilder
    {
        public static GameObject Build(Texture2D reference)
        {
            if(reference==null)throw new InvalidOperationException("Chọn ảnh Shuffle trước.");
            string folder="Assets/GeneratedUI/ShuffleMatched_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(folder);
            var copy=ComposerImages.Read(reference,Mathf.Max(reference.width,reference.height));
            var catalog=AssetCatalog.Scan("Assets/Sprite","Assets/Font","");
            var layout=new UILayout{width=reference.width,height=reference.height};
            float sx=reference.width/947f,sy=reference.height/2048f;
            Action<UIElement,float,float,float,float> add=(e,x,y,w,h)=>{
                e.x=x*sx;e.y=y*sy;e.width=w*sx;e.height=h*sy;layout.elements.Add(e);
            };
            add(new UIElement{name="DimOverlay",kind="panel",color="#000000B0"},0,0,947,2048);
            try
            {
                var title=Extract(copy,new Rect(178,230,590,205),folder+"/Title_Shuffle.png",false,sx,sy);
                Register(catalog,title);
                add(new UIElement{name="Title_Shuffle_Artwork",kind="image",spriteKey=AssetCatalog.Key(title)},178,230,590,205);
                var subtitle=Extract(copy,new Rect(294,461,361,61),folder+"/Subtitle_Artwork.png",false,sx,sy);
                Register(catalog,subtitle);
                add(new UIElement{name="Subtitle_Artwork",kind="image",spriteKey=AssetCatalog.Key(subtitle)},294,461,361,61);
                var panel=Extract(copy,new Rect(38,1374,871,235),folder+"/Description_Artwork.png",true,sx,sy);
                Register(catalog,panel);
                add(new UIElement{name="Description_Artwork",kind="image",spriteKey=AssetCatalog.Key(panel)},38,1374,871,235);
                var tap=Extract(copy,new Rect(220,1793,510,81),folder+"/Continue_Artwork.png",false,sx,sy);
                Register(catalog,tap);
                add(new UIElement{name="Continue_Artwork",kind="image",spriteKey=AssetCatalog.Key(tap)},220,1793,510,81);
                var glow=Glow(folder+"/SoftGlow.png");
                Register(catalog,glow);
                var glowElement=new UIElement{name="SoftGoldenGlow",kind="image",spriteKey=AssetCatalog.Key(glow)};
                glowElement.x=135*sx;glowElement.y=610*sy;glowElement.width=675*sx;glowElement.height=620*sy;
                layout.elements.Insert(1,glowElement);
                var hat=catalog.Sprites.FirstOrDefault(s=>s.name=="Sprite_Booster_Shuffle_Large_Colored") ??
                    catalog.Sprites.FirstOrDefault(s=>s.name.IndexOf("Shuffle",StringComparison.OrdinalIgnoreCase)>=0&&s.name.IndexOf("Large",StringComparison.OrdinalIgnoreCase)>=0&&s.name.IndexOf("Colored",StringComparison.OrdinalIgnoreCase)>=0);
                if(hat==null)throw new InvalidOperationException("Không tìm thấy sprite Shuffle nguồn.");
                var hatElement=new UIElement{name="Booster_SourceSprite",kind="image",spriteKey=AssetCatalog.Key(hat)};
                hatElement.x=265*sx;hatElement.y=725*sy;hatElement.width=418*sx;hatElement.height=430*sy;
                layout.elements.Insert(2,hatElement);
                var prefab=UIPrefabBuilder.Save(layout,catalog,catalog.Fonts.FirstOrDefault()??TMP_Settings.defaultFontAsset,folder+"/Shuffle.prefab");
                var root=PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
                try
                {
                    var alternatives=new GameObject("EditableTextAlternatives");alternatives.transform.SetParent(root.transform,false);
                    var font=catalog.Fonts.FirstOrDefault(f=>f.name.IndexOf("Lalezar",StringComparison.OrdinalIgnoreCase)>=0)??catalog.Fonts.FirstOrDefault();
                    AddAlternative(alternatives.transform,font,"Title","Shuffle",new Rect(178*sx,230*sy,590*sx,205*sy),130*sx);
                    AddAlternative(alternatives.transform,font,"Subtitle","New Booster",new Rect(294*sx,461*sy,361*sx,61*sy),60*sx);
                    AddAlternative(alternatives.transform,font,"Description","Stuck? Shuffle all buses to create new\nmoves.",new Rect(70*sx,1438*sy,805*sx,125*sy),52*sx);
                    AddAlternative(alternatives.transform,font,"Continue","Tap to continue!",new Rect(220*sx,1793*sy,510*sx,81*sy),65*sx);
                    alternatives.SetActive(false);
                    var buttonObject=new GameObject("ContinueButton",typeof(RectTransform),typeof(Image),typeof(Button));
                    buttonObject.transform.SetParent(root.transform,false);
                    var rt=buttonObject.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);
                    rt.anchoredPosition=new Vector2(180*sx,-1760*sy);rt.sizeDelta=new Vector2(590*sx,155*sy);
                    buttonObject.GetComponent<Image>().color=Color.clear;
                    buttonObject.GetComponent<Button>().transition=Selectable.Transition.None;
                    PrefabUtility.SaveAsPrefabAsset(root,AssetDatabase.GetAssetPath(prefab));
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                File.WriteAllText(folder+"/NOTES.txt","Reference-specific Shuffle reconstruction. Stylized labels and description preserve source pixels for fidelity. EditableTextAlternatives is disabled to prevent doubled text: replace corresponding artwork when editing. Hat reuses source sprite. Soft glow is procedural and approximate. Gameplay is intentionally excluded; compare over the original reference separately.");
                return prefab;
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        static void AddAlternative(Transform parent,TMP_FontAsset font,string name,string value,Rect rect,float size)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);
            rt.anchoredPosition=new Vector2(rect.x,-rect.y);rt.sizeDelta=rect.size;
            var text=go.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSharedMaterial=font.material;text.text=value;
            text.fontSize=size;text.enableWordWrapping=false;text.alignment=TextAlignmentOptions.Midline;text.raycastTarget=false;
            text.color=name=="Description"?new Color32(155,91,67,255):new Color32(255,249,225,255);
        }
        static void Register(AssetCatalog c,Sprite s){c.Sprites.Add(s);c.SpriteKeys.Add(AssetCatalog.Key(s));}
        static Sprite Extract(Texture2D source,Rect bounds,string path,bool panel,float sx,float sy)
        {
            int left=Mathf.RoundToInt(bounds.x*sx),top=Mathf.RoundToInt(bounds.y*sy),w=Mathf.RoundToInt(bounds.width*sx),h=Mathf.RoundToInt(bounds.height*sy);
            var pixels=source.GetPixels(left,source.height-top-h,w,h);
            // Flood out the dim gameplay connected to the crop edge; retain enclosed dark outlines.
            var outside=new bool[w*h];var queue=new int[w*h];int begin=0,end=0;
            Action<int> visit=i=>{if(outside[i])return;Color p=pixels[i];
                bool foreground=panel?Mathf.Max(p.r,Mathf.Max(p.g,p.b))>.37f:
                    (p.b>p.r*1.25f&&p.b>.22f)||(p.r>.58f&&p.g>.56f&&p.b>.4f);
                if(foreground)return;outside[i]=true;queue[end++]=i;};
            for(int x=0;x<w;x++){visit(x);visit((h-1)*w+x);}for(int y=0;y<h;y++){visit(y*w);visit(y*w+w-1);}
            while(begin<end){int p=queue[begin++],x=p%w,y=p/w;if(x>0)visit(p-1);if(x<w-1)visit(p+1);if(y>0)visit(p-w);if(y<h-1)visit(p+w);}
            for(int i=0;i<pixels.Length;i++)if(outside[i])pixels[i]=Color.clear;
            var t=new Texture2D(w,h,TextureFormat.RGBA32,false);t.SetPixels(pixels);t.Apply();
            return Save(t,path);
        }
        static Sprite Glow(string path)
        {
            const int size=768;var t=new Texture2D(size,size,TextureFormat.RGBA32,false);var colors=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                float px=(x-size*.5f)/(size*.5f),py=(y-size*.5f)/(size*.5f),r=Mathf.Sqrt(px*px+py*py);
                float angle=Mathf.Atan2(py,px);
                float rays=Mathf.Pow(Mathf.Max(0,Mathf.Cos(angle*9+Mathf.Sin(angle*4)*.8f)),12);
                float alpha=Mathf.Pow(Mathf.Clamp01(1-r),2)*.32f+rays*Mathf.Pow(Mathf.Clamp01(1-r),1.4f)*.5f;
                colors[y*size+x]=new Color(1,.77f,.12f,alpha);
            }
            t.SetPixels(colors);t.Apply();return Save(t,path);
        }
        static Sprite Save(Texture2D t,string path)
        {
            try{File.WriteAllBytes(path,t.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(t);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
