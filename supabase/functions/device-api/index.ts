import { createClient, type SupabaseClient } from "@supabase/supabase-js";

const API_VERSION = "1";
const PAIRING_TTL_MS = 5 * 60 * 1000;
const MAX_BODY_BYTES = 1_000_000;
const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, apikey, content-type, x-environment-id",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Cache-Control": "no-store",
};

type AuthContext = {
  userId: string;
  // This function intentionally works against migrations deployed alongside it.
  // A generated Database type is not available until the production project exists.
  // deno-lint-ignore no-explicit-any
  userClient: SupabaseClient<any>;
  // deno-lint-ignore no-explicit-any
  admin: SupabaseClient<any>;
};

type StudySession = {
  sessionId: string;
  schemaVersion: number;
  deviceId: string;
  deviceKind: string;
  mode: string;
  startedAtUtc: string;
  completedAtUtc: string;
  outcome: string;
  bankVersion: string;
  bankSha256: string;
  rulesProfile: string;
  payloadSha256: string;
};

function environmentValue(name: string, fallback = ""): string {
  return Deno.env.get(name)?.trim() || fallback;
}

function requiredEnvironmentValue(name: string): string {
  const value = environmentValue(name);
  if (!value) throw new Error(`Missing required server setting: ${name}.`);
  return value;
}

function publicApiKey(): string {
  const dictionary = environmentValue("SB_PUBLISHABLE_KEYS");
  if (dictionary) {
    const values = JSON.parse(dictionary) as Record<string, string>;
    const selected = values.default ?? Object.values(values)[0];
    if (selected) return selected;
  }
  return requiredEnvironmentValue("SUPABASE_ANON_KEY");
}

function responseJson(status: number, body: Record<string, unknown>): Response {
  return Response.json(body, { status, headers: CORS_HEADERS });
}

function routeFromUrl(url: URL): string {
  const marker = "/device-api";
  const markerIndex = url.pathname.indexOf(marker);
  const path = markerIndex >= 0
    ? url.pathname.slice(markerIndex + marker.length)
    : url.pathname;
  return path.replace(/\/+$/, "") || "/";
}

function bearerToken(request: Request): string | null {
  const authorization = request.headers.get("Authorization")?.trim() ?? "";
  const match = /^Bearer\s+(.+)$/i.exec(authorization);
  return match?.[1]?.trim() || null;
}

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(
    /=+$/,
    "",
  );
}

export function createOneTimeSecret(): string {
  return base64Url(crypto.getRandomValues(new Uint8Array(32)));
}

export function createShortCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const random = crypto.getRandomValues(new Uint8Array(8));
  return Array.from(random, (value) => alphabet[value % alphabet.length]).join(
    "",
  );
}

export async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(value),
  );
  return Array.from(
    new Uint8Array(digest),
    (byte) => byte.toString(16).padStart(2, "0"),
  ).join("");
}

export function buildPairingUrl(
  publicBaseUrl: string,
  environmentId: string,
  pairingId: string,
  secret: string,
): string {
  const url = new URL(
    "pair",
    publicBaseUrl.endsWith("/") ? publicBaseUrl : `${publicBaseUrl}/`,
  );
  url.searchParams.set("v", "1");
  url.searchParams.set("id", pairingId);
  url.searchParams.set("secret", secret);
  url.searchParams.set("env", environmentId);
  return url.toString();
}

async function readJson(request: Request): Promise<Record<string, unknown>> {
  const contentLength = Number(request.headers.get("Content-Length") ?? "0");
  if (contentLength > MAX_BODY_BYTES) throw new Error("request_too_large");
  const payload = await request.json();
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    throw new Error("invalid_request");
  }
  return payload as Record<string, unknown>;
}

function requiredString(
  body: Record<string, unknown>,
  name: string,
  maximumLength = 1024,
): string {
  const value = typeof body[name] === "string" ? body[name].trim() : "";
  if (!value || value.length > maximumLength) {
    throw new Error("invalid_request");
  }
  return value;
}

function requiredUuid(body: Record<string, unknown>, name: string): string {
  const value = requiredString(body, name, 36).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/
      .test(value)
  ) {
    throw new Error("invalid_request");
  }
  return value;
}

function deviceKind(body: Record<string, unknown>): string {
  const value = requiredString(body, "deviceKind", 32);
  if (!["WindowsDesktop", "MobilePwa", "AndroidApp"].includes(value)) {
    throw new Error("invalid_request");
  }
  return value;
}

