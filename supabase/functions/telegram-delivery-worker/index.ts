import { createClient } from "@supabase/supabase-js";
import { buildReport, type StudySessionRow } from "../telegram-report/index.ts";

const FIXED_RECIPIENT_KEY = "skeetels";
const FIXED_RECIPIENT_USERNAME = "skeetels";
export const DELIVERY_RETRY_STATUSES = [
  "pending",
  "failed",
  "sending",
] as const;

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

export function constantTimeEqual(expected: string, actual: string): boolean {
  const encoder = new TextEncoder();
  const left = encoder.encode(expected);
  const right = encoder.encode(actual);
  let difference = left.length ^ right.length;
  const length = Math.max(left.length, right.length);
  for (let index = 0; index < length; index++) {
    difference |= (left[index % Math.max(1, left.length)] ?? 0) ^
      (right[index % Math.max(1, right.length)] ?? 0);
  }
  return difference === 0;
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

async function deliverOne(
  // Hosted migrations are the schema source of truth; generated DB types are unavailable before deployment.
  // deno-lint-ignore no-explicit-any
  admin: any,
  sessionId: string,
  userId: string,
): Promise<"sent" | "duplicate" | "busy" | "failed"> {
  const lockToken = crypto.randomUUID();
  const claim = await admin.rpc("claim_telegram_report", {
    p_session_id: sessionId,
    p_user_id: userId,
    p_lock_token: lockToken,
  });
  if (claim.error) throw claim.error;
  if (claim.data === "sent") return "duplicate";
  if (claim.data !== "claimed") return "busy";

  try {
    const current = await admin.from("study_sessions")
      .select("session_id,user_id,profile_id,payload")
      .eq("session_id", sessionId)
      .maybeSingle();
    if (current.error || !current.data) {
      throw current.error ??
        new Error("Session disappeared from the delivery queue.");
    }
    const history = await admin.from("study_sessions")
      .select("session_id,user_id,profile_id,payload")
      .eq("profile_id", current.data.profile_id)
      .eq("mode", "Exam")
      .order("completed_at", { ascending: false })
      .limit(250);
    if (history.error) throw history.error;

    let chatId: string | null = null;
    const profileLink = await admin.from("telegram_profile_links")
      .select("telegram_chat_id,telegram_username")
      .eq("profile_id", current.data.profile_id)
      .is("revoked_at", null)
      .maybeSingle();
    if (
      !profileLink.error &&
      profileLink.data?.telegram_username?.toLowerCase() ===
        FIXED_RECIPIENT_USERNAME
    ) {
      chatId = String(profileLink.data.telegram_chat_id);
    }
    if (!chatId) {
      const fixed = await admin.from("telegram_private_recipients")
        .select("chat_id_text")
        .eq("recipient_key", FIXED_RECIPIENT_KEY)
        .maybeSingle();
      if (fixed.error) throw fixed.error;
      chatId = fixed.data?.chat_id_text ?? null;
    }
    if (!chatId) {
      throw new Error("Fixed Telegram owner has not sent /start yet.");
    }

    const sent = await telegramCall<{ message_id: number }>(
      requiredEnvironmentValue("TELEGRAM_BOT_TOKEN"),
      "sendMessage",
      {
        chat_id: chatId,
        text: buildReport(
          current.data as StudySessionRow,
          (history.data ?? [current.data]) as StudySessionRow[],
        ),
        protect_content: true,
        disable_web_page_preview: true,
      },
    );
    const completed = await admin.from("telegram_report_deliveries").update({
      status: "sent",
      telegram_message_id: sent.message_id,
      sent_at: new Date().toISOString(),
      locked_until: null,
      lock_token: null,
      last_error: null,
      updated_at: new Date().toISOString(),
    }).eq("session_id", sessionId).eq("lock_token", lockToken);
    if (completed.error) throw completed.error;
    return "sent";
  } catch (error) {
    const safe = error instanceof Error
      ? error.message.slice(0, 2000)
      : "Unknown delivery error.";
    await admin.from("telegram_report_deliveries").update({
      status: "failed",
      last_error: safe,
      locked_until: null,
      lock_token: null,
      updated_at: new Date().toISOString(),
    }).eq("session_id", sessionId).eq("lock_token", lockToken);
    return "failed";
  }
}

export async function handleTelegramDeliveryWorker(
  request: Request,
): Promise<Response> {
  if (request.method !== "POST") {
    return Response.json({ error: "method_not_allowed" }, { status: 405 });
  }
  const expected = requiredEnvironmentValue("TELEGRAM_DELIVERY_WORKER_SECRET");
  const supplied = request.headers.get("x-telegram-worker-secret") ?? "";
  if (!constantTimeEqual(expected, supplied)) {
    return Response.json({ error: "invalid_worker_secret" }, { status: 401 });
  }

  const projectUrl = requiredEnvironmentValue("SUPABASE_URL");
  const serviceKey = namedApiKey(
    "SUPABASE_SECRET_KEYS",
    "SUPABASE_SERVICE_ROLE_KEY",
  );
  const admin = createClient(projectUrl, serviceKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const queue = await admin.from("telegram_report_deliveries")
    .select("session_id,user_id")
    // A crashed or previously under-privileged worker can leave an expired
    // claim in `sending`; claim_telegram_report safely reclaims it after TTL.
    .in("status", [...DELIVERY_RETRY_STATUSES])
    .order("created_at", { ascending: true })
    .limit(20);
  if (queue.error) {
    return Response.json({
      error: "queue_read_failed",
      databaseCode: queue.error.code ?? "unknown",
      detail: queue.error.message.slice(0, 500),
    }, { status: 503 });
  }

  let sent = 0;
  let pending = 0;
  for (const item of queue.data ?? []) {
    const result = await deliverOne(admin, item.session_id, item.user_id);
    if (result === "sent" || result === "duplicate") sent++;
    else pending++;
  }
  return Response.json({ ok: true, sent, pending }, {
    headers: { "Cache-Control": "no-store" },
  });
}

if (import.meta.main) {
  Deno.serve(handleTelegramDeliveryWorker);
}
