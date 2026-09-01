import { createClient } from "@supabase/supabase-js";
import {
  buildHelpCommand,
  buildMistakesCommand,
  buildReport,
  buildStatisticsCommand,
  type StudySessionRow,
} from "../telegram-report/index.ts";

const OWNER_USERNAME = "skeetels";
const OWNER_RECIPIENT_KEY = "skeetels";

export type TelegramMessage = {
  text?: string;
  chat?: { id?: number | string; type?: string };
  from?: { username?: string };
};

type TelegramUpdate = {
  update_id?: number;
  message?: TelegramMessage;
};

function requiredEnvironmentValue(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) throw new Error(`Missing server secret: ${name}.`);
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

function constantTimeEqual(left: string, right: string): boolean {
  const leftBytes = new TextEncoder().encode(left);
  const rightBytes = new TextEncoder().encode(right);
  let difference = leftBytes.length ^ rightBytes.length;
  const length = Math.max(leftBytes.length, rightBytes.length);
  for (let index = 0; index < length; index++) {
    difference |= (leftBytes[index] ?? 0) ^ (rightBytes[index] ?? 0);
  }
  return difference === 0;
}

export function isOwnerPrivateMessage(message: TelegramMessage): boolean {
  return message.chat?.type === "private" &&
    message.from?.username?.toLowerCase() === OWNER_USERNAME;
}

export function commandFromText(text: string | undefined): string {
  const token = text?.trim().split(/\s+/, 1)[0]?.toLowerCase() ?? "";
  return token.split("@", 1)[0];
}

export function startPayload(text: string | undefined): string | null {
  const parts = text?.trim().split(/\s+/, 2) ?? [];
  if (commandFromText(text) !== "/start" || !parts[1]) return null;
  const value = parts[1].trim();
  return /^[A-Za-z0-9_-]{40,64}$/.test(value) ? value : null;
}

async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(value),
  );
  return Array.from(
    new Uint8Array(digest),
    (byte) => byte.toString(16).padStart(2, "0"),
  ).join("");
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
    throw new Error(payload.description ?? `Telegram ${method} failed.`);
  }
  return payload.result;
}

function publicHttpsUrl(name: string): string | null {
  const value = Deno.env.get(name)?.trim();
  if (!value || !value.startsWith("https://")) return null;
  return value.replace(/\/$/, "");
}

function todayCommand(): string {
  const pwa = publicHttpsUrl("PUBLIC_PWA_URL");
  const release = publicHttpsUrl("PUBLIC_RELEASE_URL");
  const lines = [
    "📅 Тренировка на сегодня",
    "Откройте «Умные 10» или «Работу над ошибками» — порядок будет рассчитан по общей истории устройств.",
  ];
  if (pwa) lines.push(`PWA: ${pwa}/train`);
  if (release) lines.push(`APK и обновления: ${release}`);
  if (!pwa && !release) {
    lines.push("Публичные ссылки появятся после публикации релиза.");
  }
  return lines.join("\n");
}

async function sendText(token: string, chatId: string, text: string) {
  await telegramCall<{ message_id: number }>(token, "sendMessage", {
    chat_id: chatId,
    text,
    protect_content: true,
    disable_web_page_preview: true,
  });
}

