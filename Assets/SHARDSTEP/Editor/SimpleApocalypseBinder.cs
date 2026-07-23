using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Shardstep.Editor
{
    public static class SimpleApocalypseBinder
    {
        private const string CatalogPath = "Assets/SHARDSTEP/Generated/Resources/SimpleApocalypseCatalog.asset";

        private static readonly Dictionary<string, string[]> SearchTokens = new Dictionary<string, string[]>
        {
            { "SecurityWall", new[] { "SecurityWallPiece", "SecurityWall" } },
            { "SecurityGate", new[] { "SecurityWallGate", "SecurityGate" } },
            { "ShippingContainer", new[] { "ShippingContainer" } },
            { "Barrier", new[] { "Barrier" } },
            { "FloodLight", new[] { "FloodLight" } },
            { "Rubble", new[] { "Rubble" } },
            { "AmmoCrate", new[] { "AmmoCrate", "AmmoBox" } },
            { "SafehouseDoor", new[] { "SafehouseDoor", "Door" } }
        };

        [MenuItem("SHARDSTEP/Assets/Scan SIMPLE Apocalypse")]
        public static void ScanAndCreateCatalog()
        {
            string directory = Path.GetDirectoryName(CatalogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SimpleApocalypseCatalog catalog = ScriptableObject.CreateInstance<SimpleApocalypseCatalog>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

            foreach (KeyValuePair<string, string[]> mapping in SearchTokens)
            {
                GameObject match = FindFirstMatchingPrefab(prefabGuids, mapping.Value);
                catalog.entries.Add(new SimpleApocalypseCatalogEntry
                {
                    key = mapping.Key,
                    prefab = match
                });

                if (match == null)
                {
                    Debug.LogWarning($"SIMPLE Apocalypse mapping not found: {mapping.Key}");
                }
                else
                {
                    Debug.Log($"Mapped {mapping.Key} -> {AssetDatabase.GetAssetPath(match)}");
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SimpleApocalypseCatalog>(CatalogPath) != null)
            {
                AssetDatabase.DeleteAsset(CatalogPath);
            }

            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
        }

        private static GameObject FindFirstMatchingPrefab(string[] guids, string[] tokens)
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);

                foreach (string token in tokens)
                {
                    if (!fileName.Contains(token, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                    {
                        return prefab;
                    }
                }
            }

            return null;
        }
    }
}
