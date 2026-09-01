import {
  buildPairingUrl,
  createOneTimeSecret,
  createShortCode,
  handleDeviceApi,
  sha256Hex,
} from "./index.ts";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

Deno.test("pairing secret has at least 256 bits and is URL safe", () => {
  const values = new Set(
    Array.from({ length: 64 }, () => createOneTimeSecret()),
  );
  assert(values.size === 64, "secrets must be unique");
  for (const value of values) {
    assert(
      value.length >= 43,
      "32 random bytes must remain in the encoded token",
    );
    assert(/^[A-Za-z0-9_-]+$/.test(value), "secret must be URL safe");
  }
});

Deno.test("manual code avoids ambiguous characters", () => {
  for (let index = 0; index < 64; index++) {
    const value = createShortCode();
    assert(/^[A-HJ-NP-Z2-9]{8}$/.test(value), `unexpected code: ${value}`);
  }
});

Deno.test("QR contains only invitation data", () => {
  const qr = buildPairingUrl(
    "https://study.example.test/app/",
    "production",
    "6db7ad64-fb2d-4d07-8a67-9d90b56d133a",
    "temporary_secret",
  );
  const url = new URL(qr);
  assert(url.searchParams.get("v") === "1", "protocol version missing");
  assert(url.searchParams.get("env") === "production", "environment missing");
  assert(
    url.searchParams.get("secret") === "temporary_secret",
    "one-time secret missing",
  );
  assert(
    !qr.toLowerCase().includes("token="),
    "permanent token field is forbidden",
  );
  assert(
    !qr.toLowerCase().includes("service_role"),
    "service role is forbidden",
  );
});

Deno.test("hash is stable and does not expose the secret", async () => {
  const hash = await sha256Hex("temporary_secret");
  assert(
    hash === "52e8a79afc90375ff6431a4cf26080d09e2cf64f24cf2ed4805464378af24c1c",
    "unexpected SHA-256",
  );
  assert(!hash.includes("temporary_secret"), "hash leaked input");
});

Deno.test({
  name: "health has no credentials",
  permissions: { env: true },
  async fn() {
    Deno.env.set("DEPLOYMENT_ENVIRONMENT_ID", "test");
    const response = await handleDeviceApi(
      new Request("https://local/functions/v1/device-api/health"),
    );
    const text = await response.text();
    assert(response.status === 200, text);
    assert(text.includes('"environmentId":"test"'), text);
    assert(!/token|secret|service.role/i.test(text), text);
  },
});
