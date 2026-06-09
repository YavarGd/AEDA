import * as net from "node:net";
import { EditorContextEnvelope, serializeEnvelope } from "./contracts";

export interface PipeResponse {
  ok: boolean;
  message: string;
}

export async function sendEnvelope(
  pipeName: string,
  envelope: EditorContextEnvelope,
  timeoutMs = 2500
): Promise<PipeResponse> {
  const payload = serializeEnvelope(envelope);

  return new Promise((resolve, reject) => {
    const socket = net.createConnection(pipeName);
    let response = "";
    const timeout = setTimeout(() => {
      socket.destroy();
      reject(new Error("Timed out connecting to PersonalAI."));
    }, timeoutMs);

    socket.setEncoding("utf8");

    socket.on("connect", () => {
      socket.write(payload);
    });

    socket.on("data", chunk => {
      response += chunk;

      if (response.includes("\n")) {
        socket.end();
      }
    });

    socket.on("error", error => {
      clearTimeout(timeout);
      reject(error);
    });

    socket.on("close", () => {
      clearTimeout(timeout);

      if (!response) {
        reject(new Error("PersonalAI closed the pipe without a response."));
        return;
      }

      try {
        resolve(JSON.parse(response.trim()) as PipeResponse);
      } catch {
        reject(new Error("PersonalAI returned an invalid response."));
      }
    });
  });
}
