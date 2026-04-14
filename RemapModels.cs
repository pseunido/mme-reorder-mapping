using System.Collections.Generic;
using System.Text;

// Shared request/result/data models used across UI, remap flow, and file processors.
internal sealed class RemapRequest
{
    public string BasePmxPath = string.Empty;
    public string EmmPath = string.Empty;
    public string ModifiedPmxPath = string.Empty;
    public string OutputEmmPath = string.Empty;
    public string PmmPath = string.Empty;
    public string OutputPmmPath = string.Empty;
    public bool KeepOriginalModelPath = false;
}

internal sealed class RemapReport
{
    public RemapRequest Input;
    public string ObjectKey = string.Empty;
    public string SourceObjectPath = string.Empty;
    public string OutputObjectPath = string.Empty;
    public List<string> BaseMaterialNames = new List<string>();
    public List<string> ModifiedMaterialNames = new List<string>();
    public int RemappedMaterialCount;
    public List<IndexedMaterialName> UnmatchedBaseMaterials = new List<IndexedMaterialName>();
    public List<IndexedMaterialName> UnmatchedModifiedMaterials = new List<IndexedMaterialName>();
    public MaterialRemapMetrics RemapStats = new MaterialRemapMetrics();
    public bool PmmUpdated;
    public string PmmUpdateMessage = string.Empty;
}

internal sealed class EmmObjectReference
{
    public string ObjectKey = string.Empty;
    public string ObjectPath = string.Empty;
}

internal sealed class IndexedMaterialName
{
    public int Index;
    public string Name = string.Empty;
}

internal sealed class MaterialRemapMetrics
{
    public int MaterialEffectLineCount;
    public int MaterialShowLineCount;
    public int DroppedMaterialLineCount;
}

internal sealed class PmmRewriteOutcome
{
    public bool Updated;
    public string Message = string.Empty;
}

internal sealed class TextFileContent
{
    public string Text = string.Empty;
    public Encoding Encoding = Encoding.UTF8;
}
