using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SyncDroidAndroid.Helpers;

public static class FileNameLengthHelper
{
    public const int MaxUtf8Bytes = 254; // Strictly < 255 bytes

    private static readonly char[] WindowsInvalidFileChars = new[]
    {
        ':', '*', '?', '"', '<', '>', '|'
    };

    public static bool IsFileNameTooLong(string filePathOrName, out int byteCount)
    {
        string fileName = Path.GetFileName(filePathOrName.Replace('\\', '/'));
        byteCount = Encoding.UTF8.GetByteCount(fileName);
        return byteCount > 255;
    }

    /// <summary>
    /// Truncates a filename from the end of its stem (preserving extension and Unicode grapheme clusters)
    /// until its UTF-8 byte count is strictly under 255 bytes (&lt;= 254).
    /// </summary>
    public static string TruncateFileNameStrictlyUnder255Bytes(
        string originalFileName,
        Func<string, bool>? isNameTakenInDir = null,
        int maxBytes = MaxUtf8Bytes)
    {
        string ext = Path.GetExtension(originalFileName);
        string stem = Path.GetFileNameWithoutExtension(originalFileName);
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();

        stem = new string(stem.Where(c => !invalidChars.Contains(c)).ToArray());

        int extBytes = Encoding.UTF8.GetByteCount(ext);
        if (extBytes >= maxBytes)
        {
            ext = "";
            extBytes = 0;
        }

        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(stem);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        while (elements.Count > 0)
        {
            string currentStem = string.Concat(elements).TrimEnd(' ', '.');
            string candidate = currentStem + ext;

            if (!string.IsNullOrEmpty(currentStem) &&
                Encoding.UTF8.GetByteCount(candidate) <= maxBytes)
            {
                if (isNameTakenInDir == null || !isNameTakenInDir(candidate))
                {
                    return candidate;
                }

                int suffixIndex = 1;
                while (true)
                {
                    string suffix = $"_{suffixIndex}";
                    int maxStemBytes = maxBytes - extBytes - Encoding.UTF8.GetByteCount(suffix);
                    while (elements.Count > 0 &&
                           Encoding.UTF8.GetByteCount(string.Concat(elements).TrimEnd(' ', '.')) > maxStemBytes)
                    {
                        elements.RemoveAt(elements.Count - 1);
                    }

                    string baseStem = string.Concat(elements).TrimEnd(' ', '.');
                    if (string.IsNullOrEmpty(baseStem))
                    {
                        baseStem = "file";
                    }

                    string numberedCandidate = baseStem + suffix + ext;
                    if (!isNameTakenInDir(numberedCandidate))
                    {
                        return numberedCandidate;
                    }
                    suffixIndex++;
                }
            }

            elements.RemoveAt(elements.Count - 1);
        }

        return "renamed_file" + ext;
    }

    /// <summary>
    /// Sanitizes relative path segments when transferring from Android (Linux) to Windows PC
    /// so characters like ':', '*', '?', '"', '&lt;', '&gt;', '|' do not cause NTFS IOException.
    /// </summary>
    public static string SanitizeRelativePathForWindows(string relativePath, out bool wasModified)
    {
        wasModified = false;
        if (string.IsNullOrEmpty(relativePath)) return relativePath;

        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string segment = parts[i];
            var sb = new StringBuilder(segment.Length);
            foreach (char c in segment)
            {
                if (Array.IndexOf(WindowsInvalidFileChars, c) >= 0)
                {
                    sb.Append('_');
                    wasModified = true;
                }
                else
                {
                    sb.Append(c);
                }
            }

            string cleaned = sb.ToString().TrimEnd(' ', '.');
            if (string.IsNullOrEmpty(cleaned))
            {
                cleaned = "_";
                wasModified = true;
            }
            parts[i] = cleaned;
        }

        return string.Join('/', parts);
    }
}
