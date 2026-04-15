using System;
using System.Collections.Generic;

// Name-based material index mapping utilities for before/after PMX material lists.
internal static class MaterialIndexMapper
{
    public static bool AreEquivalent(IList<string> left, IList<string> right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(NormalizeName(left[index]), NormalizeName(right[index]), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static Dictionary<int, int> BuildMap(IList<string> baseMaterialNames, IList<string> modifiedMaterialNames)
    {
        Dictionary<string, Queue<int>> targetIndicesByName = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < modifiedMaterialNames.Count; index++)
        {
            string key = NormalizeName(modifiedMaterialNames[index]);
            Queue<int> queue;
            if (!targetIndicesByName.TryGetValue(key, out queue))
            {
                queue = new Queue<int>();
                targetIndicesByName[key] = queue;
            }

            queue.Enqueue(index);
        }

        Dictionary<int, int> map = new Dictionary<int, int>();
        for (int index = 0; index < baseMaterialNames.Count; index++)
        {
            string key = NormalizeName(baseMaterialNames[index]);
            Queue<int> queue;
            if (!targetIndicesByName.TryGetValue(key, out queue))
            {
                continue;
            }

            if (queue.Count == 0)
            {
                continue;
            }

            map[index] = queue.Dequeue();
        }

        return map;
    }

    public static List<IndexedMaterialName> FindUnmatchedBaseMaterials(IList<string> baseMaterialNames, IDictionary<int, int> map)
    {
        List<IndexedMaterialName> result = new List<IndexedMaterialName>();
        for (int index = 0; index < baseMaterialNames.Count; index++)
        {
            if (map.ContainsKey(index))
            {
                continue;
            }

            result.Add(new IndexedMaterialName { Index = index, Name = SafeDisplayName(baseMaterialNames[index]) });
        }

        return result;
    }

    public static List<IndexedMaterialName> FindUnmatchedModifiedMaterials(IList<string> modifiedMaterialNames, IDictionary<int, int> map)
    {
        HashSet<int> usedTargetIndices = new HashSet<int>(map.Values);
        List<IndexedMaterialName> result = new List<IndexedMaterialName>();

        for (int index = 0; index < modifiedMaterialNames.Count; index++)
        {
            if (usedTargetIndices.Contains(index))
            {
                continue;
            }

            result.Add(new IndexedMaterialName { Index = index, Name = SafeDisplayName(modifiedMaterialNames[index]) });
        }

        return result;
    }

    private static string NormalizeName(string value)
    {
        return SafeDisplayName(value).Trim();
    }

    private static string SafeDisplayName(string value)
    {
        return string.IsNullOrEmpty(value) ? "(材質名なし)" : value;
    }
}
