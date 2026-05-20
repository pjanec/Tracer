using System.Diagnostics;

namespace Tracer.OfflineViewer.Browser;

public static class BrowserLauncher
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Best effort; user can copy URL from log if it fails
        }
    }
}
