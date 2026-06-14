using UnityEditor;
using UnityEngine;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Editor.Infrastructure
{
    /// <summary>IAssetGateway 的生产实现,真实调用 AssetDatabase。须在主线程使用。</summary>
    public sealed class UnityAssetGateway : IAssetGateway
    {
        public string[] FindAssets(string filter)
        {
            return AssetDatabase.FindAssets(filter);
        }

        public T LoadAssetAtPath<T>(string assetPath) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        public string MoveAsset(string sourcePath, string destinationPath)
        {
            return AssetDatabase.MoveAsset(sourcePath, destinationPath);
        }

        public string GUIDToAssetPath(string guid)
        {
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        public string AssetPathToGUID(string assetPath)
        {
            return AssetDatabase.AssetPathToGUID(assetPath);
        }
    }
}
