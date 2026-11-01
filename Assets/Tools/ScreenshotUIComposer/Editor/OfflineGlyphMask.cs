using UnityEngine;
namespace ScreenshotUIComposer {
    public static class OfflineGlyphMask {
        public static float[] Build(Color32[] pixels,int imageWidth,RectInt region,Color32 foreground,Color32 background,out RectInt bounds)
        {
            int minX=region.xMax,minY=region.yMax,maxX=region.x,maxY=region.y;
            var mask=new bool[region.width*region.height];
            for(int y=region.y;y<region.yMax;y++)for(int x=region.x;x<region.xMax;x++)
            {
                var p=pixels[y*imageWidth+x];
                bool ink=Distance(p,foreground)<Mathf.Min(.22f,Distance(p,background)*.8f);
                mask[(y-region.y)*region.width+x-region.x]=ink;
                if(ink){minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);}
            }
            bounds=new RectInt(minX,minY,Mathf.Max(1,maxX-minX+1),Mathf.Max(1,maxY-minY+1));
            var normalized=new float[96*24];
            for(int y=0;y<24;y++)for(int x=0;x<96;x++)
            {
                float sum=0;
                for(int yy=0;yy<2;yy++)for(int xx=0;xx<2;xx++)
                {
                    int px=Mathf.Clamp(bounds.x+(int)((x+(xx+.5f)/2)*bounds.width/96f)-region.x,0,region.width-1);
                    int py=Mathf.Clamp(bounds.y+(int)((y+(yy+.5f)/2)*bounds.height/24f)-region.y,0,region.height-1);
                    if(mask[py*region.width+px])sum+=.25f;
                }
                normalized[y*96+x]=sum;
            }
            return normalized;
        }

        static float Distance(Color32 a, Color32 b) { return (Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b))/765f; }
    }
}
