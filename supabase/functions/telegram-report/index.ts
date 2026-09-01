import { createClient, type SupabaseClient } from "@supabase/supabase-js";

const FIXED_RECIPIENT_KEY = "skeetels";
const FIXED_RECIPIENT_USERNAME = "skeetels";
const MAX_TELEGRAM_TEXT_LENGTH = 3900;

type AnswerPayload = {
  ticketNumber?: number;
  questionNumber?: number;
  thematicBlockId?: number;
  isCorrect?: boolean;
  responseTimeMs?: number;
};

export type SessionPayload = {
  sessionId?: string;
  deviceId?: string;
  deviceKind?: "WindowsDesktop" | "MobilePwa" | "AndroidApp";
  mode?: string;
  outcome?: string;
  completedAtUtc?: string;
  answers?: AnswerPayload[];
  summary?: {
    questionCount?: number;
    answeredCount?: number;
    correctCount?: number;
    errorCount?: number;
    elapsedMs?: number;
  };
};

export type StudySessionRow = {
  session_id: string;
  user_id: string;
  profile_id?: string;
  payload: SessionPayload;
};

type StudySessionDatabaseRow = StudySessionRow & {
  server_seq: number;
  device_id: string;
  device_kind: string;
  mode: string;
  started_at: string;
  completed_at: string;
  outcome: string;
  bank_version: string;
  bank_sha256: string;
  rules_profile: string;
  schema_version: number;
  payload_sha256: string;
  inserted_at: string;
};

type TelegramRecipientRow = {
  recipient_key: string;
  username: string;
  chat_id_text: string;
  confirmed_at: string;
};

type TelegramDeliveryRow = {
  session_id: string;
  user_id: string;
  status: string;
  attempt_count: number;
  lock_token: string | null;
  locked_until: string | null;
  last_error: string | null;
  telegram_message_id: number | null;
  sent_at: string | null;
  created_at: string;
  updated_at: string;
};

type TelegramProfileLinkRow = {
  profile_id: string;
  telegram_chat_id: number;
  telegram_username: string | null;
  linked_at: string;
  revoked_at: string | null;
};

type Database = {
  public: {
    Tables: {
      study_sessions: {
        Row: StudySessionDatabaseRow;
        Insert: Partial<StudySessionDatabaseRow>;
        Update: Partial<StudySessionDatabaseRow>;
        Relationships: [];
      };
      telegram_private_recipients: {
        Row: TelegramRecipientRow;
        Insert: Partial<TelegramRecipientRow>;
        Update: Partial<TelegramRecipientRow>;
        Relationships: [];
      };
      telegram_report_deliveries: {
        Row: TelegramDeliveryRow;
        Insert: Partial<TelegramDeliveryRow>;
        Update: Partial<TelegramDeliveryRow>;
        Relationships: [];
      };
      telegram_profile_links: {
        Row: TelegramProfileLinkRow;
        Insert: Partial<TelegramProfileLinkRow>;
        Update: Partial<TelegramProfileLinkRow>;
        Relationships: [];
      };
    };
    Views: Record<string, never>;
    Functions: {
      claim_telegram_report: {
        Args: { p_session_id: string; p_user_id: string; p_lock_token: string };
        Returns: string;
      };
    };
  };
};

function requiredEnvironmentValue(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) throw new Error(`Missing required server secret: ${name}.`);
  return value;
}

function namedApiKey(pluralName: string, legacyName: string): string {
  const dictionary = Deno.env.get(pluralName);
  if (dictionary) {
    const parsed = JSON.parse(dictionary) as Record<string, string>;
    const key = parsed.default ?? Object.values(parsed)[0];
    if (key) return key;
  }
  return requiredEnvironmentValue(legacyName);
}

function responseJson(status: number, body: Record<string, unknown>): Response {
  return Response.json(body, {
    status,
    headers: { "Cache-Control": "no-store" },
  });
}

