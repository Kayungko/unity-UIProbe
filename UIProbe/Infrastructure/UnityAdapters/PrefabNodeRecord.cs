namespace UIProbe.Infrastructure.UnityAdapters
{
    /// <summary>
    /// prefab 单个节点的中立检视记录,由 IAssetGateway.InspectPrefab 产出。
    /// 把节点上 UI 组件的检测要点(Image/Text/Graphic)扁平成 Core.Services 可读的纯数据,
    /// 避免把 UnityEngine.UI 类型层级泄漏进 Core(同 AssetReferenceRecord 的避循环约定)。
    /// </summary>
    public sealed class PrefabNodeRecord
    {
        /// <summary>相对 prefab 根的节点路径,根节点为根名本身(与 CollectReferences 的 NodePath 约定一致)。</summary>
        public string NodePath;

        /// <summary>节点名(GameObject.name)。</summary>
        public string Name;

        /// <summary>是否为 prefab 根节点(重名/部分规则需跳过根)。</summary>
        public bool IsRoot;

        /// <summary>是否为可交互控件(Selectable 或 ScrollRect),由适配层判定,避免泄漏 Unity 类型。</summary>
        public bool IsInteractable;

        /// <summary>是否挂有 Image 组件。</summary>
        public bool HasImage;

        /// <summary>Image.sprite 是否已赋值。</summary>
        public bool ImageSpriteAssigned;

        /// <summary>Image.color 的 alpha 通道(用于判定可见的缺图)。</summary>
        public float ImageColorAlpha;

        /// <summary>是否挂有 Text 组件。</summary>
        public bool HasText;

        /// <summary>Text.font 是否已赋值。</summary>
        public bool TextFontAssigned;

        /// <summary>Text.text 内容(用于空文本检测)。</summary>
        public string TextContent;

        /// <summary>是否挂有 Graphic 组件(Image/Text/RawImage 等图形基类)。</summary>
        public bool HasGraphic;

        /// <summary>Graphic.raycastTarget 是否开启(用于不必要 RaycastTarget 检测)。</summary>
        public bool GraphicRaycastTarget;
    }
}
