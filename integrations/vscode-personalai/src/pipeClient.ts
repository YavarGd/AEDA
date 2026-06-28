import * as net from "net";
import { EditorContextEnvelope, serializeEnvelope } from "./contracts";

export interface PipeResponse {
    Ok: boolean;
    Message: string;
}

export interface PipeTimeoutOptions {
    connectTimeoutMs: number;
    sendTimeoutMs: number;
    responseTimeoutMs: number;
}

export type PipeTimeoutPhase = "connect" | "send" | "response";

export class PipeTimeoutError extends Error {
    constructor(public readonly phase: PipeTimeoutPhase) {
        super(createPipeTimeoutMessage(phase));
        this.name = "PipeTimeoutError";
    }
}

export const defaultPipeTimeoutOptions: PipeTimeoutOptions = {
    connectTimeoutMs: 3000,
    sendTimeoutMs: 5000,
    responseTimeoutMs: 10000
};

export function createPipeTimeoutMessage(phase: PipeTimeoutPhase): string {
    switch (phase) {
        case "connect":
            return "Timed out connecting to PersonalAI.";
        case "send":
            return "Timed out sending context to PersonalAI.";
        case "response":
            return "AEDA received the code, but timed out while generating the response.";
    }
}

/**
 * Sends an EditorContextEnvelope to the PersonalAI desktop app over a named pipe
 * and returns the parsed PipeResponse.
 */
export async function sendEnvelope(
    pipeName: string,
    envelope: EditorContextEnvelope,
    timeoutOptions: Partial<PipeTimeoutOptions> = {}
): Promise<PipeResponse> {
    const payload = serializeEnvelope(envelope);
    const options = normalizeTimeoutOptions(timeoutOptions);

    return new Promise((resolve, reject) => {
        const socket = net.createConnection(pipeName);
        let response = "";
        let settled = false;
        let timeout: NodeJS.Timeout | undefined;

        const clearActiveTimeout = () => {
            if (timeout) {
                clearTimeout(timeout);
                timeout = undefined;
            }
        };

        const fail = (error: Error) => {
            if (settled) {
                return;
            }

            settled = true;
            clearActiveTimeout();
            socket.destroy();
            reject(error);
        };

        const armTimeout = (phase: PipeTimeoutPhase, timeoutMs: number) => {
            clearActiveTimeout();
            timeout = setTimeout(() => {
                fail(new PipeTimeoutError(phase));
            }, timeoutMs);
        };

        socket.setEncoding("utf8");
        armTimeout("connect", options.connectTimeoutMs);

        socket.on("connect", () => {
            armTimeout("send", options.sendTimeoutMs);
            socket.write(payload, "utf8", (error?: Error | null) => {
                if (error) {
                    fail(error);
                    return;
                }

                armTimeout("response", options.responseTimeoutMs);
            });
        });

        socket.on("data", (chunk: string) => {
            if (settled) {
                return;
            }

            // Because we called setEncoding("utf8") above, chunk is a string.
            response += chunk;
            if (response.includes("\n")) {
                clearActiveTimeout();
                socket.end();
            }
        });

        socket.on("error", (err: NodeJS.ErrnoException) => {
            fail(err);
        });

        socket.on("close", (had_error: number) => {
            if (settled) {
                return;
            }

            settled = true;
            clearActiveTimeout();
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

function normalizeTimeoutOptions(
    timeoutOptions: Partial<PipeTimeoutOptions>
): PipeTimeoutOptions {
    return {
        connectTimeoutMs: positiveOrDefault(
            timeoutOptions.connectTimeoutMs,
            defaultPipeTimeoutOptions.connectTimeoutMs),
        sendTimeoutMs: positiveOrDefault(
            timeoutOptions.sendTimeoutMs,
            defaultPipeTimeoutOptions.sendTimeoutMs),
        responseTimeoutMs: positiveOrDefault(
            timeoutOptions.responseTimeoutMs,
            defaultPipeTimeoutOptions.responseTimeoutMs)
    };
}

function positiveOrDefault(value: number | undefined, fallback: number): number {
    return typeof value === "number" && Number.isFinite(value) && value > 0
        ? value
        : fallback;
}