function durationText(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.round(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

export function deviceLabel(payload: SessionPayload): string {
  if (payload.deviceKind === "AndroidApp") return "Телефон / APK";
  if (payload.deviceKind === "MobilePwa") return "Телефон / PWA";
  return "ПК";
}

export function installationCode(payload: SessionPayload): string {
  const compact = (payload.deviceId ?? "unknown").replaceAll("-", "")
    .toUpperCase();
  return compact.slice(0, 6) || "UNKNOWN";
}

export function topProblemLines(
  allSessions: StudySessionRow[],
  keySelector: (answer: AnswerPayload) => number | undefined,
  label: string,
): string[] {
  const aggregate = new Map<
    number,
    { attempts: number; errors: number; elapsedMs: number }
  >();
  for (const row of allSessions) {
    for (const answer of row.payload.answers ?? []) {
      const key = keySelector(answer);
      if (!Number.isInteger(key)) continue;
      const current = aggregate.get(key!) ??
        { attempts: 0, errors: 0, elapsedMs: 0 };
      current.attempts++;
      if (!answer.isCorrect) current.errors++;
      current.elapsedMs += Math.max(0, answer.responseTimeMs ?? 0);
      aggregate.set(key!, current);
    }
  }

  return [...aggregate.entries()]
    .filter(([, value]) => value.errors > 0)
    .sort((left, right) => {
      const leftRate = left[1].errors / left[1].attempts;
      const rightRate = right[1].errors / right[1].attempts;
      return rightRate - leftRate || right[1].errors - left[1].errors ||
        left[0] - right[0];
    })
    .slice(0, 5)
    .map(([key, value]) => {
      const rate = Math.round((value.errors / value.attempts) * 100);
      const average = value.attempts === 0
        ? 0
        : value.elapsedMs / value.attempts;
      return `${label} ${key}: ошибок ${value.errors}/${value.attempts} (${rate}%), среднее ${
        durationText(average)
      }`;
    });
}

export function buildStatisticsCommand(allSessions: StudySessionRow[]): string {
  const exams = allSessions.filter((row) => row.payload.mode === "Exam");
  const passed = exams.filter((row) => row.payload.outcome === "Passed").length;
  const answers = exams.flatMap((row) => row.payload.answers ?? []);
  const correct = answers.filter((answer) => answer.isCorrect).length;
  const accuracy = answers.length === 0
    ? 0
    : Math.round(correct * 100 / answers.length);
  const devices = new Set(
    exams.map((row) => row.payload.deviceId).filter((value) => Boolean(value)),
  ).size;
  return [
    "📊 Общая статистика ГИБДД",
    `Экзаменов: ${exams.length}`,
    `Сдано: ${passed}`,
    `Не сдано: ${Math.max(0, exams.length - passed)}`,
    `Точность ответов: ${accuracy}% (${correct}/${answers.length})`,
    `Установок в истории: ${devices}`,
  ].join("\n");
}

export function buildMistakesCommand(allSessions: StudySessionRow[]): string {
  const ticketLines = topProblemLines(
    allSessions,
    (answer) => answer.ticketNumber,
    "• билет",
  );
  const blockLines = topProblemLines(
    allSessions,
    (answer) => answer.thematicBlockId,
    "• блок",
  );
  return [
    "🧩 Последние проблемные места",
    "",
    "Билеты:",
    ...(ticketLines.length > 0 ? ticketLines : ["ошибок пока нет"]),
    "",
    "Тематические блоки:",
    ...(blockLines.length > 0 ? blockLines : ["ошибок пока нет"]),
  ].join("\n");
}

export function buildHelpCommand(): string {
  return [
    "🚦 Учебный помощник ГИБДД",
    "/stats — общая статистика",
    "/mistakes — сложные билеты и блоки",
    "/today — открыть текущую тренировку",
    "/last — последний экзамен",
    "/help — список команд",
  ].join("\n");
}

export function buildReport(
  current: StudySessionRow,
  allSessions: StudySessionRow[],
): string {
  const payload = current.payload;
  const answers = payload.answers ?? [];
  const errors = answers.filter((answer) => !answer.isCorrect);
  const summary = payload.summary ?? {};
  const averageMs = answers.length === 0 ? 0 : answers.reduce(
    (sum, answer) => sum + Math.max(0, answer.responseTimeMs ?? 0),
    0,
  ) / answers.length;
  const outcome = payload.outcome === "Passed"
    ? "СДАН"
    : payload.outcome === "Failed"
    ? "НЕ СДАН"
    : "ЗАВЕРШЁН";
  const completed = payload.completedAtUtc
    ? new Date(payload.completedAtUtc).toLocaleString("ru-RU", {
      timeZone: "Asia/Yekaterinburg",
    })
    : "—";

  const lines = [
    "🚦 Результат экзамена ГИБДД",
    `Результат: ${outcome}`,
    `Устройство: ${deviceLabel(payload)} · ${installationCode(payload)}`,
    `Завершён: ${completed} (Екатеринбург)`,
    `Вопросы: ${summary.answeredCount ?? answers.length}/${
      summary.questionCount ?? answers.length
    }`,
    `Правильно: ${
      summary.correctCount ??
        answers.filter((answer) => answer.isCorrect).length
    }`,
    `Ошибки: ${summary.errorCount ?? errors.length}`,
    `Общее время: ${durationText(summary.elapsedMs ?? 0)}`,
    `Среднее время ответа: ${durationText(averageMs)}`,
    "",
    "Ошибки этого экзамена:",
  ];

  if (errors.length === 0) {
    lines.push("нет");
  } else {
    for (const answer of errors) {
      lines.push(
        `• билет ${answer.ticketNumber ?? "—"}, вопрос ${
          answer.questionNumber ?? "—"
        }, блок ${answer.thematicBlockId ?? "—"}, ${
          durationText(answer.responseTimeMs ?? 0)
        }`,
      );
    }
  }

  lines.push("", "Самые проблемные билеты:");
  const tickets = topProblemLines(
    allSessions,
    (answer) => answer.ticketNumber,
    "• билет",
  );
  lines.push(
    ...(tickets.length > 0 ? tickets : ["недостаточно ошибок для анализа"]),
  );
  lines.push("", "Самые проблемные тематические блоки:");
  const blocks = topProblemLines(
    allSessions,
    (answer) => answer.thematicBlockId,
    "• блок",
  );
  lines.push(
    ...(blocks.length > 0 ? blocks : ["недостаточно ошибок для анализа"]),
  );

  const report = lines.join("\n");
  return report.length <= MAX_TELEGRAM_TEXT_LENGTH
    ? report
    : `${report.slice(0, MAX_TELEGRAM_TEXT_LENGTH - 24)}\n…отчёт сокращён`;
}

async function telegramCall<T>(
  token: string,
  method: string,
  body: Record<string, unknown>,
): Promise<T> {
  const response = await fetch(
    `https://api.telegram.org/bot${token}/${method}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
  );
  const payload = await response.json() as {
    ok?: boolean;
    result?: T;
    description?: string;
  };
  if (!response.ok || !payload.ok || payload.result === undefined) {
    throw new Error(
      payload.description ??
        `Telegram ${method} failed with HTTP ${response.status}.`,
    );
  }
  return payload.result;
}

async function resolveFixedChatId(
  admin: SupabaseClient<Database>,
  token: string,
): Promise<string> {
  const cached = await admin
    .from("telegram_private_recipients")
    .select("chat_id_text")
    .eq("recipient_key", FIXED_RECIPIENT_KEY)
    .maybeSingle();
  if (cached.error) throw cached.error;
  if (cached.data?.chat_id_text) return cached.data.chat_id_text as string;

  type TelegramUpdate = {
    update_id: number;
    message?: {
      text?: string;
      chat?: { id?: number | string; type?: string };
      from?: { username?: string };
    };
  };
  const updates = await telegramCall<TelegramUpdate[]>(token, "getUpdates", {
    limit: 100,
    timeout: 0,
    allowed_updates: ["message"],
  });
  const match = updates
    .filter((update) =>
      update.message?.chat?.type === "private" &&
      update.message?.from?.username?.toLowerCase() ===
        FIXED_RECIPIENT_USERNAME &&
      update.message?.text?.trim().toLowerCase().startsWith("/start")
    )
    .sort((left, right) => right.update_id - left.update_id)[0];
  const chatId = match?.message?.chat?.id;
  if (chatId === undefined) {
    throw new Error(
      "Пользователь @skeetels должен отправить боту команду /start.",
    );
  }

  const saved = await admin.from("telegram_private_recipients").upsert({
    recipient_key: FIXED_RECIPIENT_KEY,
    username: FIXED_RECIPIENT_USERNAME,
    chat_id_text: String(chatId),
    confirmed_at: new Date().toISOString(),
  });
  if (saved.error) throw saved.error;
  return String(chatId);
}

export async function handleTelegramReport(
  request: Request,
): Promise<Response> {
  if (request.method !== "POST") {
    return responseJson(405, { error: "method_not_allowed" });
  }

  const authorization = request.headers.get("Authorization") ?? "";
  const tokenMatch = /^Bearer\s+(.+)$/i.exec(authorization);
  if (!tokenMatch) return responseJson(401, { error: "missing_user_token" });

  const projectUrl = requiredEnvironmentValue("SUPABASE_URL");
  const publishableKey = namedApiKey(
    "SUPABASE_PUBLISHABLE_KEYS",
    "SUPABASE_ANON_KEY",
  );
  const secretKey = namedApiKey(
    "SUPABASE_SECRET_KEYS",
    "SUPABASE_SERVICE_ROLE_KEY",
  );
  const userClient = createClient<Database>(projectUrl, publishableKey, {
    global: { headers: { Authorization: authorization } },
    auth: {
      autoRefreshToken: false,
      persistSession: false,
      detectSessionInUrl: false,
    },
  });
  const admin = createClient<Database>(projectUrl, secretKey, {
    auth: {
      autoRefreshToken: false,
      persistSession: false,
      detectSessionInUrl: false,
    },
  });

  const userResult = await userClient.auth.getUser(tokenMatch[1]);
  if (userResult.error || !userResult.data.user) {
    return responseJson(401, { error: "invalid_user_token" });
  }
  const userId = userResult.data.user.id;

  let requestBody: { sessionId?: string };
  try {
    requestBody = await request.json();
  } catch {
    return responseJson(400, { error: "invalid_json" });
  }
  if (!requestBody.sessionId) {
    return responseJson(400, { error: "session_id_required" });
  }

  const currentResult = await userClient
    .from("study_sessions")
    .select("session_id,user_id,profile_id,payload")
    .eq("session_id", requestBody.sessionId)
    .maybeSingle();
  if (currentResult.error) {
    return responseJson(500, { error: "session_read_failed" });
  }
  if (!currentResult.data) {
    return responseJson(404, { error: "session_not_found" });
  }
  if (currentResult.data.payload.mode !== "Exam") {
    return responseJson(200, { skipped: true });
  }

  const lockToken = crypto.randomUUID();
  const claim = await admin.rpc("claim_telegram_report", {
    p_session_id: currentResult.data.session_id,
    p_user_id: userId,
    p_lock_token: lockToken,
  });
  if (claim.error) return responseJson(500, { error: "delivery_claim_failed" });
  if (claim.data === "sent") {
    return responseJson(200, { delivered: true, duplicate: true });
  }
  if (claim.data !== "claimed") return responseJson(202, { pending: true });

  try {
    const historyResult = await userClient
      .from("study_sessions")
      .select("session_id,user_id,profile_id,payload")
      .eq("mode", "Exam")
      .order("completed_at", { ascending: false })
      .limit(250);
    if (historyResult.error) throw historyResult.error;

    const botToken = requiredEnvironmentValue("TELEGRAM_BOT_TOKEN");
    let chatId: string | null = null;
    if (currentResult.data.profile_id) {
      const profileLink = await admin.from("telegram_profile_links")
        .select("telegram_chat_id,telegram_username")
        .eq("profile_id", currentResult.data.profile_id)
        .is("revoked_at", null)
        .maybeSingle();
      if (
        !profileLink.error &&
        profileLink.data?.telegram_username?.toLowerCase() ===
          FIXED_RECIPIENT_USERNAME
      ) {
        chatId = String(profileLink.data.telegram_chat_id);
      }
    }
    chatId ??= await resolveFixedChatId(admin, botToken);
    const sent = await telegramCall<{ message_id: number }>(
      botToken,
      "sendMessage",
      {
        chat_id: chatId,
        text: buildReport(
          currentResult.data,
          historyResult.data ?? [currentResult.data],
        ),
        protect_content: true,
        disable_web_page_preview: true,
      },
    );

    const completed = await admin
      .from("telegram_report_deliveries")
      .update({
        status: "sent",
        telegram_message_id: sent.message_id,
        sent_at: new Date().toISOString(),
        locked_until: null,
        lock_token: null,
        last_error: null,
        updated_at: new Date().toISOString(),
      })
      .eq("session_id", currentResult.data.session_id)
      .eq("lock_token", lockToken);
    if (completed.error) throw completed.error;
    return responseJson(200, { delivered: true, duplicate: false });
  } catch (error) {
    const safeMessage = error instanceof Error
      ? error.message.slice(0, 2000)
      : "Unknown delivery error.";
    await admin
      .from("telegram_report_deliveries")
      .update({
        status: "failed",
        last_error: safeMessage,
        locked_until: null,
        lock_token: null,
        updated_at: new Date().toISOString(),
      })
      .eq("session_id", currentResult.data.session_id)
      .eq("lock_token", lockToken);
    return responseJson(503, { error: "telegram_delivery_pending" });
  }
}

if (import.meta.main) {
  Deno.serve(handleTelegramReport);
}
