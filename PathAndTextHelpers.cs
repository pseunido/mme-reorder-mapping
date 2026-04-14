using System;
using System.IO;
using System.Text;

// Shared path normalization and text encoding detection helpers for EMM/PMM I/O.
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

internal static class TextFileHelper
{
    public static TextFileContent ReadAllText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectEncoding(bytes);
        int preambleLength = GetPreambleLength(bytes, encoding);

        TextFileContent file = new TextFileContent();
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
