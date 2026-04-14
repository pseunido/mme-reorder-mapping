using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Lightweight PMX binary reader that extracts material names required for remapping.
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
