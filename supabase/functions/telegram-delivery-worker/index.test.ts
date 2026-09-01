import {
  constantTimeEqual,
  DELIVERY_RETRY_STATUSES,
  handleTelegramDeliveryWorker,
} from "./index.ts";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

Deno.test("worker secret comparison rejects different lengths and values", () => {
  assert(
    constantTimeEqual("0123456789abcdef", "0123456789abcdef"),
    "equal secret rejected",
  );
  assert(
    !constantTimeEqual("0123456789abcdef", "0123456789abcdeg"),
    "different secret accepted",
  );
  assert(
    !constantTimeEqual("0123456789abcdef", "short"),
    "short secret accepted",
  );
});

Deno.test("worker rejects non-POST without reading secrets", async () => {
  const response = await handleTelegramDeliveryWorker(
    new Request("https://local", { method: "GET" }),
  );
  assert(response.status === 405, "unexpected method status");
});

Deno.test("worker retries abandoned sending claims", () => {
  assert(
    DELIVERY_RETRY_STATUSES.includes("sending"),
    "expired sending claims would remain stuck forever",
  );
});
