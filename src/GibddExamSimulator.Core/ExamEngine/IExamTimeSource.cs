using System.Diagnostics;

namespace GibddExamSimulator.ExamEngine;

public interface IExamTimeSource
{
    DateTimeOffset UtcNow { get; }
    TimeSpan Elapsed { get; }
}

public sealed class StopwatchExamTimeSource : IExamTimeSource
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
}

