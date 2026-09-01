using GibddExamSimulator.ExamEngine;

namespace GibddExamSimulator.Tests;

internal sealed class FakeExamTimeSource : IExamTimeSource
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);
    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan value)
    {
        Elapsed += value;
        UtcNow += value;
    }
}

