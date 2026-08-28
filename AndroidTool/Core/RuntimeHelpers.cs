using System.Text.RegularExpressions;

namespace AndroidTool.Core;

public sealed class BoundedLineBuffer
{
    private readonly int _capacity;
    private readonly Queue<string> _lines = new();

    public BoundedLineBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public string Text => string.Join(Environment.NewLine, _lines);
    public void Clear() => _lines.Clear();
    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > _capacity) _lines.Dequeue();
    }
}

public sealed record LogQueueBatch(IReadOnlyList<string> Lines, long TotalDroppedLines);

public sealed class BoundedLogQueue
{
    private readonly object _sync = new();
    private readonly int _maxLines;
    private readonly int _maxLineLength;
    private readonly int _maxCharacters;
    private readonly Queue<string> _lines = new();
    private int _characterCount;
    private long _totalDroppedLines;

    public BoundedLogQueue(int maxLines, int maxLineLength, int maxCharacters = 8 * 1024 * 1024)
    {
        if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxLineLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLineLength));
        if (maxCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        _maxLines = maxLines;
        _maxLineLength = maxLineLength;
        _maxCharacters = maxCharacters;
    }

    public int CharacterCount
    {
        get
        {
            lock (_sync) return _characterCount;
        }
    }

    public int Count
    {
        get
        {
            lock (_sync) return _lines.Count;
        }
    }

    public void Enqueue(string? line)
    {
        var normalized = NormalizeLine(line ?? string.Empty);
        if (normalized.Length > _maxCharacters)
            normalized = _maxCharacters == 1 ? "…" : normalized[..(_maxCharacters - 1)] + "…";
        lock (_sync)
        {
            while (_lines.Count >= _maxLines || (_lines.Count > 0 && _characterCount + normalized.Length > _maxCharacters))
            {
                _characterCount -= _lines.Dequeue().Length;
                _totalDroppedLines++;
            }
            _lines.Enqueue(normalized);
            _characterCount += normalized.Length;
        }
    }

    public LogQueueBatch Drain(int maxLines)
    {
        if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));

        lock (_sync)
        {
            var count = Math.Min(maxLines, _lines.Count);
            var drained = new List<string>(count);
            for (var index = 0; index < count; index++)
            {
                var line = _lines.Dequeue();
                _characterCount -= line.Length;
                drained.Add(line);
            }
            return new LogQueueBatch(drained, _totalDroppedLines);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _lines.Clear();
            _characterCount = 0;
            _totalDroppedLines = 0;
        }
    }

    private string NormalizeLine(string line)
    {
        var builder = new System.Text.StringBuilder(Math.Min(line.Length, _maxLineLength + 1));
        var truncated = false;

        foreach (var character in line)
        {
            if (character != '\t' && char.IsControl(character)) continue;
            if (builder.Length >= _maxLineLength)
            {
                truncated = true;
                break;
            }
            builder.Append(character);
        }

        if (truncated) builder.Append('…');
        return builder.ToString();
    }
}

public sealed class BoundedDisplayBuffer
{
    private readonly int _maxLines;
    private readonly int _maxCharacters;
    private readonly int _targetLines;
    private readonly int _targetCharacters;
    private readonly Queue<string> _lines = new();

    public BoundedDisplayBuffer(int maxLines, int maxCharacters)
    {
        if (maxLines < 1) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        _maxLines = maxLines;
        _maxCharacters = maxCharacters;
        _targetLines = Math.Max(1, maxLines - Math.Max(1, (int)Math.Ceiling(maxLines * 0.2)));
        _targetCharacters = Math.Max(1, maxCharacters - Math.Max(1, (int)Math.Ceiling(maxCharacters * 0.2)));
    }

    public int LineCount => _lines.Count;
    public int CharacterCount { get; private set; }
    public string Text => string.Join(Environment.NewLine, _lines);

    public bool AddRange(IEnumerable<string> lines)
    {
        var trimmed = false;
        foreach (var line in lines)
        {
            if (_lines.Count > 0) CharacterCount += Environment.NewLine.Length;
            _lines.Enqueue(line);
            CharacterCount += line.Length;

            if (_lines.Count <= _maxLines && CharacterCount <= _maxCharacters) continue;

            while (_lines.Count > _targetLines || CharacterCount > _targetCharacters)
            {
                var removed = _lines.Dequeue();
                CharacterCount -= removed.Length;
                if (_lines.Count > 0) CharacterCount -= Environment.NewLine.Length;
                trimmed = true;
            }
        }
        return trimmed;
    }

    public void Clear()
    {
        _lines.Clear();
        CharacterCount = 0;
    }
}

public sealed class LogSessionGate
{
    private long _currentSession;

    public long Begin() => Interlocked.Increment(ref _currentSession);
    public bool IsCurrent(long session) => session == Interlocked.Read(ref _currentSession);
    public void Invalidate() => Interlocked.Increment(ref _currentSession);
}

public sealed record LogUiBatch(IReadOnlyList<string> Lines, string? ReplacementText, long TotalDroppedLines);

public sealed class LogPipeline
{
    private readonly BoundedLogQueue _pending;
    private readonly BoundedDisplayBuffer _display;

    public LogPipeline(int pendingLines, int maxLineLength, int displayLines, int displayCharacters)
    {
        _pending = new BoundedLogQueue(pendingLines, maxLineLength);
        _display = new BoundedDisplayBuffer(displayLines, displayCharacters);
    }

    public void Enqueue(string line) => _pending.Enqueue(line);

    public LogUiBatch DrainUiBatch(int maxLines)
    {
        var pending = _pending.Drain(maxLines);
        var trimmed = _display.AddRange(pending.Lines);
        return new LogUiBatch(pending.Lines, trimmed ? _display.Text : null, pending.TotalDroppedLines);
    }

    public string Snapshot => _display.Text;

    public void Clear()
    {
        _pending.Clear();
        _display.Clear();
    }
}

public static partial class AdbProgressParser
{
    [GeneratedRegex(@"\[\s*(\d{1,3})%\]")]
    private static partial Regex PercentRegex();

    public static int? TryParsePercent(string line)
    {
        var match = PercentRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? Math.Clamp(value, 0, 100) : null;
    }
}

public static class AdbErrorTranslator
{
    public static string ToUserMessage(string raw)
    {
        if (raw.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)) return "设备未授权，请在设备上允许 USB 调试。";
        if (raw.Contains("no devices", StringComparison.OrdinalIgnoreCase) || raw.Contains("device not found", StringComparison.OrdinalIgnoreCase)) return "未找到 Android 设备。";
        if (raw.Contains("INSTALL_FAILED_VERSION_DOWNGRADE", StringComparison.OrdinalIgnoreCase)) return "版本降级：目标设备上的版本更高。";
        if (raw.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE", StringComparison.OrdinalIgnoreCase)) return "设备存储空间不足。";
        if (raw.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase)) return "签名不一致，无法覆盖安装。";
        return string.IsNullOrWhiteSpace(raw) ? "命令执行失败，未返回详细原因。" : raw.Trim();
    }
}
