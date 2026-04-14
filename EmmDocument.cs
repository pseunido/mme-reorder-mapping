using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// EMM parser/editor that locates objects and remaps material-scoped effect lines.
internal sealed class EmmDocument
{
    private static readonly StringComparison NameComparison = StringComparison.OrdinalIgnoreCase;

    private readonly List<EmmSection> _sections = new List<EmmSection>();
    private readonly Encoding _encoding;

    private EmmDocument(Encoding encoding)
    {
        _encoding = encoding;
    }

    public static EmmDocument Load(string path)
    {
        TextFileContent textFile = TextFileHelper.ReadAllText(path);
        string[] lines = SplitLines(textFile.Text);
        EmmDocument document = new EmmDocument(textFile.Encoding);

        EmmSection currentSection = new EmmSection(string.Empty);
        document._sections.Add(currentSection);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                currentSection = new EmmSection(trimmed.Substring(1, trimmed.Length - 2));
                document._sections.Add(currentSection);
                continue;
            }

            currentSection.Lines.Add(line);
        }

        return document;
    }

    public void Save(string path)
    {
        StringBuilder builder = new StringBuilder();

        for (int index = 0; index < _sections.Count; index++)
        {
            EmmSection section = _sections[index];
            if (index > 0)
            {
                builder.AppendLine("[" + section.Name + "]");
            }

            for (int lineIndex = 0; lineIndex < section.Lines.Count; lineIndex++)
            {
                builder.AppendLine(section.Lines[lineIndex]);
            }
        }

        TextFileHelper.WriteAllText(path, builder.ToString(), _encoding);
    }

    public EmmObjectReference ResolveObjectReferenceByMaterialNames(IList<string> currentMaterialNames)
    {
        EmmSection objectSection = GetSection("Object");
        if (objectSection == null)
        {
            throw new InvalidOperationException("EMM に [Object] セクションがありません。");
        }

        List<EmmObjectReference> exactMatches = new List<EmmObjectReference>();

        for (int index = 0; index < objectSection.Lines.Count; index++)
        {
            KeyValueLine line;
            if (!KeyValueLine.TryParse(objectSection.Lines[index], out line))
            {
                continue;
            }

            ParsedObjectKey key;
            if (!ParsedObjectKey.TryParse(line.Key, out key))
            {
                continue;
            }

            if (key.MaterialIndex.HasValue || key.IsShow)
            {
                continue;
            }

            if (!PathHelper.HasPmxExtension(line.Value) || !File.Exists(line.Value))
            {
                continue;
            }

            List<string> materialNames;
            try
            {
                materialNames = PmxMaterialReader.ReadMaterialNames(line.Value);
            }
            catch
            {
                continue;
            }

            if (MaterialIndexMapper.AreEquivalent(materialNames, currentMaterialNames))
            {
                EmmObjectReference match = new EmmObjectReference();
                match.ObjectKey = key.ObjectKey;
                match.ObjectPath = line.Value;
                exactMatches.Add(match);
            }
        }

        if (exactMatches.Count == 1)
        {
            return exactMatches[0];
        }

        if (exactMatches.Count > 1)
        {
            throw new InvalidOperationException("EMM 内で現在ロード中モデルに一致するオブジェクトキーが複数見つかりました。ベースPMX(任意)を指定して対象を明示してください。");
        }

        throw new InvalidOperationException("EMM 内で現在ロード中モデルに対応するオブジェクトキーを見つけられませんでした。現在の材質構成と一致する PMX が [Object] セクションにあるか確認してください。");
    }

    public EmmObjectReference ResolveObjectReferenceByPath(string modelPath)
    {
        EmmSection objectSection = GetSection("Object");
        if (objectSection == null)
        {
            throw new InvalidOperationException("EMM に [Object] セクションがありません。");
        }

        string normalizedTargetPath = PathHelper.NormalizePath(modelPath);
        List<EmmObjectReference> exactMatches = new List<EmmObjectReference>();
        List<EmmObjectReference> fileNameMatches = new List<EmmObjectReference>();
        string targetFileName = Path.GetFileName(modelPath);

        for (int index = 0; index < objectSection.Lines.Count; index++)
        {
            KeyValueLine line;
            if (!KeyValueLine.TryParse(objectSection.Lines[index], out line))
            {
                continue;
            }

            ParsedObjectKey key;
            if (!ParsedObjectKey.TryParse(line.Key, out key))
            {
                continue;
            }

            if (key.MaterialIndex.HasValue || key.IsShow)
            {
                continue;
            }

            EmmObjectReference match = new EmmObjectReference();
            match.ObjectKey = key.ObjectKey;
            match.ObjectPath = line.Value;

            string normalizedLinePath = PathHelper.NormalizePath(line.Value);
            if (string.Equals(normalizedLinePath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                exactMatches.Add(match);
            }

            if (string.Equals(Path.GetFileName(line.Value), targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                fileNameMatches.Add(match);
            }
        }

        if (exactMatches.Count == 1)
        {
            return exactMatches[0];
        }

        if (exactMatches.Count > 1)
        {
            throw new InvalidOperationException("EMM 内でベースPMXに一致するオブジェクトキーが複数見つかりました。");
        }

        if (fileNameMatches.Count == 1)
        {
            return fileNameMatches[0];
        }

        if (fileNameMatches.Count > 1)
        {
            throw new InvalidOperationException("EMM 内で同名PMXが複数見つかりました。ベースPMXの絶対パス一致で特定できるように EMM 内容を確認してください。");
        }

        throw new InvalidOperationException("EMM 内で指定したベースPMXに対応するオブジェクトキーを見つけられませんでした。");
    }

    public void SetObjectPath(string objectKey, string newPath)
    {
        EmmSection objectSection = GetSection("Object");
        if (objectSection == null)
        {
            return;
        }

        for (int index = 0; index < objectSection.Lines.Count; index++)
        {
            KeyValueLine line;
            if (!KeyValueLine.TryParse(objectSection.Lines[index], out line))
            {
                continue;
            }

            ParsedObjectKey key;
            if (!ParsedObjectKey.TryParse(line.Key, out key))
            {
                continue;
            }

            if (!string.Equals(key.ObjectKey, objectKey, NameComparison))
            {
                continue;
            }

            if (key.MaterialIndex.HasValue || key.IsShow)
            {
                continue;
            }

            objectSection.Lines[index] = line.Key + " = " + newPath;
            return;
        }
    }

    public MaterialRemapMetrics RemapMaterialEntries(string objectKey, IDictionary<int, int> materialMap, int targetMaterialCount)
    {
        MaterialRemapMetrics totalStats = new MaterialRemapMetrics();

        for (int sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
        {
            EmmSection section = _sections[sectionIndex];
            if (!section.Name.StartsWith("Effect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MaterialRemapMetrics sectionStats = RemapSectionMaterialEntries(section, objectKey, materialMap, targetMaterialCount);
            totalStats.MaterialEffectLineCount += sectionStats.MaterialEffectLineCount;
            totalStats.MaterialShowLineCount += sectionStats.MaterialShowLineCount;
            totalStats.DroppedMaterialLineCount += sectionStats.DroppedMaterialLineCount;
        }

        return totalStats;
    }

    private static MaterialRemapMetrics RemapSectionMaterialEntries(EmmSection section, string objectKey, IDictionary<int, int> materialMap, int targetMaterialCount)
    {
        MaterialRemapMetrics stats = new MaterialRemapMetrics();
        Dictionary<int, string> effectLines = new Dictionary<int, string>();
        Dictionary<int, string> showLines = new Dictionary<int, string>();
        List<string> remainingLines = new List<string>();

        // We rebuild only material-scoped entries for a target object, preserving all unrelated lines.
        for (int index = 0; index < section.Lines.Count; index++)
        {
            string rawLine = section.Lines[index];
            KeyValueLine line;
            if (!KeyValueLine.TryParse(rawLine, out line))
            {
                remainingLines.Add(rawLine);
                continue;
            }

            ParsedObjectKey key;
            if (!ParsedObjectKey.TryParse(line.Key, out key))
            {
                remainingLines.Add(rawLine);
                continue;
            }

            if (!string.Equals(key.ObjectKey, objectKey, NameComparison) || !key.MaterialIndex.HasValue)
            {
                remainingLines.Add(rawLine);
                continue;
            }

            int sourceIndex = key.MaterialIndex.Value;
            int targetIndex;
            if (!materialMap.TryGetValue(sourceIndex, out targetIndex) || targetIndex < 0 || targetIndex >= targetMaterialCount)
            {
                stats.DroppedMaterialLineCount++;
                continue;
            }

            if (key.IsShow)
            {
                showLines[targetIndex] = line.Value;
            }
            else
            {
                effectLines[targetIndex] = line.Value;
            }
        }

        int insertIndex = FindInsertIndex(remainingLines, objectKey);
        List<string> insertedLines = BuildMaterialLines(objectKey, effectLines, showLines, stats);
        remainingLines.InsertRange(insertIndex, insertedLines);
        section.Lines = remainingLines;

        return stats;
    }

    private static int FindInsertIndex(IList<string> lines, string objectKey)
    {
        int insertIndex = lines.Count;

        for (int index = 0; index < lines.Count; index++)
        {
            KeyValueLine line;
            if (!KeyValueLine.TryParse(lines[index], out line))
            {
                continue;
            }

            ParsedObjectKey key;
            if (!ParsedObjectKey.TryParse(line.Key, out key))
            {
                continue;
            }

            if (!string.Equals(key.ObjectKey, objectKey, NameComparison))
            {
                continue;
            }

            if (key.MaterialIndex.HasValue)
            {
                continue;
            }

            insertIndex = index + 1;
        }

        return insertIndex;
    }

    private static List<string> BuildMaterialLines(string objectKey, IDictionary<int, string> effectLines, IDictionary<int, string> showLines, MaterialRemapMetrics stats)
    {
        List<string> lines = new List<string>();
        SortedSet<int> indices = new SortedSet<int>();

        foreach (int key in effectLines.Keys)
        {
            indices.Add(key);
        }

        foreach (int key in showLines.Keys)
        {
            indices.Add(key);
        }

        foreach (int index in indices)
        {
            string effectValue;
            if (effectLines.TryGetValue(index, out effectValue))
            {
                lines.Add(objectKey + "[" + index.ToString() + "] = " + effectValue);
                stats.MaterialEffectLineCount++;
            }

            string showValue;
            if (showLines.TryGetValue(index, out showValue))
            {
                lines.Add(objectKey + "[" + index.ToString() + "].show = " + showValue);
                stats.MaterialShowLineCount++;
            }
        }

        return lines;
    }

    private EmmSection GetSection(string name)
    {
        for (int index = 0; index < _sections.Count; index++)
        {
            if (string.Equals(_sections[index].Name, name, NameComparison))
            {
                return _sections[index];
            }
        }

        return null;
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
}

internal sealed class EmmSection
{
    public EmmSection(string name)
    {
        Name = name;
        Lines = new List<string>();
    }

    public string Name;
    public List<string> Lines;
}

internal struct KeyValueLine
{
    public string Key;
    public string Value;

    public static bool TryParse(string rawLine, out KeyValueLine line)
    {
        line = new KeyValueLine();
        int separatorIndex = rawLine.IndexOf('=');
        if (separatorIndex < 0)
        {
            return false;
        }

        line.Key = rawLine.Substring(0, separatorIndex).Trim();
        line.Value = rawLine.Substring(separatorIndex + 1).Trim();
        return line.Key.Length > 0;
    }
}

internal struct ParsedObjectKey
{
    private static readonly Regex Pattern = new Regex(@"^(?<object>[A-Za-z]+\d+)(\[(?<index>\d+)\])?(?<show>\.show)?$", RegexOptions.Compiled);

    public string ObjectKey;
    public int? MaterialIndex;
    public bool IsShow;

    public static bool TryParse(string key, out ParsedObjectKey result)
    {
        result = new ParsedObjectKey();
        Match match = Pattern.Match(key);
        if (!match.Success)
        {
            return false;
        }

        result.ObjectKey = match.Groups["object"].Value;
        if (match.Groups["index"].Success)
        {
            result.MaterialIndex = int.Parse(match.Groups["index"].Value);
        }

        result.IsShow = match.Groups["show"].Success;
        return true;
    }
}
