using System;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace ScreenshotUIComposer
{
    public sealed partial class UIComposerWindow
    {
        [SerializeField] bool routerEnabled, routerFoldout = true;
        [SerializeField] NineRouterSettings router = new NineRouterSettings();
        [SerializeField] string[] routerVisionModels = new string[0], routerImageModels = new string[0];
        [NonSerialized] string routerKey = "";

        void LoadRouterPreferences()
        {
            if(router==null)router=new NineRouterSettings();
            string prefix=Application.dataPath+".UIComposer.9Router.";
            router.baseUrl=EditorPrefs.GetString(prefix+"URL",Environment.GetEnvironmentVariable("NINEROUTER_URL")??router.baseUrl);
            router.visionModel=EditorPrefs.GetString(prefix+"Vision",router.visionModel);
            router.imageModel=EditorPrefs.GetString(prefix+"Image",router.imageModel);
            routerKey=Environment.GetEnvironmentVariable("NINEROUTER_KEY")??"";
        }

        void SaveRouterPreferences()
        {
            string prefix=Application.dataPath+".UIComposer.9Router.";
            EditorPrefs.SetString(prefix+"URL",router.baseUrl);
            EditorPrefs.SetString(prefix+"Vision",router.visionModel);
            EditorPrefs.SetString(prefix+"Image",router.imageModel);
        }

        void DrawRouterSettings()
        {
            EditorGUILayout.Space(6);
            routerEnabled=EditorGUILayout.ToggleLeft("Dùng 9Router để hoàn thiện text / UI",routerEnabled);
            if(!routerEnabled)return;
            routerFoldout=EditorGUILayout.Foldout(routerFoldout,"Kết nối API · URL / Key / Model",true);
            if(!routerFoldout)return;
            router.baseUrl=EditorGUILayout.TextField("Base URL",router.baseUrl);
            routerKey=EditorGUILayout.PasswordField("API key",routerKey);
            if(GUILayout.Button("Kiểm tra kết nối / Tải model")) DiscoverRouter();
            router.visionModel=EditorGUILayout.TextField("Model nhận diện",router.visionModel);
            ModelMenu("Chọn model nhận diện",routerVisionModels,ref router.visionModel);
            router.imageModel=EditorGUILayout.TextField("Model tạo ảnh",router.imageModel);
            ModelMenu("Chọn model tạo ảnh",routerImageModels,ref router.imageModel);
            router.maxNewImages=EditorGUILayout.IntSlider("Tối đa ảnh mới",router.maxNewImages,0,8);
            router.transparentImages=EditorGUILayout.Toggle("Model hỗ trợ alpha",router.transparentImages);
            if(GUILayout.Button("Ghi nhớ URL / Model")) { SaveRouterPreferences();SetStatus("Đã ghi nhớ URL và model. API key chỉ giữ trong bộ nhớ.",MessageType.Info); }
            EditorGUILayout.HelpBox("Khi bật: gửi ảnh mẫu và danh mục asset tới 9Router để sửa bố cục/chữ. Sprite thiếu được tạo bằng model ảnh. Key chỉ giữ trong bộ nhớ; có thể dùng NINEROUTER_KEY. Model nhận diện cần hỗ trợ ảnh; model tạo ảnh cần hỗ trợ images/generations đồng bộ.",MessageType.Info);
        }

        static void ModelMenu(string label,string[] models,ref string value)
        {
            if(models==null||models.Length==0)return;
            int current=Array.IndexOf(models,value);
            string[] options=new[]{"— "+label+" —"}.Concat(models).ToArray();
            int chosen=EditorGUILayout.Popup(current+1,options);
            if(chosen>0)value=models[chosen-1];
        }

        async void DiscoverRouter()
        {
            if(cancellation!=null)return;
            cancellation=new CancellationTokenSource();
            try
            {
                SaveRouterPreferences();
                SetStatus("Đang tải model từ 9Router...",MessageType.Info);
                var models=await NineRouterComposer.Discover(router,routerKey,cancellation.Token);
                if(this==null)return;
                routerVisionModels=models.Vision;routerImageModels=models.Images;
                if(models.Images.Length>0 && !models.Images.Contains(router.imageModel))
                    router.imageModel=models.Images[0];
                SaveRouterPreferences();
                SetStatus("Kết nối được: "+models.Vision.Length+" model chat và "+models.Images.Length+
                    " model tạo ảnh. Đây mới là kết nối router; provider vẫn có thể từ chối khi chat.",MessageType.Info);
            }
            catch(OperationCanceledException){if(this!=null)SetStatus("Đã hủy kết nối.",MessageType.Info);}
            catch(Exception ex){if(this!=null)SetStatus(ex.Message,MessageType.Error);}
            finally{cancellation?.Dispose();cancellation=null;if(this!=null)Repaint();}
        }
    }
}
