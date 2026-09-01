import {
  buildHelpCommand,
  buildMistakesCommand,
  buildReport,
  buildStatisticsCommand,
  deviceLabel,
  installationCode,
  type SessionPayload,
  type StudySessionRow,
} from "./index.ts";

function assertIncludes(actual: string, expected: string): void {
  if (!actual.includes(expected)) {
    throw new Error(
      `Expected report to include ${JSON.stringify(expected)}.\n${actual}`,
    );
  }
}

function examRow(payload: SessionPayload): StudySessionRow {
  return {
    session_id: "10000000-0000-0000-0000-000000000001",
    user_id: "20000000-0000-0000-0000-000000000002",
    payload,
  };
}

Deno.test("mobile report contains anonymous device marker and detailed analysis", () => {
  const current = examRow({
    deviceId: "abcdef12-3456-4789-abcd-ef1234567890",
    deviceKind: "MobilePwa",
    mode: "Exam",
    outcome: "Failed",
    completedAtUtc: "2026-08-31T12:00:00Z",
    answers: [
      {
        ticketNumber: 7,
        questionNumber: 3,
        thematicBlockId: 9,
        isCorrect: false,
        responseTimeMs: 12_000,
      },
      {
        ticketNumber: 12,
        questionNumber: 8,
        thematicBlockId: 4,
        isCorrect: true,
        responseTimeMs: 8_000,
      },
    ],
    summary: {
      questionCount: 20,
      answeredCount: 20,
      correctCount: 19,
      errorCount: 1,
      elapsedMs: 240_000,
    },
  });

  const earlier = examRow({
    answers: [
      {
        ticketNumber: 7,
        questionNumber: 6,
        thematicBlockId: 9,
        isCorrect: false,
        responseTimeMs: 18_000,
      },
    ],
  });

  const report = buildReport(current, [current, earlier]);
  assertIncludes(report, "Устройство: Телефон / PWA · ABCDEF");
  assertIncludes(report, "билет 7, вопрос 3, блок 9, 0:12");
  assertIncludes(report, "билет 7: ошибок 2/2 (100%), среднее 0:15");
  assertIncludes(report, "блок 9: ошибок 2/2 (100%), среднее 0:15");
});

Deno.test("installable Android report has a distinct APK marker", () => {
  const payload: SessionPayload = {
    deviceId: "fedcba98-3456-4789-abcd-ef1234567890",
    deviceKind: "AndroidApp",
  };

  if (deviceLabel(payload) !== "Телефон / APK") {
    throw new Error("Unexpected Android label.");
  }
});

Deno.test("owner commands aggregate exams without exposing account secrets", () => {
  const sessions = [examRow({
    deviceId: "abcdef12-3456-4789-abcd-ef1234567890",
    deviceKind: "AndroidApp",
    mode: "Exam",
    outcome: "Passed",
    answers: [
      { ticketNumber: 4, thematicBlockId: 7, isCorrect: false },
      { ticketNumber: 5, thematicBlockId: 8, isCorrect: true },
    ],
  })];

  assertIncludes(buildStatisticsCommand(sessions), "Экзаменов: 1");
  assertIncludes(
    buildStatisticsCommand(sessions),
    "Точность ответов: 50% (1/2)",
  );
  assertIncludes(buildMistakesCommand(sessions), "билет 4: ошибок 1/1");
  assertIncludes(buildHelpCommand(), "/last — последний экзамен");
});

Deno.test("desktop marker never exposes a host name", () => {
  const payload: SessionPayload = {
    deviceId: "12345678-0000-0000-0000-000000000000",
    deviceKind: "WindowsDesktop",
  };

  if (deviceLabel(payload) !== "ПК") {
    throw new Error("Unexpected desktop label.");
  }
  if (installationCode(payload) !== "123456") {
    throw new Error("Unexpected installation code.");
  }
});
