namespace GibddExamSimulator.Models;

public sealed class CandidateProfile
{
    public string FullName { get; set; } = string.Empty;
    public DateOnly BirthDate { get; set; } = new(2000, 1, 1);
    public string Category { get; set; } = "AB";
    public int TerminalNumber { get; set; } = 6;
    public string Department { get; set; } = "Учебный терминал";
}

