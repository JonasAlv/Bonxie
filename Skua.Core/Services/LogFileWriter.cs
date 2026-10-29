using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Services;

/// <summary>
/// Appends the Script and Debug logs to logs/&lt;account&gt;_&lt;yyyy-MM-dd&gt;.log in the Bonxie folder, so a run left alone
/// (overnight, on another machine) can be checked afterwards. Kept small on purpose: lines are written in batches every
/// couple of seconds, a line repeated back to back is written once with a count, a day's file rolls over to a new part
/// at 5 MB, and files older than 14 days are deleted. "Save Logs to File" (LogToFile) turns it off.
/// </summary>
internal sealed partial class LogFileWriter : IDisposable
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;
    private const int RepeatSummaryEvery = 1000;
    private const int MaxPendingChars = 1_000_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SettingRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly string _directory = Path.Combine(ClientFileSources.SkuaDIR, "logs");
    // _lock guards the pending lines and the repeat state; it is never held during I/O or a settings read.
    // _writeLock keeps batches in order: taking the pending lines and writing them happen together under it.
    // Nothing here may Trace: Debug lines come from a Trace listener, so it would loop back.
    private readonly object _lock = new();
    private readonly object _writeLock = new();
    private readonly Queue<Line> _pending = new();
    private readonly Timer _timer;
    private int _pendingChars;
    private string _account;
    private LogType _lastType;
    private string? _lastKey;
    private DateOnly _lastDay;
    private string _lastStamp = string.Empty;
    private int _repeats;
    private volatile int _enabled = -1; // -1 unknown, 0 off, 1 on
    private DateTime _enabledReadAt;
    private DateTime _nextCleanup;
    private bool _writeFailing;
    private bool _disposed;

    private readonly record struct Line(string Account, DateOnly Day, string Text);

    public LogFileWriter()
    {
        // The Manager never logs in; its lines get their own file instead of mixing with a client's.
        string? entry = Assembly.GetEntryAssembly()?.GetName().Name;
        _account = entry?.Contains("Manager", StringComparison.OrdinalIgnoreCase) == true ? "manager" : "unknown";

        _timer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
        // ProcessExit doesn't run when the process dies of an unhandled exception, and those last lines matter most.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Dispose();
    }

    /// <summary>The logged-in account; lines from then on go to its file.</summary>
    public void SetAccount(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        string account = InvalidFileNameChars().Replace(username.Trim().ToLowerInvariant(), "_");
        lock (_lock)
        {
            if (account == _account)
                return;

            // Lines already waiting keep the account they were logged under.
            AppendRepeatSummary();
            _lastKey = null;
            _account = account;
        }
    }

    /// <summary>Queues a line; called inside LogService's collection lock, so it never touches the disk (until disposed).</summary>
    public void Add(LogType type, string message)
    {
        if (_enabled == 0 || string.IsNullOrWhiteSpace(message))
            return;

        string text = message.TrimEnd();
        DateTime now = DateTime.Now;
        // Lines from CoreBots already start with [HH:mm:ss]: keep that time, and compare repeats without it.
        Match time = StartsWithTime().Match(text);
        string key = time.Success ? text[time.Length..].TrimStart() : text;
        string stamp = time.Success ? time.Value : $"[{now:HH:mm:ss}]";
        DateOnly day = DateOnly.FromDateTime(now);
        bool disposed;
        lock (_lock)
        {
            disposed = _disposed;
            if (!disposed && type == _lastType && key == _lastKey)
            {
                _lastStamp = stamp;
                _lastDay = day;
                if (++_repeats >= RepeatSummaryEvery)
                    AppendRepeatSummary();
                return;
            }

            AppendRepeatSummary();
            _lastType = type;
            _lastKey = key;
            Enqueue(new(_account, day, $"{stamp} {(type == LogType.Debug ? "[debug] " : string.Empty)}{key}"));
        }

        // After the final flush there is no timer left: write it now (the process is shutting down).
        if (disposed)
            Flush();
    }

    public void Flush()
    {
        // Read the setting on every tick, even with nothing pending, so turning it back on takes effect.
        // Outside _writeLock: the settings service takes its own lock and a cross-process mutex.
        bool enabled = IsEnabled();
        lock (_writeLock)
        {
            Line[] lines;
            lock (_lock)
            {
                if (!enabled)
                {
                    // Off: drop what waits. Unknown (settings not up yet): keep it, bounded by MaxPendingChars.
                    if (_enabled == 0)
                    {
                        _pending.Clear();
                        _pendingChars = 0;
                        _lastKey = null;
                        _repeats = 0;
                    }
                    return;
                }

                if (_pending.Count == 0)
                    return;

                lines = _pending.ToArray();
                _pending.Clear();
                _pendingChars = 0;
            }

            int written = Write(lines);
            if (written < lines.Length)
                Requeue(lines.AsSpan(written));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            // From here on Add writes its line itself, so nothing can be queued after the final flush below.
            _disposed = true;
            AppendRepeatSummary();
        }

        _timer.Dispose();
        Flush();
    }

    private void Enqueue(Line line)
    {
        _pending.Enqueue(line);
        _pendingChars += line.Text.Length;
        // Settings not readable yet, or the disk keeps failing: keep the newest lines and drop the oldest.
        while (_pendingChars > MaxPendingChars && _pending.Count > 1)
            _pendingChars -= _pending.Dequeue().Text.Length;
    }

    // A failed batch goes back in front of what was queued meanwhile, so the next tick retries it in order.
    private void Requeue(ReadOnlySpan<Line> failed)
    {
        lock (_lock)
        {
            Line[] queued = _pending.ToArray();
            _pending.Clear();
            _pendingChars = 0;
            foreach (Line line in failed)
                Enqueue(line);
            foreach (Line line in queued)
                Enqueue(line);
        }
    }

    private void AppendRepeatSummary()
    {
        if (_repeats == 0)
            return;

        // Same [debug] tag as the line it summarizes, so it isn't read as a Script line.
        Enqueue(new(_account, _lastDay, $"{_lastStamp} {(_lastType == LogType.Debug ? "[debug] " : string.Empty)}(previous line repeated {_repeats} more time{(_repeats == 1 ? string.Empty : "s")})"));
        _repeats = 0;
    }

    /// <summary>Writes the lines grouped by file; returns how many were written before a failure.</summary>
    private int Write(Line[] lines)
    {
        DeleteOldFiles();
        int written = 0;
        while (written < lines.Length)
        {
            Line first = lines[written];
            StringBuilder text = new();
            int end = written;
            for (; end < lines.Length && lines[end].Account == first.Account && lines[end].Day == first.Day; end++)
                text.Append(lines[end].Text).Append('\n');

            if (!Append(first.Account, first.Day, Encoding.UTF8.GetBytes(text.ToString())))
                break;
            written = end;
        }
        return written;
    }

    private bool Append(string account, DateOnly day, byte[] bytes)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                // Shared, so another client (or the Manager) appending to the same file doesn't make this one fail.
                using FileStream stream = new(CurrentFile(account, day, bytes.Length), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
                _writeFailing = false;
                return true;
            }
            catch (IOException) when (attempt == 0)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Disk full, still locked or no permission: the lines stay queued for the next tick.
                // Report once per failure streak, on stderr (Trace would loop back into this logger).
                if (!_writeFailing)
                    Console.Error.WriteLine($"Bonxie: can't write the log file ({ex.Message}); will keep retrying.");
                _writeFailing = true;
                return false;
            }
        }
    }

    // The last part of the day, or a new one when this batch would take it past MaxFileBytes.
    private string CurrentFile(string account, DateOnly day, int byteCount)
    {
        string name = $"{account}_{day:yyyy-MM-dd}";
        int part = 1;
        while (File.Exists(PartPath(name, part + 1)))
            part++;

        string path = PartPath(name, part);
        FileInfo file = new(path);
        if (file.Exists && file.Length > 0 && file.Length + byteCount > MaxFileBytes)
            path = PartPath(name, part + 1);
        return path;
    }

    private string PartPath(string name, int part) => Path.Combine(_directory, part == 1 ? $"{name}.log" : $"{name}_{part}.log");

    // At most once an hour. If the folder can't be listed the pass is retried on the next write;
    // a single file that can't be deleted is left for the next pass.
    private void DeleteOldFiles()
    {
        DateTime now = DateTime.Now;
        if (now < _nextCleanup)
            return;

        try
        {
            if (Directory.Exists(_directory))
            {
                DateTime cutoff = now.AddDays(-KeepDays);
                foreach (string file in Directory.EnumerateFiles(_directory, "*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                            File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
            _nextCleanup = now + CleanupInterval;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // It may not exist yet when the first lines arrive; until it does, lines wait in memory.
    // After Dispose the last value read is kept: the settings service may already be gone.
    private bool IsEnabled()
    {
        if (_enabled != -1 && (_disposed || DateTime.Now - _enabledReadAt < SettingRefreshInterval))
            return _enabled == 1;

        try
        {
            ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
            if (settings is null)
                return false;

            _enabled = settings.Get<bool>("LogToFile") ? 1 : 0;
            _enabledReadAt = DateTime.Now;
            return _enabled == 1;
        }
        catch (InvalidOperationException)
        {
            // Ioc not configured yet.
            return false;
        }
    }

    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2}\]")]
    private static partial Regex StartsWithTime();

    [GeneratedRegex(@"[^a-z0-9_.-]")]
    private static partial Regex InvalidFileNameChars();
}
