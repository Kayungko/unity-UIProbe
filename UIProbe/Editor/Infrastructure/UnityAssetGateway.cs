using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// 移植自遗留 UIProbeWindow_Indexer.CollectAssetReferences:
        /// 加载 prefab,遍历 Image/RawImage(图片纹理)、嵌套 Prefab、Renderer 材质纹理引用。
        /// </summary>
        public IReadOnlyList<AssetReferenceRecord> CollectReferences(string prefabPath)
        {
            var records = new List<AssetReferenceRecord>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return records;

            CollectImageReferences(records, prefab);
            CollectPrefabReferences(records, prefab);
            CollectMaterialReferences(records, prefab);
            return records;
        }

        private static void CollectImageReferences(List<AssetReferenceRecord> records, GameObject prefab)
        {
            foreach (var image in prefab.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.sprite == null) continue;
                string spritePath = AssetDatabase.GetAssetPath(image.sprite);
                if (string.IsNullOrEmpty(spritePath)) continue;

                string spriteName = image.sprite.name;
                string fileName = Path.GetFileName(spritePath);
                records.Add(new AssetReferenceRecord
                {
                    AssetPath = spritePath,
                    Guid = AssetDatabase.AssetPathToGUID(spritePath),
                    NodePath = GetNodePath(prefab.transform, image.transform),
                    AssetName = spriteName,
                    Kind = "Image",
                    ExtraInfo = spriteName != fileName ? "Image (" + fileName + ")" : "Image"
                });
            }

            foreach (var rawImage in prefab.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
            {
                if (rawImage.texture == null) continue;
                string texturePath = AssetDatabase.GetAssetPath(rawImage.texture);
                if (string.IsNullOrEmpty(texturePath)) continue;

                records.Add(new AssetReferenceRecord
                {
                    AssetPath = texturePath,
                    Guid = AssetDatabase.AssetPathToGUID(texturePath),
                    NodePath = GetNodePath(prefab.transform, rawImage.transform),
                    AssetName = Path.GetFileName(texturePath),
                    Kind = "RawImage",
                    ExtraInfo = "RawImage"
                });
            }
        }

        private static void CollectPrefabReferences(List<AssetReferenceRecord> records, GameObject prefab)
        {
            var processed = new HashSet<GameObject>();
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t == prefab.transform) continue;

                GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                if (prefabRoot == null || prefabRoot != t.gameObject || processed.Contains(prefabRoot)) continue;
                processed.Add(prefabRoot);

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                if (source == null) continue;
                string prefabRefPath = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(prefabRefPath)) continue;

                records.Add(new AssetReferenceRecord
                {
                    AssetPath = prefabRefPath,
                    Guid = AssetDatabase.AssetPathToGUID(prefabRefPath),
                    NodePath = GetNodePath(prefab.transform, t),
                    AssetName = Path.GetFileName(prefabRefPath),
                    Kind = "Prefab",
                    ExtraInfo = source.name
                });
            }
        }

        private static void CollectMaterialReferences(List<AssetReferenceRecord> records, GameObject prefab)
        {
            var processedMaterials = new HashSet<Material>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || processedMaterials.Contains(mat)) continue;
                    processedMaterials.Add(mat);

                    string materialPath = AssetDatabase.GetAssetPath(mat);
                    if (string.IsNullOrEmpty(materialPath)) continue;

                    var shader = mat.shader;
                    if (shader == null) continue;

                    string nodePath = GetNodePath(prefab.transform, renderer.transform);
                    int propertyCount = ShaderUtil.GetPropertyCount(shader);
                    for (int i = 0; i < propertyCount; i++)
                    {
                        if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        string propertyName = ShaderUtil.GetPropertyName(shader, i);
                        Texture texture = mat.GetTexture(propertyName);
                        if (texture == null) continue;
                        string texturePath = AssetDatabase.GetAssetPath(texture);
                        if (string.IsNullOrEmpty(texturePath)) continue;

                        records.Add(new AssetReferenceRecord
                        {
                            AssetPath = texturePath,
                            Guid = AssetDatabase.AssetPathToGUID(texturePath),
                            NodePath = nodePath,
                            AssetName = texture.name,
                            Kind = "Material",
                            ExtraInfo = "Material: " + mat.name + " (" + propertyName + ")"
                        });
                    }
                }
            }
        }

        private static string GetNodePath(Transform root, Transform target)
        {
            if (target == root) return root.name;
            var path = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", path);
        }
    }
}
