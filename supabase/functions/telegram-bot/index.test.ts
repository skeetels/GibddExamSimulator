import { commandFromText, isOwnerPrivateMessage } from "./index.ts";

Deno.test("only the fixed owner private account is accepted", () => {
  if (
    !isOwnerPrivateMessage({
      chat: { id: 123, type: "private" },
      from: { username: "Skeetels" },
    })
  ) {
    throw new Error("The fixed owner should be accepted.");
  }
  if (
    isOwnerPrivateMessage({
      chat: { id: 123, type: "private" },
      from: { username: "someone_else" },
    })
  ) {
    throw new Error("Another username must not be accepted.");
  }
  if (
    isOwnerPrivateMessage({
      chat: { id: 123, type: "group" },
      from: { username: "skeetels" },
    })
  ) {
    throw new Error("A group chat must not be accepted.");
  }
});

Deno.test("bot-qualified Telegram commands are normalized", () => {
  if (commandFromText(" /stats@my_bot now ") !== "/stats") {
    throw new Error("Command normalization failed.");
  }
});