async function authorize(request: Request): Promise<AuthContext> {
  const token = bearerToken(request);
  if (!token) throw new Error("not_authorized");
  const projectUrl = requiredEnvironmentValue("SUPABASE_URL");
  const publishableKey = publicApiKey();
  const admin = createClient(
    projectUrl,
    requiredEnvironmentValue("SUPABASE_SERVICE_ROLE_KEY"),
    {
      auth: { persistSession: false, autoRefreshToken: false },
    },
  );
  const result = await admin.auth.getUser(token);
  if (result.error || !result.data.user) throw new Error("not_authorized");
  const userClient = createClient(projectUrl, publishableKey, {
    global: { headers: { Authorization: `Bearer ${token}` } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  return { userId: result.data.user.id, userClient, admin };
}

async function currentMembership(
  context: AuthContext,
  deviceId?: string,
): Promise<{ profile_id: string; device_id: string } | null> {
  // Membership reads should run with the caller JWT. The database policies are
  // deliberately scoped to the authenticated profile and this also keeps the
  // request working when a project uses the newer secret-key format for the
  // administrative client.
  let query = context.userClient
    .from("device_memberships")
    .select("profile_id,device_id")
    .eq("auth_user_id", context.userId)
    .is("revoked_at", null)
    .order("last_seen_at", { ascending: false })
    .limit(1);
  if (deviceId) query = query.eq("device_id", deviceId);
  const result = await query.maybeSingle();
  if (result.error) throw result.error;
  return result.data;
}

async function bootstrap(request: Request): Promise<Response> {
  const context = await authorize(request);
  const body = await readJson(request);
  const deviceId = requiredUuid(body, "deviceId");
  const platform = deviceKind(body);
  const deviceName = requiredString(body, "deviceName", 120);
  const result = await context.userClient.rpc("ensure_device_membership", {
    requested_device_id: deviceId,
    requested_platform: platform,
    requested_device_name: deviceName,
  });
  if (result.error || !Array.isArray(result.data) || !result.data[0]) {
    throw result.error ?? new Error("bootstrap_failed");
  }
  const row = result.data[0] as Record<string, unknown>;
  return responseJson(200, {
    profileId: row.profile_id,
    hasPeerDevice: Boolean(row.has_peer_device),
    telegramLinked: Boolean(row.telegram_linked),
    latestRevision: Number(row.latest_revision ?? 0),
    serverTimeUtc: new Date().toISOString(),
  });
}

async function startPairing(request: Request): Promise<Response> {
  const context = await authorize(request);
  const body = await readJson(request);
  const deviceId = requiredUuid(body, "deviceId");
  const secret = createOneTimeSecret();
  const shortCode = createShortCode();
  const expiresAt = new Date(Date.now() + PAIRING_TTL_MS);
  const result = await context.userClient.rpc("start_device_pairing", {
    requested_device_id: deviceId,
    requested_secret_hash: await sha256Hex(secret),
    requested_short_code_hash: await sha256Hex(shortCode),
    requested_expires_at: expiresAt.toISOString(),
  });
  if (result.error || !Array.isArray(result.data) || !result.data[0]) {
    throw result.error ?? new Error("pairing_start_failed");
  }
  const row = result.data[0] as Record<string, unknown>;
  const pairingId = String(row.pairing_id);
  const environmentId = requiredEnvironmentValue("DEPLOYMENT_ENVIRONMENT_ID");
  const qrPayload = buildPairingUrl(
    requiredEnvironmentValue("PUBLIC_PAIRING_BASE_URL"),
    environmentId,
    pairingId,
    secret,
  );
  return responseJson(200, {
    pairingId,
    qrPayload,
    shortCode,
    expiresAtUtc: String(row.expires_at),
  });
}

async function pairingStatus(request: Request): Promise<Response> {
  const context = await authorize(request);
  const id = new URL(request.url).searchParams.get("id")?.toLowerCase() ?? "";
  if (!/^[0-9a-f-]{36}$/.test(id)) throw new Error("invalid_request");
  const result = await context.userClient.rpc("read_device_pairing_status", {
    requested_pairing_id: id,
  });
  if (result.error || !Array.isArray(result.data) || !result.data[0]) {
    throw result.error ?? new Error("pairing_status_failed");
  }
  const row = result.data[0] as Record<string, unknown>;
  const status = String(row.result_status);
  if (status === "not_found") {
    return responseJson(404, { error: "pairing_not_found" });
  }
  if (status === "rate_limited") {
    return responseJson(429, {
      error: "rate_limited",
      message: "Проверка выполняется слишком часто. Повторяем автоматически.",
    });
  }
  let linkedDeviceName: string | null = null;
  if (status === "completed" && row.consumed_by_auth_user_id) {
    const membership = await context.userClient
      .from("device_memberships")
      .select("device_name")
      .eq("profile_id", row.linked_profile_id)
      .eq("auth_user_id", row.consumed_by_auth_user_id)
      .is("revoked_at", null)
      .order("created_at", { ascending: false })
      .limit(1)
      .maybeSingle();
    linkedDeviceName = membership.data?.device_name ?? null;
  }
  const names: Record<string, string> = {
    pending: "Pending",
    completed: "Completed",
    expired: "Expired",
    cancelled: "Cancelled",
  };
  return responseJson(200, {
    status: names[status] ?? "Cancelled",
    profileId: status === "completed" ? row.linked_profile_id : null,
    linkedDeviceName,
    expiresAtUtc: row.request_expires_at,
  });
}

async function completePairing(
  request: Request,
  shortCodeMode: boolean,
): Promise<Response> {
  const context = await authorize(request);
  const body = await readJson(request);
  const deviceId = requiredUuid(body, "deviceId");
  const platform = deviceKind(body);
  const deviceName = requiredString(body, "deviceName", 120);
  const pairingId = shortCodeMode ? null : requiredUuid(body, "pairingId");
  const supplied = shortCodeMode
    ? requiredString(body, "shortCode", 16).replaceAll(/[^A-Za-z0-9]/g, "")
      .toUpperCase()
    : requiredString(body, "secret", 1024);
  const hash = await sha256Hex(supplied);
  const result = await context.userClient.rpc("complete_device_pairing", {
    requested_pairing_id: pairingId,
    requested_secret_hash: shortCodeMode ? "" : hash,
    requested_short_code_hash: shortCodeMode ? hash : "",
    requested_device_id: deviceId,
    requested_platform: platform,
    requested_device_name: deviceName,
  });
  if (result.error || !Array.isArray(result.data) || !result.data[0]) {
    throw result.error ?? new Error("pairing_complete_failed");
  }
  const row = result.data[0] as Record<string, unknown>;
  const status = String(row.result_status);
  if (status !== "completed") {
    const messages: Record<string, string> = {
      expired: "QR-код истёк. Покажите новый код на компьютере.",
      replayed: "Этот QR-код уже использован.",
      rate_limited: "Слишком много попыток. Подождите и повторите.",
      same_device: "Нельзя привязать устройство к самому себе.",
      invalid: "QR-код не подходит или уже недействителен.",
    };
    return responseJson(status === "rate_limited" ? 429 : 409, {
      error: "pairing_rejected",
      message: messages[status] ?? messages.invalid,
    });
  }
  return responseJson(200, {
    profileId: row.linked_profile_id,
    latestRevision: Number(row.latest_revision ?? 0),
    linkedDeviceName: deviceName,
  });
}

function validStudySession(
  value: unknown,
): value is StudySession & Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const session = value as StudySession;
  return typeof session.sessionId === "string" &&
    typeof session.deviceId === "string" &&
    typeof session.deviceKind === "string" &&
    typeof session.mode === "string" &&
    typeof session.startedAtUtc === "string" &&
    typeof session.completedAtUtc === "string" &&
    typeof session.outcome === "string" &&
    typeof session.bankVersion === "string" &&
    typeof session.bankSha256 === "string" &&
    /^[0-9a-f]{64}$/i.test(session.bankSha256) &&
    typeof session.rulesProfile === "string" &&
    typeof session.schemaVersion === "number" &&
    typeof session.payloadSha256 === "string" &&
    /^[0-9a-f]{64}$/i.test(session.payloadSha256);
}

async function syncPush(request: Request): Promise<Response> {
  const context = await authorize(request);
  const body = await readJson(request);
  const session = body.session;
  if (!validStudySession(session)) throw new Error("invalid_session");
  const membership = await currentMembership(context, session.deviceId);
  if (!membership) return responseJson(403, { error: "device_not_linked" });
  const row = {
    session_id: session.sessionId,
    profile_id: membership.profile_id,
    user_id: context.userId,
    device_id: session.deviceId,
    device_kind: session.deviceKind,
    mode: session.mode,
    started_at: session.startedAtUtc,
    completed_at: session.completedAtUtc,
    outcome: session.outcome,
    bank_version: session.bankVersion,
    bank_sha256: session.bankSha256.toUpperCase(),
    rules_profile: session.rulesProfile,
    schema_version: session.schemaVersion,
    payload: session,
    payload_sha256: session.payloadSha256.toUpperCase(),
  };
  const inserted = await context.userClient
    .from("study_sessions")
    .insert(row)
    .select("server_seq")
    .maybeSingle();
  if (inserted.error?.code === "23505") {
    const existing = await context.userClient
      .from("study_sessions")
      .select("payload_sha256")
      .eq("session_id", session.sessionId)
      .eq("profile_id", membership.profile_id)
      .maybeSingle();
    const same = existing.data?.payload_sha256?.toUpperCase() ===
      session.payloadSha256.toUpperCase();
    return responseJson(200, {
      disposition: same ? "AlreadyExists" : "IntegrityConflict",
      message: same ? "" : "Session identifier has different content.",
    });
  }
  if (inserted.error || !inserted.data) {
    throw inserted.error ?? new Error("sync_push_failed");
  }
  await context.admin
    .from("learning_profiles")
    .update({ latest_revision: inserted.data.server_seq })
    .eq("id", membership.profile_id)
    .lt("latest_revision", inserted.data.server_seq);
  if (session.mode === "Exam") {
    try {
      const projectUrl = requiredEnvironmentValue("SUPABASE_URL");
      await fetch(new URL("functions/v1/telegram-report", `${projectUrl}/`), {
        method: "POST",
        headers: {
          Authorization: request.headers.get("Authorization") ?? "",
          apikey: publicApiKey(),
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ sessionId: session.sessionId }),
      });
    } catch {
      // The database trigger already created a durable delivery row.
    }
  }
  return responseJson(200, { disposition: "Uploaded", message: "" });
}

async function syncPull(request: Request): Promise<Response> {
  const context = await authorize(request);
  const membership = await currentMembership(context);
  if (!membership) return responseJson(403, { error: "device_not_linked" });
  const url = new URL(request.url);
  const after = Math.max(
    0,
    Number.parseInt(url.searchParams.get("after") ?? "0", 10) || 0,
  );
  const limit = Math.min(
    500,
    Math.max(
      1,
      Number.parseInt(url.searchParams.get("limit") ?? "100", 10) || 100,
    ),
  );
  const result = await context.userClient
    .from("study_sessions")
    .select("server_seq,payload")
    .eq("profile_id", membership.profile_id)
    .gt("server_seq", after)
    .order("server_seq", { ascending: true })
    .limit(limit);
  if (result.error) throw result.error;
  const items = (result.data ?? []).map((row) => ({
    serverSequence: row.server_seq,
    session: row.payload,
  }));
  return responseJson(200, { items, hasMore: items.length === limit });
}

async function listDevices(request: Request): Promise<Response> {
  const context = await authorize(request);
  const membership = await currentMembership(context);
  if (!membership) return responseJson(403, { error: "device_not_linked" });
  const result = await context.userClient
    .from("device_memberships")
    .select(
      "device_id,platform,device_name,created_at,last_seen_at,auth_user_id",
    )
    .eq("profile_id", membership.profile_id)
    .is("revoked_at", null)
    .order("created_at", { ascending: true });
  if (result.error) throw result.error;
  return responseJson(200, {
    items: (result.data ?? []).map((row) => ({
      deviceId: row.device_id,
      deviceKind: row.platform,
      deviceName: row.device_name,
      createdAtUtc: row.created_at,
      lastSeenAtUtc: row.last_seen_at,
      isCurrentDevice: row.auth_user_id === context.userId &&
        row.device_id === membership.device_id,
    })),
  });
}

async function revokeDevice(request: Request): Promise<Response> {
  const context = await authorize(request);
  const body = await readJson(request);
  const targetDeviceId = requiredUuid(body, "deviceId");
  const membership = await currentMembership(context);
  if (!membership) return responseJson(403, { error: "device_not_linked" });
  const result = await context.admin
    .from("device_memberships")
    .update({ revoked_at: new Date().toISOString() })
    .eq("profile_id", membership.profile_id)
    .eq("device_id", targetDeviceId)
    .is("revoked_at", null)
    .select("id");
  if (result.error) throw result.error;
  return responseJson(200, { ok: true });
}

async function startTelegramLink(request: Request): Promise<Response> {
  const context = await authorize(request);
  const membership = await currentMembership(context);
  if (!membership) return responseJson(403, { error: "device_not_linked" });
  const token = createOneTimeSecret();
  const expiresAt = new Date(Date.now() + 10 * 60 * 1000);
  const inserted = await context.admin.from("telegram_link_tokens").insert({
    profile_id: membership.profile_id,
    token_hash: await sha256Hex(token),
    expires_at: expiresAt.toISOString(),
  });
  if (inserted.error) throw inserted.error;
  const username = requiredEnvironmentValue("TELEGRAM_BOT_USERNAME").replace(
    /^@/,
    "",
  );
  return responseJson(200, {
    deepLink: `https://t.me/${encodeURIComponent(username)}?start=${
      encodeURIComponent(token)
    }`,
    expiresAtUtc: expiresAt.toISOString(),
  });
}

function health(request: Request): Response {
  const configuredEnvironment = requiredEnvironmentValue(
    "DEPLOYMENT_ENVIRONMENT_ID",
  );
  const requestedEnvironment = request.headers.get("X-Environment-Id")?.trim();
  if (requestedEnvironment && requestedEnvironment !== configuredEnvironment) {
    return responseJson(409, { error: "environment_mismatch" });
  }
  return responseJson(200, {
    status: "ok",
    apiVersion: API_VERSION,
    minimumClientVersion: environmentValue("MINIMUM_CLIENT_VERSION", "2.0.2"),
    bankVersion: environmentValue("BANK_VERSION", "ab-2025-05-26"),
    environmentId: configuredEnvironment,
    serverTimeUtc: new Date().toISOString(),
  });
}

function publicError(
  error: unknown,
): { status: number; code: string; message: string } {
  const code = error instanceof Error ? error.message : "request_failed";
  if (code === "not_authorized") {
    return {
      status: 401,
      code,
      message: "Связь устройства устарела. Перезапустите приложение.",
    };
  }
  if (code === "request_too_large") {
    return { status: 413, code, message: "Запрос слишком большой." };
  }
  if (["invalid_request", "invalid_session"].includes(code)) {
    return { status: 400, code, message: "Данные запроса повреждены." };
  }
  if (code.toLowerCase().includes("rate limit")) {
    return {
      status: 429,
      code: "rate_limited",
      message: "Слишком много попыток. Повторите позже.",
    };
  }
  return {
    status: 503,
    code: "temporarily_unavailable",
    message: "Не удалось синхронизировать. Повторить.",
  };
}

export async function handleDeviceApi(request: Request): Promise<Response> {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: CORS_HEADERS });
  }
  const route = routeFromUrl(new URL(request.url));
  try {
    if (route === "/health" && request.method === "GET") return health(request);
    if (route === "/identity/bootstrap" && request.method === "POST") {
      return await bootstrap(request);
    }
    if (route === "/pairing/start" && request.method === "POST") {
      return await startPairing(request);
    }
    if (route === "/pairing/status" && request.method === "GET") {
      return await pairingStatus(request);
    }
    if (route === "/pairing/complete" && request.method === "POST") {
      return await completePairing(request, false);
    }
    if (route === "/pairing/complete-code" && request.method === "POST") {
      return await completePairing(request, true);
    }
    if (route === "/sync/push" && request.method === "POST") {
      return await syncPush(request);
    }
    if (route === "/sync/pull" && request.method === "GET") {
      return await syncPull(request);
    }
    if (route === "/devices/list" && request.method === "GET") {
      return await listDevices(request);
    }
    if (route === "/devices/revoke" && request.method === "POST") {
      return await revokeDevice(request);
    }
    if (route === "/telegram/link" && request.method === "POST") {
      return await startTelegramLink(request);
    }
    return responseJson(404, { error: "not_found" });
  } catch (error) {
    const safe = publicError(error);
    return responseJson(safe.status, {
      error: safe.code,
      message: safe.message,
    });
  }
}

if (import.meta.main) {
  Deno.serve(handleDeviceApi);
}
