using PEPlugin;
using PEPlugin.Pmx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

// Plugin entry point and end-to-end remap orchestration.
// PEPlugin script host discovers this exact class name, so we keep it stable.
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

            using (EmmRemapForm form = new EmmRemapForm(BuildCurrentModelPathHint(pmx)))
            {
                if (form.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                RemapRequest input = form.BuildRemapRequest();
                ValidateRemapRequest(input);

                RemapReport result = RunRemap(input, currentMaterialNames);
                MessageBox.Show(BuildRemapSummary(result), "MME EMM再マップ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }
    }

    private static RemapReport RunRemap(RemapRequest input, IList<string> currentMaterialNames)
    {
        // 1) Resolve the target object in EMM and collect source/target material lists.
        EmmDocument document = EmmDocument.Load(input.EmmPath);
        List<string> baseMaterialNames;
        EmmObjectReference objectMatch;

        if (input.BasePmxPath.Length > 0)
        {
            baseMaterialNames = PmxMaterialReader.ReadMaterialNames(input.BasePmxPath);
            objectMatch = document.ResolveObjectReferenceByPath(input.BasePmxPath);
        }
        else
        {
            baseMaterialNames = new List<string>(currentMaterialNames);
            objectMatch = document.ResolveObjectReferenceByMaterialNames(baseMaterialNames);
        }

        List<string> modifiedMaterialNames = PmxMaterialReader.ReadMaterialNames(input.ModifiedPmxPath);
        string objectKey = objectMatch.ObjectKey;
        string outputObjectPath = input.KeepOriginalModelPath ? objectMatch.ObjectPath : input.ModifiedPmxPath;

        // 2) Remap material-scoped entries by name + occurrence order.
        Dictionary<int, int> materialMap = MaterialIndexMapper.BuildMap(baseMaterialNames, modifiedMaterialNames);
        if (!input.KeepOriginalModelPath)
        {
            document.SetObjectPath(objectKey, input.ModifiedPmxPath);
        }

        MaterialRemapMetrics remapStats = document.RemapMaterialEntries(objectKey, materialMap, modifiedMaterialNames.Count);

        EnsureOutputDirectory(input.OutputEmmPath);
        document.Save(input.OutputEmmPath);

        RemapReport result = new RemapReport();
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

        // 3) PMM update is optional and uses a conservative rewrite strategy.
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
                result.PmmUpdateMessage = "ベースPMMをそのまま出力しました。";
            }
            else
            {
                PmmRewriteOutcome pmmResult = PmmPathRewriter.TryRewriteModelPath(input.PmmPath, input.OutputPmmPath, objectMatch.ObjectPath, input.ModifiedPmxPath);
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

    private static void ValidateRemapRequest(RemapRequest input)
    {
        if (input == null)
        {
            throw new InvalidOperationException("入力情報を取得できませんでした。");
        }

        if (input.EmmPath.Length == 0)
        {
            throw new InvalidOperationException("ベースEMMを指定してください。");
        }

        if (!File.Exists(input.EmmPath))
        {
            throw new FileNotFoundException("ベースEMMが見つかりません。", input.EmmPath);
        }

        if (input.BasePmxPath.Length > 0 && !File.Exists(input.BasePmxPath))
        {
            throw new FileNotFoundException("ベースPMXが見つかりません。", input.BasePmxPath);
        }

        if (input.ModifiedPmxPath.Length == 0)
        {
            throw new InvalidOperationException("更新後PMXを指定してください。");
        }

        if (!File.Exists(input.ModifiedPmxPath))
        {
            throw new FileNotFoundException("更新後PMXが見つかりません。", input.ModifiedPmxPath);
        }

        if (input.OutputEmmPath.Length == 0)
        {
            throw new InvalidOperationException("出力EMMを指定してください。");
        }

        if (input.PmmPath.Length > 0 && !File.Exists(input.PmmPath))
        {
            throw new FileNotFoundException("ベースPMMが見つかりません。", input.PmmPath);
        }

        if (input.PmmPath.Length > 0 && input.OutputPmmPath.Length == 0)
        {
            throw new InvalidOperationException("ベースPMMを指定した場合は出力PMMも指定してください。");
        }
    }

    private static string BuildRemapSummary(RemapReport result)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("EMM の再マップが完了しました。");
        builder.AppendLine();
        if (result.Input.BasePmxPath.Length > 0)
        {
            builder.AppendLine("ベースPMX指定: あり");
            builder.AppendLine("ベースPMX: " + result.Input.BasePmxPath);
        }
        else
        {
            builder.AppendLine("ベースPMX指定: なし (現在ロード中モデルを使用)");
        }
        builder.AppendLine();
        builder.AppendLine("対象モデルキー: " + result.ObjectKey);
        builder.AppendLine("EMM 内の元モデル: " + result.SourceObjectPath);
        builder.AppendLine("出力後モデルパス: " + result.OutputObjectPath);
        builder.AppendLine("ベース材質数: " + result.BaseMaterialNames.Count);
        builder.AppendLine("更新後材質数: " + result.ModifiedMaterialNames.Count);
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
            builder.AppendLine("ベース側にのみ存在する材質:");
            AppendMaterialList(builder, result.UnmatchedBaseMaterials);
        }

        if (result.UnmatchedModifiedMaterials.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("更新後側にのみ存在する材質:");
            AppendMaterialList(builder, result.UnmatchedModifiedMaterials);
        }

        builder.AppendLine();
        builder.AppendLine("現在の対応ロジックは、材質名の完全一致 + 同名材質の出現順一致です。");
        return builder.ToString();
    }

    private static void AppendMaterialList(StringBuilder builder, IList<IndexedMaterialName> entries)
    {
        int count = Math.Min(entries.Count, 12);
        for (int index = 0; index < count; index++)
        {
            IndexedMaterialName entry = entries[index];
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
