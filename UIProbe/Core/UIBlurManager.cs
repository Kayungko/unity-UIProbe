using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UIProbe.Core
{
    /// <summary>
    /// UI 模糊管理器
    /// 负责截屏、降采样和高斯模糊处理
    /// </summary>
    public class UIBlurManager : MonoBehaviour
    {
        private static UIBlurManager _instance;
        public static UIBlurManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("UIBlurManager");
                    _instance = go.AddComponent<UIBlurManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [Header("模糊设置")]
        [Range(1, 4)] public int downsample = 2; // 降采样倍数
        [Range(0, 4)] public int iterations = 2;   // 模糊迭代次数
        [Range(0, 5)] public float blurSpread = 0.6f; // 模糊扩散
        public LayerMask captureLayers = -1;       // 要模糊的层级（通常不包含 UI 层）

        private RenderTexture _blurRT;
        private Shader _blurProcessShader;
        private Material _blurMaterial;
        private Camera _captureCamera;

        private int _activeBlurElements = 0;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            
            // 加载用于处理模糊的私有 Shader (这里可以内置一个简单的 Dual Blur 或 Gaussian)
            _blurProcessShader = Shader.Find("Hidden/UIProbe/BlurProcess");
            if (_blurProcessShader != null)
                _blurMaterial = new Material(_blurProcessShader);
        }

        public void RegisterElement()
        {
            _activeBlurElements++;
            if (_activeBlurElements == 1)
            {
                enabled = true;
                StartCapture();
            }
        }

        public void UnregisterElement()
        {
            _activeBlurElements--;
            if (_activeBlurElements <= 0)
            {
                _activeBlurElements = 0;
                enabled = false;
                StopCapture();
            }
        }

        private void StartCapture()
        {
            if (_captureCamera == null)
            {
                GameObject camGo = new GameObject("BlurCaptureCamera");
                camGo.transform.SetParent(transform);
                _captureCamera = camGo.AddComponent<Camera>();
                _captureCamera.enabled = false; // 手动触发渲染
            }
        }

        private void StopCapture()
        {
            if (_blurRT != null)
            {
                RenderTexture.ReleaseTemporary(_blurRT);
                _blurRT = null;
            }
        }

        private void Update()
        {
            if (_activeBlurElements <= 0) return;

            UpdateBlurTexture();
        }

        private void UpdateBlurTexture()
        {
            int rtW = Screen.width >> downsample;
            int rtH = Screen.height >> downsample;

            if (_blurRT != null && (_blurRT.width != rtW || _blurRT.height != rtH))
            {
                RenderTexture.ReleaseTemporary(_blurRT);
                _blurRT = null;
            }

            if (_blurRT == null)
            {
                _blurRT = RenderTexture.GetTemporary(rtW, rtH, 0, RenderTextureFormat.Default);
                Shader.SetGlobalTexture("_GlobalBlurTex", _blurRT);
            }

            // 同步主相机参数（除层级外）
            Camera mainCam = Camera.main;
            if (mainCam != null && _captureCamera != null)
            {
                _captureCamera.CopyFrom(mainCam);
                _captureCamera.cullingMask = captureLayers;
                _captureCamera.targetTexture = _blurRT;
                _captureCamera.Render();
                _captureCamera.targetTexture = null;

                // 在这里可以进一步进行高斯模糊多遍处理
                // 如果没有处理材质，就直接用降采样的结果
                if (_blurMaterial != null && iterations > 0)
                {
                    RenderTexture temp = RenderTexture.GetTemporary(rtW, rtH, 0, RenderTextureFormat.Default);
                    for (int i = 0; i < iterations; i++)
                    {
                        _blurMaterial.SetFloat("_BlurSize", 1.0f + i * blurSpread);
                        Graphics.Blit(_blurRT, temp, _blurMaterial, 0); // Horizontal
                        Graphics.Blit(temp, _blurRT, _blurMaterial, 1); // Vertical
                    }
                    RenderTexture.ReleaseTemporary(temp);
                }
            }
        }

        private void OnDestroy()
        {
            StopCapture();
        }
    }
}
