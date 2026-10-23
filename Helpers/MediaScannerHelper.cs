using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Media;

namespace SyncDroidAndroid.Helpers;

public static class MediaScannerHelper
{
    public static void NotifyFilesChanged(Context? context, IEnumerable<string> filePaths)
    {
        if (context == null) return;
        var array = filePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToArray();

        if (array.Length == 0) return;

        try
        {
            MediaScannerConnection.ScanFile(context, array, null, null);
        }
        catch
        {
            // Ignore scanner errors on restricted paths
        }
    }
}
