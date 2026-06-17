using System.Collections.Generic;
using UnityEngine;

namespace UIProbe.Infrastructure.UnityAdapters
{
    /// <summary>
    /// AssetDatabase / PrefabStage 等静态资源 API 的接缝。
    /// 约定:所有方法必须在 Unity 主线程调用(经 Dispatcher 调度);
    /// 后台线程直接调用 AssetDatabase 在 Editor 下是未定义行为。
    /// </summary>
    public interface IAssetGateway
    {
        /// <summary>按 filter 查找资源,返回 GUID 列表(对应 AssetDatabase.FindAssets)。</summary>
        string[] FindAssets(string filter);

        /// <summary>按路径加载资源(对应 AssetDatabase.LoadAssetAtPath)。未命中返回 null。</summary>
        T LoadAssetAtPath<T>(string assetPath) where T : Object;

        /// <summary>移动 / 重命名资源。成功返回空字符串,失败返回错误信息(对应 AssetDatabase.MoveAsset)。</summary>
        string MoveAsset(string sourcePath, string destinationPath);

        /// <summary>GUID 转资源路径,未命中返回空字符串。</summary>
        string GUIDToAssetPath(string guid);

        /// <summary>资源路径转 GUID,未命中返回空字符串。</summary>
        string AssetPathToGUID(string assetPath);

        /// <summary>
        /// 加载 prefab 并收集其 Image/RawImage/Material/Prefab 资源引用。
        /// prefab 不存在或无引用时返回空列表(非 null)。须在主线程调用。
        /// </summary>
        IReadOnlyList<AssetReferenceRecord> CollectReferences(string prefabPath);

        /// <summary>
        /// 加载 prefab 并把每个节点的 UI 检测要点扁平为中立 PrefabNodeRecord 列表(含根节点)。
        /// prefab 不存在时返回空列表(非 null)。须在主线程调用,供 UICheckService 在 Core 侧跑规则。
        /// </summary>
        IReadOnlyList<PrefabNodeRecord> InspectPrefab(string prefabPath);
    }
}
