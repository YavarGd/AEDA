import * as net from "net";
import { EditorContextEnvelope, serializeEnvelope } from "./contracts";

export interface PipeResponse {
    Ok: boolean;
    Message: string;
}

/**
 * Sends an EditorContextEnvelope to the PersonalAI desktop app over a named pipe
 * and returns the parsed PipeResponse.
 */
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

        socket.on("data", (chunk: string) => {
            // Because we called setEncoding("utf8") above, chunk is a string.
            response += chunk;
            if (response.includes("\n")) {
                socket.end();
            }
        });

        socket.on("error", (err: NodeJS.ErrnoException) => {
            clearTimeout(timeout);
            reject(err);
        });

        socket.on("close", (had_error: number) => {
            clearTimeout(timeout);
            if (!response) {
                reject(new Error("PersonalAI closed the pipe without a response."));
                return;
            }
            try {
                const parsed = JSON.parse(response.trim()) as PipeResponse;
                resolve(parsed);
            } catch (e) {
                reject(new Error("PersonalAI returned an invalid response."));
            }
        });
    });
}
