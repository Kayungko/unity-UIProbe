using UnityEngine;
using UnityEngine.UI;

namespace UIProbe.Core
{
    /// <summary>
    /// UI 模糊显示组件
    /// 配合 UIBlurManager 使用，显示模糊后的背景
    /// </summary>
    [AddComponentMenu("UI/UIProbe/UI Blur Image")]
    public class UIBlurImage : Image
    {
        [SerializeField, Range(0, 1)]
        private float _blurIntensity = 1.0f;

        public float BlurIntensity
        {
            get => _blurIntensity;
            set
            {
                if (_blurIntensity != value)
                {
                    _blurIntensity = value;
                    UpdateMaterial();
                }
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Application.isPlaying)
            {
                UIBlurManager.Instance.RegisterElement();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Application.isPlaying && UIBlurManager.Instance != null)
            {
                UIBlurManager.Instance.UnregisterElement();
            }
        }

        private void UpdateMaterial()
        {
            if (canvasRenderer != null)
            {
                Material mat = materialForRendering;
                if (mat != null)
                {
                    mat.SetFloat("_BlurIntensity", _blurIntensity);
                }
            }
        }

        #if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            UpdateMaterial();
        }
        #endif
    }
}