export async function handleTelegramBot(request: Request): Promise<Response> {
  if (request.method !== "POST") {
    return responseJson(405, { error: "method_not_allowed" });
  }

  const expectedSecret = requiredEnvironmentValue("TELEGRAM_WEBHOOK_SECRET");
  const suppliedSecret = request.headers.get(
    "x-telegram-bot-api-secret-token",
  ) ?? "";
  if (!constantTimeEqual(expectedSecret, suppliedSecret)) {
    return responseJson(401, { error: "invalid_webhook_secret" });
  }

  let update: TelegramUpdate;
  try {
    update = await request.json();
  } catch {
    return responseJson(400, { error: "invalid_json" });
  }
  const message = update.message;
  if (!message || !isOwnerPrivateMessage(message)) {
    return responseJson(200, { ignored: true });
  }
  const chatIdValue = message.chat?.id;
  if (chatIdValue === undefined) {
    return responseJson(200, { ignored: true });
  }
  const chatId = String(chatIdValue);
  const command = commandFromText(message.text);

  try {
    const projectUrl = requiredEnvironmentValue("SUPABASE_URL");
    const serviceKey = namedApiKey(
      "SUPABASE_SECRET_KEYS",
      "SUPABASE_SERVICE_ROLE_KEY",
    );
    const admin = createClient(projectUrl, serviceKey, {
      auth: {
        autoRefreshToken: false,
        persistSession: false,
        detectSessionInUrl: false,
      },
    });
    const botToken = requiredEnvironmentValue("TELEGRAM_BOT_TOKEN");

    if (command === "/start") {
      const saved = await admin.from("telegram_private_recipients").upsert({
        recipient_key: OWNER_RECIPIENT_KEY,
        username: OWNER_USERNAME,
        chat_id_text: chatId,
        confirmed_at: new Date().toISOString(),
      });
      if (saved.error) throw saved.error;
      const linkToken = startPayload(message.text);
      let profileLinked = false;
      if (linkToken) {
        const tokenHash = await sha256Hex(linkToken);
        const tokenResult = await admin.from("telegram_link_tokens")
          .select("id,profile_id,expires_at,consumed_at")
          .eq("token_hash", tokenHash)
          .maybeSingle();
        if (
          !tokenResult.error && tokenResult.data &&
          tokenResult.data.consumed_at === null &&
          Date.parse(tokenResult.data.expires_at) > Date.now()
        ) {
          const consumed = await admin.from("telegram_link_tokens")
            .update({ consumed_at: new Date().toISOString() })
            .eq("id", tokenResult.data.id)
            .is("consumed_at", null)
            .select("id")
            .maybeSingle();
          if (!consumed.error && consumed.data) {
            const linked = await admin.from("telegram_profile_links").upsert({
              profile_id: tokenResult.data.profile_id,
              telegram_chat_id: Number(chatId),
              telegram_username: OWNER_USERNAME,
              linked_at: new Date().toISOString(),
              revoked_at: null,
            });
            if (linked.error) throw linked.error;
            profileLinked = true;
          }
        }
      }
      const greeting = profileLinked
        ? "✅ Telegram подключён к учебному профилю. Отчёты будут приходить автоматически.\n\n"
        : "✅ Бот готов принимать автоматические отчёты.\n\n";
      await sendText(botToken, chatId, greeting + buildHelpCommand());
      return responseJson(200, { accepted: true, linked: true, profileLinked });
    }

    const recipient = await admin.from("telegram_private_recipients")
      .select("chat_id_text,username")
      .eq("recipient_key", OWNER_RECIPIENT_KEY)
      .maybeSingle();
    if (recipient.error) throw recipient.error;
    if (
      recipient.data?.chat_id_text !== chatId ||
      recipient.data?.username?.toLowerCase() !== OWNER_USERNAME
    ) {
      return responseJson(403, { error: "owner_not_linked" });
    }

    const history = await admin.from("study_sessions")
      .select("session_id,user_id,payload")
      .eq("mode", "Exam")
      .order("completed_at", { ascending: false })
      .limit(500);
    if (history.error) throw history.error;
    const sessions = (history.data ?? []) as StudySessionRow[];

    let reply: string;
    switch (command) {
      case "/stats":
        reply = buildStatisticsCommand(sessions);
        break;
      case "/mistakes":
        reply = buildMistakesCommand(sessions);
        break;
      case "/today":
        reply = todayCommand();
        break;
      case "/last":
        reply = sessions.length === 0
          ? "Завершённых экзаменов пока нет."
          : buildReport(sessions[0], sessions);
        break;
      case "/help":
      default:
        reply = buildHelpCommand();
        break;
    }
    await sendText(botToken, chatId, reply);
    return responseJson(200, { accepted: true });
  } catch {
    return responseJson(503, { error: "command_processing_pending" });
  }
}

if (import.meta.main) {
  Deno.serve(handleTelegramBot);
}
