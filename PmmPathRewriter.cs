using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Conservative PMM binary path rewriter with safety checks and bounded in-place patching.
internal static class PmmPathRewriter
{
    private static readonly Encoding PmmEncoding = Encoding.GetEncoding(932);

    public static PmmRewriteOutcome TryRewriteModelPath(string inputPath, string outputPath, string sourcePath, string targetPath)
    {
        PmmRewriteOutcome result = new PmmRewriteOutcome();

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

        // Conservative patch: zero-fill the whole field first, then copy the new bytes.
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
