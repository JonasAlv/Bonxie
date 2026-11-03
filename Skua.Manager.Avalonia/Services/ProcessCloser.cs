using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Manager.Avalonia.Services;

/// <summary>
/// Stops client processes by sending SIGTERM first so they run their graceful shutdown,
/// falling back to SIGKILL after the timeout.
/// </summary>
internal static class ProcessCloser
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private const int SIGTERM = 15;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    public static bool RequestClose(Process process)
    {
        try
        {
            if (process.HasExited)
                return false;
            return kill(process.Id, SIGTERM) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task CloseAsync(Process process, TimeSpan timeout)
    {
        if (process.HasExited)
            return;

        if (RequestClose(process))
        {
            using CancellationTokenSource cts = new(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return;
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine($"Process {process.Id} did not close within {timeout.TotalSeconds:0.#}s, killing it.");
            }
        }

        if (!process.HasExited)
        {
            process.Kill();
            await process.WaitForExitAsync();
        }
    }
}
