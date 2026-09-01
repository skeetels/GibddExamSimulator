using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GibddExamSimulator.Application.StudySessions;

public static class StudySessionCanonicalizer
{
    public static string ComputePayloadSha256(StudySessionEnvelope session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Convert.ToHexString(SHA256.HashData(SerializeCanonicalPayload(session)));
    }

    public static bool VerifyPayloadSha256(StudySessionEnvelope session)
    {
        if (string.IsNullOrWhiteSpace(session.PayloadSha256) || session.PayloadSha256.Length != 64)
            return false;
        var expected = Encoding.ASCII.GetBytes(ComputePayloadSha256(session));
        var actual = Encoding.ASCII.GetBytes(session.PayloadSha256.ToUpperInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static byte[] SerializeCanonicalPayload(StudySessionEnvelope session)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", session.SessionId);
            writer.WriteNumber("schemaVersion", session.SchemaVersion);
            writer.WriteString("deviceId", session.DeviceId);
            writer.WriteString("deviceKind", session.DeviceKind.ToString());
            writer.WriteString("mode", session.Mode.ToString());
            writer.WriteString("startedAtUtc", session.StartedAtUtc.ToUniversalTime());
            writer.WriteString("completedAtUtc", session.CompletedAtUtc.ToUniversalTime());
            writer.WriteString("outcome", session.Outcome.ToString());
            writer.WriteString("bankVersion", session.BankVersion);
            writer.WriteString("bankSha256", session.BankSha256.ToUpperInvariant());
            writer.WriteString("rulesProfile", session.RulesProfile);

            writer.WriteStartArray("orderedQuestionIds");
            foreach (var questionId in session.OrderedQuestionIds)
                writer.WriteNumberValue(questionId);
            writer.WriteEndArray();

            writer.WriteStartArray("answers");
            foreach (var answer in session.Answers.OrderBy(answer => answer.SequenceNumber))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sequenceNumber", answer.SequenceNumber);
                writer.WriteNumber("questionId", answer.QuestionId);
                writer.WriteNumber("ticketNumber", answer.TicketNumber);
                writer.WriteNumber("questionNumber", answer.QuestionNumber);
                writer.WriteNumber("groupId", answer.GroupId);
                writer.WriteNumber("thematicBlockId", answer.ThematicBlockId);
                writer.WriteString("stage", answer.Stage.ToString());
                if (answer.SelectedAnswer.HasValue)
                    writer.WriteNumber("selectedAnswer", answer.SelectedAnswer.Value);
                else
                    writer.WriteNull("selectedAnswer");
                writer.WriteNumber("correctAnswer", answer.CorrectAnswer);
                writer.WriteBoolean("isCorrect", answer.IsCorrect);
                writer.WriteNumber("responseTimeMs", answer.ResponseTimeMs);
                if (answer.AnsweredAtUtc.HasValue)
                    writer.WriteString("answeredAtUtc", answer.AnsweredAtUtc.Value.ToUniversalTime());
                else
                    writer.WriteNull("answeredAtUtc");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("legacyAggregates");
            foreach (var aggregate in session.LegacyAggregates.OrderBy(item => item.QuestionId))
            {
                writer.WriteStartObject();
                writer.WriteNumber("questionId", aggregate.QuestionId);
                writer.WriteNumber("groupId", aggregate.GroupId);
                writer.WriteNumber("attemptCount", aggregate.AttemptCount);
                writer.WriteNumber("correctCount", aggregate.CorrectCount);
                writer.WriteNumber("totalResponseTimeMs", aggregate.TotalResponseTimeMs);
                writer.WriteString("lastAttemptAtUtc", aggregate.LastAttemptAtUtc.ToUniversalTime());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("summary");
            writer.WriteNumber("questionCount", session.Summary.QuestionCount);
            writer.WriteNumber("answeredCount", session.Summary.AnsweredCount);
            writer.WriteNumber("correctCount", session.Summary.CorrectCount);
            writer.WriteNumber("errorCount", session.Summary.ErrorCount);
            writer.WriteNumber("elapsedMs", session.Summary.ElapsedMs);
            writer.WriteNumber("longestCorrectStreak", session.Summary.LongestCorrectStreak);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
