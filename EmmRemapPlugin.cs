using PEPlugin;
using PEPlugin.Pmx;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public class CSScriptClass : PEPluginClass
{
    public CSScriptClass() : base()
    {
        m_option = new PEPluginOption(false, true, "MME EMM再マップ");
    }

    public override void Run(IPERunArgs args)
    {
        base.Run(args);

        try
        {
            IPEConnector connect = args.Host.Connector;
            IPXPmx pmx = connect.Pmx.GetCurrentState();
            List<string> currentMaterialNames = BuildCurrentMaterialNameList(pmx);

            using (MainForm form = new MainForm(BuildCurrentModelPathHint(pmx)))
            {
                if (form.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                PluginInput input = form.BuildInput();
                ValidateInput(input);

                RemapExecutionResult result = ExecuteRemap(input, currentMaterialNames);
                MessageBox.Show(BuildSummary(result), "MME EMM再マップ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }
    }

    private static RemapExecutionResult ExecuteRemap(PluginInput input, IList<string> currentMaterialNames)
    {
        EmmDocument document = EmmDocument.Load(input.EmmPath);
        List<string> baseMaterialNames;
        EmmObjectMatch objectMatch;

        if (input.BasePmxPath.Length > 0)
        {
            baseMaterialNames = PmxMaterialReader.ReadMaterialNames(input.BasePmxPath);
            objectMatch = document.FindObjectByPath(input.BasePmxPath);
        }
        else
        {
            baseMaterialNames = new List<string>(currentMaterialNames);
            objectMatch = document.FindObjectByMaterialNames(baseMaterialNames);
        }

        List<string> modifiedMaterialNames = PmxMaterialReader.ReadMaterialNames(input.ModifiedPmxPath);
        string objectKey = objectMatch.ObjectKey;
        string outputObjectPath = input.KeepOriginalModelPath ? objectMatch.ObjectPath : input.ModifiedPmxPath;

        Dictionary<int, int> materialMap = MaterialIndexMapper.BuildMap(baseMaterialNames, modifiedMaterialNames);
        // Only update the object path if not keeping the original
        if (!input.KeepOriginalModelPath)
        {
            document.UpdateObjectPath(objectKey, input.ModifiedPmxPath);
        }

        MaterialRemapStats remapStats = document.RemapMaterialAssignments(objectKey, materialMap, modifiedMaterialNames.Count);

        EnsureOutputDirectory(input.OutputEmmPath);
        document.Save(input.OutputEmmPath);

        RemapExecutionResult result = new RemapExecutionResult();
        result.Input = input;
        result.ObjectKey = objectKey;
        result.SourceObjectPath = objectMatch.ObjectPath;
        result.OutputObjectPath = outputObjectPath;
        result.BaseMaterialNames = baseMaterialNames;
        result.ModifiedMaterialNames = modifiedMaterialNames;
        result.RemappedMaterialCount = materialMap.Count;
        result.UnmatchedBaseMaterials = MaterialIndexMapper.FindUnmatchedBaseMaterials(baseMaterialNames, materialMap);
        result.UnmatchedModifiedMaterials = MaterialIndexMapper.FindUnmatchedModifiedMaterials(modifiedMaterialNames, materialMap);
        result.RemapStats = remapStats;

        if (input.PmmPath.Length > 0)
        {
            if (input.KeepOriginalModelPath)
            {
                if (!string.Equals(PathHelper.NormalizePath(input.PmmPath), PathHelper.NormalizePath(input.OutputPmmPath), StringComparison.OrdinalIgnoreCase))
                {
                    EnsureOutputDirectory(input.OutputPmmPath);
                    File.Copy(input.PmmPath, input.OutputPmmPath, true);
                }

                result.PmmUpdated = false;
                result.PmmUpdateMessage = "入力PMMをそのまま出力しました。";
            }
            else
            {
                PmmRewriteResult pmmResult = PmmPathRewriter.TryRewriteModelPath(input.PmmPath, input.OutputPmmPath, objectMatch.ObjectPath, input.ModifiedPmxPath);
                result.PmmUpdated = pmmResult.Updated;
                result.PmmUpdateMessage = pmmResult.Message;
            }
        }

        return result;
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string BuildCurrentModelPathHint(IPXPmx pmx)
    {
        if (pmx == null || pmx.ModelInfo == null)
        {
            return string.Empty;
        }

        string name = SafeString(pmx.ModelInfo.ModelName);
        if (name.Length == 0)
        {
            name = "現在のPMX";
        }

        return name;
    }

    private static List<string> BuildCurrentMaterialNameList(IPXPmx pmx)
    {
        List<string> names = new List<string>();

        if (pmx == null || pmx.Material == null)
        {
            return names;
        }

        for (int index = 0; index < pmx.Material.Count; index++)
        {
            IPXMaterial material = pmx.Material[index];
            names.Add(SafeString(material != null ? material.Name : null));
        }

        return names;
    }

    private static void ValidateInput(PluginInput input)
    {
        if (input == null)
        {
            throw new InvalidOperationException("入力情報を取得できませんでした。");
        }

        if (input.EmmPath.Length == 0)
        {
            throw new InvalidOperationException("入力EMMを指定してください。");
        }

        if (!File.Exists(input.EmmPath))
        {
            throw new FileNotFoundException("入力EMMが見つかりません。", input.EmmPath);
        }

        if (input.BasePmxPath.Length > 0 && !File.Exists(input.BasePmxPath))
        {
            throw new FileNotFoundException("改造前PMXが見つかりません。", input.BasePmxPath);
        }

        if (input.ModifiedPmxPath.Length == 0)
        {
            throw new InvalidOperationException("改造後PMXを指定してください。");
        }

        if (!File.Exists(input.ModifiedPmxPath))
        {
            throw new FileNotFoundException("改造後PMXが見つかりません。", input.ModifiedPmxPath);
        }

        if (input.OutputEmmPath.Length == 0)
        {
            throw new InvalidOperationException("出力EMMを指定してください。");
        }

        if (input.PmmPath.Length > 0 && !File.Exists(input.PmmPath))
        {
            throw new FileNotFoundException("入力PMMが見つかりません。", input.PmmPath);
        }

        if (input.PmmPath.Length > 0 && input.OutputPmmPath.Length == 0)
        {
            throw new InvalidOperationException("入力PMMを指定した場合は出力PMMも指定してください。");
        }
    }

    private static string BuildSummary(RemapExecutionResult result)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("EMM の再マップが完了しました。");
        builder.AppendLine();
        if (result.Input.BasePmxPath.Length > 0)
        {
            builder.AppendLine("改造前PMX指定: あり");
            builder.AppendLine("改造前PMX: " + result.Input.BasePmxPath);
        }
        else
        {
            builder.AppendLine("改造前PMX指定: なし (現在ロード中モデルを使用)");
        }
        builder.AppendLine();
        builder.AppendLine("対象モデルキー: " + result.ObjectKey);
        builder.AppendLine("EMM 内の元モデル: " + result.SourceObjectPath);
        builder.AppendLine("出力後モデルパス: " + result.OutputObjectPath);
        builder.AppendLine("改造前材質数: " + result.BaseMaterialNames.Count);
        builder.AppendLine("改造後材質数: " + result.ModifiedMaterialNames.Count);
        builder.AppendLine("材質名一致で対応付けできた数: " + result.RemappedMaterialCount);
        builder.AppendLine();
        builder.AppendLine("EMM 反映結果:");
        builder.AppendLine("- 材質別エフェクト行の再配置: " + result.RemapStats.MaterialEffectLineCount);
        builder.AppendLine("- 材質別表示行の再配置: " + result.RemapStats.MaterialShowLineCount);
        builder.AppendLine("- 対応先がなく削除された材質別エントリ: " + result.RemapStats.DroppedMaterialLineCount);
        builder.AppendLine();
        builder.AppendLine("出力先:");
        builder.AppendLine(result.Input.OutputEmmPath);

        if (result.Input.PmmPath.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("PMM 出力:");
            builder.AppendLine(result.Input.OutputPmmPath);
            builder.AppendLine("PMM 更新結果: " + result.PmmUpdateMessage);
        }

        if (result.UnmatchedBaseMaterials.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("改造前にのみ存在する材質:");
            AppendMaterialList(builder, result.UnmatchedBaseMaterials);
        }

        if (result.UnmatchedModifiedMaterials.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("改造後にのみ存在する材質:");
            AppendMaterialList(builder, result.UnmatchedModifiedMaterials);
        }

        builder.AppendLine();
        builder.AppendLine("現在の対応ロジックは、材質名の完全一致 + 同名材質の出現順一致です。");
        return builder.ToString();
    }

    private static void AppendMaterialList(StringBuilder builder, IList<MaterialNameEntry> entries)
    {
        int count = Math.Min(entries.Count, 12);
        for (int index = 0; index < count; index++)
        {
            MaterialNameEntry entry = entries[index];
            builder.AppendLine("- [" + entry.Index.ToString() + "] " + entry.Name);
        }

        if (entries.Count > count)
        {
            builder.AppendLine("- ... 他 " + (entries.Count - count).ToString() + " 件");
        }
    }

    private static string SafeString(string value)
    {
        return value != null ? value : string.Empty;
    }
}

internal sealed class PluginInput
{
    public string BasePmxPath = string.Empty;
    public string EmmPath = string.Empty;
    public string ModifiedPmxPath = string.Empty;
    public string OutputEmmPath = string.Empty;
    public string PmmPath = string.Empty;
    public string OutputPmmPath = string.Empty;
    public bool KeepOriginalModelPath = false;
}

internal sealed class RemapExecutionResult
{
    public PluginInput Input;
    public string ObjectKey = string.Empty;
    public string SourceObjectPath = string.Empty;
    public string OutputObjectPath = string.Empty;
    public List<string> BaseMaterialNames = new List<string>();
    public List<string> ModifiedMaterialNames = new List<string>();
    public int RemappedMaterialCount;
    public List<MaterialNameEntry> UnmatchedBaseMaterials = new List<MaterialNameEntry>();
    public List<MaterialNameEntry> UnmatchedModifiedMaterials = new List<MaterialNameEntry>();
    public MaterialRemapStats RemapStats = new MaterialRemapStats();
    public bool PmmUpdated;
    public string PmmUpdateMessage = string.Empty;
}

internal sealed class EmmObjectMatch
{
    public string ObjectKey = string.Empty;
    public string ObjectPath = string.Empty;
}

internal sealed class MaterialNameEntry
{
    public int Index;
    public string Name = string.Empty;
}

internal sealed class MaterialRemapStats
{
    public int MaterialEffectLineCount;
    public int MaterialShowLineCount;
    public int DroppedMaterialLineCount;
}

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

    public static List<MaterialNameEntry> FindUnmatchedBaseMaterials(IList<string> baseMaterialNames, IDictionary<int, int> map)
    {
        List<MaterialNameEntry> result = new List<MaterialNameEntry>();
        for (int index = 0; index < baseMaterialNames.Count; index++)
        {
            if (map.ContainsKey(index))
            {
                continue;
            }

            result.Add(new MaterialNameEntry { Index = index, Name = SafeDisplayName(baseMaterialNames[index]) });
        }

        return result;
    }

    public static List<MaterialNameEntry> FindUnmatchedModifiedMaterials(IList<string> modifiedMaterialNames, IDictionary<int, int> map)
    {
        HashSet<int> usedTargetIndices = new HashSet<int>(map.Values);
        List<MaterialNameEntry> result = new List<MaterialNameEntry>();

        for (int index = 0; index < modifiedMaterialNames.Count; index++)
        {
            if (usedTargetIndices.Contains(index))
            {
                continue;
            }

            result.Add(new MaterialNameEntry { Index = index, Name = SafeDisplayName(modifiedMaterialNames[index]) });
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
        DetectedTextFile textFile = TextFileHelper.ReadAllText(path);
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

    public EmmObjectMatch FindObjectByMaterialNames(IList<string> currentMaterialNames)
    {
        EmmSection objectSection = GetSection("Object");
        if (objectSection == null)
        {
            throw new InvalidOperationException("EMM に [Object] セクションがありません。");
        }

        List<EmmObjectMatch> exactMatches = new List<EmmObjectMatch>();

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
                EmmObjectMatch match = new EmmObjectMatch();
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
            throw new InvalidOperationException("EMM 内で現在ロード中モデルに一致するオブジェクトキーが複数見つかりました。改造前PMX(任意)を指定して対象を明示してください。");
        }

        throw new InvalidOperationException("EMM 内で現在ロード中モデルに対応するオブジェクトキーを見つけられませんでした。現在の材質構成と一致する PMX が [Object] セクションにあるか確認してください。");
    }

    public EmmObjectMatch FindObjectByPath(string modelPath)
    {
        EmmSection objectSection = GetSection("Object");
        if (objectSection == null)
        {
            throw new InvalidOperationException("EMM に [Object] セクションがありません。");
        }

        string normalizedTargetPath = PathHelper.NormalizePath(modelPath);
        List<EmmObjectMatch> exactMatches = new List<EmmObjectMatch>();
        List<EmmObjectMatch> fileNameMatches = new List<EmmObjectMatch>();
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

            EmmObjectMatch match = new EmmObjectMatch();
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
            throw new InvalidOperationException("EMM 内で改造前PMXに一致するオブジェクトキーが複数見つかりました。");
        }

        if (fileNameMatches.Count == 1)
        {
            return fileNameMatches[0];
        }

        if (fileNameMatches.Count > 1)
        {
            throw new InvalidOperationException("EMM 内で同名PMXが複数見つかりました。改造前PMXの絶対パス一致で特定できるように EMM 内容を確認してください。");
        }

        throw new InvalidOperationException("EMM 内で指定した改造前PMXに対応するオブジェクトキーを見つけられませんでした。");
    }

    public void UpdateObjectPath(string objectKey, string newPath)
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

    public MaterialRemapStats RemapMaterialAssignments(string objectKey, IDictionary<int, int> materialMap, int targetMaterialCount)
    {
        MaterialRemapStats totalStats = new MaterialRemapStats();

        for (int sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
        {
            EmmSection section = _sections[sectionIndex];
            if (!section.Name.StartsWith("Effect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MaterialRemapStats sectionStats = RemapSectionMaterialAssignments(section, objectKey, materialMap, targetMaterialCount);
            totalStats.MaterialEffectLineCount += sectionStats.MaterialEffectLineCount;
            totalStats.MaterialShowLineCount += sectionStats.MaterialShowLineCount;
            totalStats.DroppedMaterialLineCount += sectionStats.DroppedMaterialLineCount;
        }

        return totalStats;
    }

    private static MaterialRemapStats RemapSectionMaterialAssignments(EmmSection section, string objectKey, IDictionary<int, int> materialMap, int targetMaterialCount)
    {
        MaterialRemapStats stats = new MaterialRemapStats();
        Dictionary<int, string> effectLines = new Dictionary<int, string>();
        Dictionary<int, string> showLines = new Dictionary<int, string>();
        List<string> remainingLines = new List<string>();

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

    private static List<string> BuildMaterialLines(string objectKey, IDictionary<int, string> effectLines, IDictionary<int, string> showLines, MaterialRemapStats stats)
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

internal static class PathHelper
{
    public static bool HasPmxExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".pmx", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        string value = path.Trim().Trim('"').Replace('/', '\\');
        try
        {
            value = Path.GetFullPath(value);
        }
        catch
        {
        }

        return value;
    }
}

internal sealed class PmmRewriteResult
{
    public bool Updated;
    public string Message = string.Empty;
}

internal static class PmmPathRewriter
{
    private static readonly Encoding PmmEncoding = Encoding.GetEncoding(932);

    public static PmmRewriteResult TryRewriteModelPath(string inputPath, string outputPath, string sourcePath, string targetPath)
    {
        PmmRewriteResult result = new PmmRewriteResult();

        string normalizedSourcePath = PathHelper.NormalizePath(sourcePath);
        string normalizedTargetPath = PathHelper.NormalizePath(targetPath);
        if (normalizedSourcePath.Length == 0 || normalizedTargetPath.Length == 0)
        {
            result.Message = "PMM の対象パスを確定できませんでした。";
            return result;
        }

        byte[] bytes = File.ReadAllBytes(inputPath);
        byte[] sourceBytes = PmmEncoding.GetBytes(normalizedSourcePath);
        byte[] targetBytes = PmmEncoding.GetBytes(normalizedTargetPath);
        List<int> candidateOffsets = FindNullTerminatedMatches(bytes, sourceBytes);

        if (candidateOffsets.Count == 0)
        {
            result.Message = "PMM 内で対象モデルパスを見つけられませんでした。";
            return result;
        }

        if (candidateOffsets.Count > 1)
        {
            result.Message = "PMM 内で対象モデルパス候補が複数見つかったため、安全のため更新を中止しました。";
            return result;
        }

        int offset = candidateOffsets[0];
        int fieldLength = MeasureNullPaddedFieldLength(bytes, offset, sourceBytes.Length);
        if (targetBytes.Length >= fieldLength)
        {
            result.Message = "改造後PMXパスが PMM 内の空き領域に収まらないため、PMM 更新を中止しました。";
            return result;
        }

        Array.Clear(bytes, offset, fieldLength);
        Array.Copy(targetBytes, 0, bytes, offset, targetBytes.Length);

        EnsureOutputDirectory(outputPath);
        File.WriteAllBytes(outputPath, bytes);
        result.Updated = true;
        result.Message = "対象モデルパスを更新して PMM を出力しました。";
        return result;
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static List<int> FindNullTerminatedMatches(byte[] bytes, byte[] pattern)
    {
        List<int> offsets = new List<int>();
        if (bytes == null || pattern == null || pattern.Length == 0 || bytes.Length < pattern.Length + 1)
        {
            return offsets;
        }

        for (int index = 0; index <= bytes.Length - pattern.Length - 1; index++)
        {
            if (!IsMatch(bytes, index, pattern))
            {
                continue;
            }

            if (bytes[index + pattern.Length] != 0)
            {
                continue;
            }

            offsets.Add(index);
        }

        return offsets;
    }

    private static int MeasureNullPaddedFieldLength(byte[] bytes, int offset, int contentLength)
    {
        int index = offset + contentLength;
        while (index < bytes.Length && bytes[index] == 0)
        {
            index++;
        }

        return index - offset;
    }

    private static bool IsMatch(byte[] bytes, int offset, byte[] pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
        {
            if (bytes[offset + index] != pattern[index])
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class DetectedTextFile
{
    public string Text = string.Empty;
    public Encoding Encoding = Encoding.UTF8;
}

internal static class TextFileHelper
{
    public static DetectedTextFile ReadAllText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectEncoding(bytes);
        int preambleLength = GetPreambleLength(bytes, encoding);

        DetectedTextFile file = new DetectedTextFile();
        file.Encoding = encoding;
        file.Text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return file;
    }

    public static void WriteAllText(string path, string text, Encoding encoding)
    {
        File.WriteAllText(path, text, encoding);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (HasPrefix(bytes, Encoding.UTF8.GetPreamble()))
        {
            return new UTF8Encoding(true);
        }

        if (HasPrefix(bytes, Encoding.Unicode.GetPreamble()))
        {
            return Encoding.Unicode;
        }

        if (HasPrefix(bytes, Encoding.BigEndianUnicode.GetPreamble()))
        {
            return Encoding.BigEndianUnicode;
        }

        return Encoding.GetEncoding(932);
    }

    private static int GetPreambleLength(byte[] bytes, Encoding encoding)
    {
        byte[] preamble = encoding.GetPreamble();
        return HasPrefix(bytes, preamble) ? preamble.Length : 0;
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix)
    {
        if (prefix == null || prefix.Length == 0 || bytes.Length < prefix.Length)
        {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }
}

internal static class PmxMaterialReader
{
    public static List<string> ReadMaterialNames(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            string signature = new string(reader.ReadChars(4));
            if (signature != "PMX ")
            {
                throw new InvalidDataException("PMX ファイルではありません: " + path);
            }

            reader.ReadSingle();
            byte settingCount = reader.ReadByte();
            byte[] settings = reader.ReadBytes(settingCount);
            if (settings.Length < 8)
            {
                throw new InvalidDataException("PMX ヘッダの設定情報が不足しています: " + path);
            }

            Encoding textEncoding = settings[0] == 0 ? Encoding.Unicode : new UTF8Encoding(false);
            int additionalUvCount = settings[1];
            int vertexIndexSize = settings[2];
            int textureIndexSize = settings[3];
            int boneIndexSize = settings[5];

            ReadText(reader, textEncoding);
            ReadText(reader, textEncoding);
            ReadText(reader, textEncoding);
            ReadText(reader, textEncoding);

            int vertexCount = reader.ReadInt32();
            for (int index = 0; index < vertexCount; index++)
            {
                SkipVertex(reader, additionalUvCount, boneIndexSize);
            }

            int faceIndexCount = reader.ReadInt32();
            Skip(reader, faceIndexCount * vertexIndexSize);

            int textureCount = reader.ReadInt32();
            for (int index = 0; index < textureCount; index++)
            {
                ReadText(reader, textEncoding);
            }

            int materialCount = reader.ReadInt32();
            List<string> materialNames = new List<string>();
            for (int index = 0; index < materialCount; index++)
            {
                string materialName = ReadText(reader, textEncoding);
                materialNames.Add(materialName);

                ReadText(reader, textEncoding);
                Skip(reader, 16);
                Skip(reader, 12);
                Skip(reader, 4);
                Skip(reader, 12);
                Skip(reader, 1);
                Skip(reader, 16);
                Skip(reader, 4);
                SkipIndex(reader, textureIndexSize);
                SkipIndex(reader, textureIndexSize);
                Skip(reader, 1);

                byte toonFlag = reader.ReadByte();
                if (toonFlag == 0)
                {
                    SkipIndex(reader, textureIndexSize);
                }
                else
                {
                    Skip(reader, 1);
                }

                ReadText(reader, textEncoding);
                Skip(reader, 4);
            }

            return materialNames;
        }
    }

    private static void SkipVertex(BinaryReader reader, int additionalUvCount, int boneIndexSize)
    {
        Skip(reader, 12);
        Skip(reader, 12);
        Skip(reader, 8);
        Skip(reader, additionalUvCount * 16);

        byte deformType = reader.ReadByte();
        switch (deformType)
        {
            case 0:
                SkipIndex(reader, boneIndexSize);
                break;
            case 1:
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                Skip(reader, 4);
                break;
            case 2:
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                Skip(reader, 16);
                break;
            case 3:
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                Skip(reader, 4);
                Skip(reader, 36);
                break;
            case 4:
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                SkipIndex(reader, boneIndexSize);
                Skip(reader, 16);
                break;
            default:
                throw new InvalidDataException("未対応の頂点ウェイト形式です: " + deformType.ToString());
        }

        Skip(reader, 4);
    }

    private static string ReadText(BinaryReader reader, Encoding encoding)
    {
        int byteCount = reader.ReadInt32();
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = reader.ReadBytes(byteCount);
        return encoding.GetString(bytes);
    }

    private static void SkipIndex(BinaryReader reader, int size)
    {
        Skip(reader, size);
    }

    private static void Skip(BinaryReader reader, int count)
    {
        if (count <= 0)
        {
            return;
        }

        byte[] skipped = reader.ReadBytes(count);
        if (skipped.Length != count)
        {
            throw new EndOfStreamException("PMX の読み込み中に予期せずファイル終端に到達しました。");
        }
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox _basePmxTextBox;
    private readonly TextBox _emmTextBox;
    private readonly TextBox _modifiedPmxTextBox;
    private readonly TextBox _outputEmmTextBox;
    private readonly TextBox _pmmTextBox;
    private readonly TextBox _outputPmmTextBox;
    private readonly CheckBox _keepOriginalModelPathCheckBox;

    public MainForm(string currentModelHint)
    {
        Text = "MME EMM再マップ";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 444);

        Font = SystemFonts.MessageBoxFont;

        Label descriptionLabel = new Label();
        descriptionLabel.AutoSize = false;
        descriptionLabel.Location = new Point(12, 12);
        descriptionLabel.Size = new Size(696, 44);
        descriptionLabel.Text = "改造前PMX(任意)が未指定なら現在ロード中モデルを改造前として使用し、指定時はそのPMXを優先します。"
            + Environment.NewLine
            + "各入力欄はファイルのドラッグ＆ドロップにも対応しています。";
        Controls.Add(descriptionLabel);

        Label currentModelLabel = new Label();
        currentModelLabel.AutoSize = false;
        currentModelLabel.Location = new Point(12, 60);
        currentModelLabel.Size = new Size(696, 20);
        currentModelLabel.Text = "現在モデル: " + currentModelHint;
        Controls.Add(currentModelLabel);

        _basePmxTextBox = AddPathRow("改造前PMX(任意)", 108, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _emmTextBox = AddPathRow("入力EMM", 154, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");
        _modifiedPmxTextBox = AddPathRow("改造後PMX", 200, "PMX Files (*.pmx)|*.pmx|All Files (*.*)|*.*");
        _outputEmmTextBox = AddSavePathRow("出力EMM", 246, "EMM Files (*.emm)|*.emm|All Files (*.*)|*.*");
        _pmmTextBox = AddPathRow("入力PMM(任意)", 292, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");
        _outputPmmTextBox = AddSavePathRow("出力PMM(任意)", 338, "PMM Files (*.pmm)|*.pmm|All Files (*.*)|*.*");
        _keepOriginalModelPathCheckBox = new CheckBox();
        _keepOriginalModelPathCheckBox.Text = "出力内のモデルパスを変更しない";
        _keepOriginalModelPathCheckBox.Location = new Point(24, 380);
        _keepOriginalModelPathCheckBox.Size = new Size(680, 24);
        Controls.Add(_keepOriginalModelPathCheckBox);

        Button runButton = new Button();
        runButton.Text = "実行";
        runButton.Location = new Point(552, 408);
        runButton.Size = new Size(75, 28);
        runButton.Click += delegate
        {
            string validationMessage = ValidateForDialog();
            if (validationMessage.Length > 0)
            {
                MessageBox.Show(validationMessage, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(runButton);

        Button cancelButton = new Button();
        cancelButton.Text = "キャンセル";
        cancelButton.Location = new Point(633, 408);
        cancelButton.Size = new Size(75, 28);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        AcceptButton = runButton;
        CancelButton = cancelButton;

        _emmTextBox.TextChanged += delegate
        {
            if (_outputEmmTextBox.Text.Trim().Length > 0)
            {
                return;
            }

            string emmPath = _emmTextBox.Text.Trim();
            if (emmPath.Length == 0)
            {
                return;
            }

            string directory = Path.GetDirectoryName(emmPath);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(emmPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(nameWithoutExtension))
            {
                return;
            }

            _outputEmmTextBox.Text = Path.Combine(directory, nameWithoutExtension + "_remapped.emm");
        };

        _pmmTextBox.TextChanged += delegate
        {
            if (_outputPmmTextBox.Text.Trim().Length > 0)
            {
                return;
            }

            string pmmPath = _pmmTextBox.Text.Trim();
            if (pmmPath.Length == 0)
            {
                return;
            }

            string directory = Path.GetDirectoryName(pmmPath);
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(pmmPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(nameWithoutExtension))
            {
                return;
            }

            _outputPmmTextBox.Text = Path.Combine(directory, nameWithoutExtension + "_remapped.pmm");
        };
    }

    public PluginInput BuildInput()
    {
        PluginInput input = new PluginInput();
        input.BasePmxPath = _basePmxTextBox.Text.Trim();
        input.EmmPath = _emmTextBox.Text.Trim();
        input.ModifiedPmxPath = _modifiedPmxTextBox.Text.Trim();
        input.OutputEmmPath = _outputEmmTextBox.Text.Trim();
        input.PmmPath = _pmmTextBox.Text.Trim();
        input.OutputPmmPath = _outputPmmTextBox.Text.Trim();
        input.KeepOriginalModelPath = _keepOriginalModelPathCheckBox.Checked;
        return input;
    }

    private string ValidateForDialog()
    {
        string basePmxPath = _basePmxTextBox.Text.Trim();
        if (basePmxPath.Length > 0 && !File.Exists(basePmxPath))
        {
            return "改造前PMXが見つかりません。";
        }

        string emmPath = _emmTextBox.Text.Trim();
        if (emmPath.Length == 0)
        {
            return "入力EMMを指定してください。";
        }

        if (!File.Exists(emmPath))
        {
            return "入力EMMが見つかりません。";
        }

        string modifiedPmxPath = _modifiedPmxTextBox.Text.Trim();
        if (modifiedPmxPath.Length == 0)
        {
            return "改造後PMXを指定してください。";
        }

        if (!File.Exists(modifiedPmxPath))
        {
            return "改造後PMXが見つかりません。";
        }

        if (_outputEmmTextBox.Text.Trim().Length == 0)
        {
            return "出力EMMを指定してください。";
        }

        string pmmPath = _pmmTextBox.Text.Trim();
        string outputPmmPath = _outputPmmTextBox.Text.Trim();
        if (pmmPath.Length > 0 && !File.Exists(pmmPath))
        {
            return "入力PMMが見つかりません。";
        }

        if (pmmPath.Length > 0 && outputPmmPath.Length == 0)
        {
            return "入力PMMを指定した場合は出力PMMも指定してください。";
        }

        return string.Empty;
    }

    private TextBox AddPathRow(string labelText, int top, string filter)
    {
        return AddPathRowCore(labelText, top, filter, false);
    }

    private TextBox AddSavePathRow(string labelText, int top, string filter)
    {
        return AddPathRowCore(labelText, top, filter, true);
    }

    private TextBox AddPathRowCore(string labelText, int top, string filter, bool saveMode)
    {
        Label label = new Label();
        label.AutoSize = true;
        label.Location = new Point(12, top + 6);
        label.Text = labelText;
        Controls.Add(label);

        TextBox textBox = new TextBox();
        textBox.Location = new Point(136, top);
        textBox.Size = new Size(492, 23);
        textBox.AllowDrop = true;
        textBox.DragEnter += delegate (object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        };
        textBox.DragDrop += delegate (object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0)
            {
                return;
            }

            string path = dropped[0];
            if (!saveMode && Directory.Exists(path))
            {
                return;
            }

            textBox.Text = path;
        };
        Controls.Add(textBox);

        Button button = new Button();
        button.Text = "参照...";
        button.Location = new Point(634, top - 1);
        button.Size = new Size(74, 26);
        button.Click += delegate
        {
            if (saveMode)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = filter;
                    dialog.OverwritePrompt = true;
                    dialog.AddExtension = true;
                    if (textBox.Text.Trim().Length > 0)
                    {
                        dialog.FileName = Path.GetFileName(textBox.Text.Trim());
                        string directory = Path.GetDirectoryName(textBox.Text.Trim());
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }
            }
            else
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = filter;
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    if (textBox.Text.Trim().Length > 0)
                    {
                        dialog.FileName = Path.GetFileName(textBox.Text.Trim());
                        string directory = Path.GetDirectoryName(textBox.Text.Trim());
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            dialog.InitialDirectory = directory;
                        }
                    }

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        textBox.Text = dialog.FileName;
                    }
                }
            }
        };
        Controls.Add(button);

        return textBox;
    }
}
