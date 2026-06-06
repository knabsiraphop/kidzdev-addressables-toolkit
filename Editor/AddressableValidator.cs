using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace KidzDev.AddressablesToolkit.Editor
{
    /// <summary>Scans Addressable entries for duplicate addresses and missing assets.</summary>
    public static class AddressableValidator
    {
        [MenuItem("Tools/Addressables Toolkit/Validate Addressables", false, 2000)]
        public static void Validate()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[Addressables Toolkit] No Addressable Asset Settings found.");
                return;
            }

            var byAddress = new Dictionary<string, List<string>>();
            int total = 0, missing = 0;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    total++;
                    if (string.IsNullOrEmpty(entry.AssetPath) || AssetDatabase.LoadAssetAtPath<Object>(entry.AssetPath) == null)
                    {
                        Debug.LogWarning($"[Addressables Toolkit] Missing asset for entry '{entry.address}' in group '{group.Name}'.");
                        missing++;
                    }

                    if (!byAddress.TryGetValue(entry.address, out var groups))
                        byAddress[entry.address] = groups = new List<string>();
                    groups.Add(group.Name);
                }
            }

            int duplicates = 0;
            foreach (var pair in byAddress)
            {
                if (pair.Value.Count > 1)
                {
                    duplicates++;
                    Debug.LogWarning($"[Addressables Toolkit] Duplicate address '{pair.Key}' used by {pair.Value.Count} entries (groups: {string.Join(", ", pair.Value)}).");
                }
            }

            Debug.Log($"[Addressables Toolkit] Validation done — {total} entries, {missing} missing, {duplicates} duplicate address(es).");
        }
    }
}
