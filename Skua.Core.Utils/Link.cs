using System.Diagnostics;

namespace Skua.Core.Utils;

public class Link
{
    public static void OpenBrowser(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return;

        ProcessStartInfo ps = new("xdg-open")
        {
            UseShellExecute = false
        };
        ps.ArgumentList.Add(link);
        Process.Start(ps);
    }
}
