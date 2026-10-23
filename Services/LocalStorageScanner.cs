using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SyncDroidAndroid.Models;

namespace SyncDroidAndroid.Services;

public static class LocalStorageScanner
{
    private static readonly HashSet<string> DangerousRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/storage",
        "/storage/emulated",
        "/storage/emulated/0",
        "/sdcard",
        @"C:\",
        @"D:\",
        @"E:\"
    };

    public static bool IsDangerousRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(normalized)) return true;
        return DangerousRoots.Contains(normalized) || DangerousRoots.Contains(normalized + "\\");
    }

    public static DirectoryListingResponse GetDirectoryListing(string? rawPath, bool includeFiles)
    {
        string normalized = string.IsNullOrWhiteSpace(rawPath)
            ? "/storage/emulated/0"
            : rawPath.Trim().Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(normalized)) normalized = "/storage";

        var entries = new List<RemoteEntryDto>();
        var addedDirs = new HashSet<string>(StringComparer.Ordinal);

        // Android 11+ restricts readdir on /storage and /storage/emulated directly
        if (normalized == "/storage")
        {
            addedDirs.Add("emulated");
            entries.Add(new RemoteEntryDto
            {
                Name = "emulated",
                FullPath = "/storage/emulated",
                IsDirectory = true
            });
        }
        else if (normalized == "/storage/emulated")
        {
            addedDirs.Add("0");
            entries.Add(new RemoteEntryDto
            {
                Name = "0",
                FullPath = "/storage/emulated/0",
                IsDirectory = true
            });
        }

        try
        {
            var dirInfo = new DirectoryInfo(normalized);
            if (dirInfo.Exists)
            {
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    if (subDir.Name == "." || subDir.Name == ".." || subDir.Name == "self")
                        continue;

                    if (addedDirs.Add(subDir.Name))
                    {
                        entries.Add(new RemoteEntryDto
                        {
                            Name = subDir.Name,
                            FullPath = $"{normalized}/{subDir.Name}",
                            IsDirectory = true,
                            LastWriteTimeUtcTicks = subDir.LastWriteTimeUtc.Ticks
                        });
                    }
                }

                if (includeFiles)
                {
                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        if (file.Name.StartsWith(".syncdroid_", StringComparison.Ordinal) &&
                            file.Name.EndsWith(".part", StringComparison.Ordinal))
                            continue;

                        entries.Add(new RemoteEntryDto
                        {
                            Name = file.Name,
                            FullPath = $"{normalized}/{file.Name}",
                            Size = file.Length,
                            IsDirectory = false,
                            LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore permission denied on restricted system folders
        }

        var sorted = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string parentPath = "/storage";
        int lastSlash = normalized.LastIndexOf('/');
        if (lastSlash > 0)
        {
            parentPath = normalized.Substring(0, lastSlash);
        }

        return new DirectoryListingResponse
        {
            CurrentPath = normalized,
            ParentPath = parentPath,
            Entries = sorted
        };
    }

    public static TreeManifestResponse GetDirectoryTreeManifest(string rootPath)
    {
        string normalizedRoot = rootPath.Trim().Replace('\\', '/').TrimEnd('/');
        var files = new List<RemoteEntryDto>();

        void Traverse(string currentDir)
        {
            // Skip Android 11+ kernel-restricted /Android/data and /Android/obb
            if (currentDir.EndsWith("/Android/data", StringComparison.OrdinalIgnoreCase) ||
                currentDir.EndsWith("/Android/obb", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var di = new DirectoryInfo(currentDir);
                if (!di.Exists) return;

                foreach (var fi in di.EnumerateFiles())
                {
                    if (fi.Name.StartsWith(".syncdroid_", StringComparison.Ordinal) &&
                        fi.Name.EndsWith(".part", StringComparison.Ordinal))
                        continue;

                    string fullPath = fi.FullName.Replace('\\', '/');
                    string relPath = fullPath.Substring(normalizedRoot.Length).TrimStart('/');

                    files.Add(new RemoteEntryDto
                    {
                        Name = fi.Name,
                        FullPath = fullPath,
                        RelativePath = relPath,
                        Size = fi.Length,
                        LastWriteTimeUtcTicks = fi.LastWriteTimeUtc.Ticks,
                        IsDirectory = false
                    });
                }

                foreach (var sub in di.EnumerateDirectories())
                {
                    if (sub.Name == "." || sub.Name == "..") continue;
                    Traverse(sub.FullName.Replace('\\', '/'));
                }
            }
            catch
            {
                // Ignore inaccessible subdirectories
            }
        }

        Traverse(normalizedRoot);

        return new TreeManifestResponse
        {
            RootPath = normalizedRoot,
            Files = files
        };
    }

    public static void CleanEmptySubdirectories(string rootPath)
    {
        if (IsDangerousRootPath(rootPath) || !Directory.Exists(rootPath)) return;

        void CleanRecursive(string dir)
        {
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    CleanRecursive(sub);
                }

                if (!string.Equals(dir.Replace('\\', '/').TrimEnd('/'), rootPath.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
            }
            catch { }
        }

        CleanRecursive(rootPath);
    }
}
