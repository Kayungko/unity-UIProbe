using System;
using System.Collections.Generic;

namespace UIProbe
{
    // 资源信息
    [Serializable]
    public class ResourceInfo
    {
        public string AssetPath;           // 资源路径
        public string AssetName;           // 资源名称
        public long FileSize;              // 文件大小(字节)
        public string AssetType;           // Sprite/Texture2D
        public bool IsUsed;                // 是否被使用
        public List<ReferenceInfo> References = new List<ReferenceInfo>(); // 引用列表
    }

    // 引用信息
    [Serializable]
    public class ReferenceInfo
    {
        public string ReferrerPath;        // 引用者路径
        public string ReferrerType;        // Prefab/Scene/Material/Animation/Particle
        public string ComponentType;       // Image/RawImage/ParticleSystemRenderer等
    }
}
