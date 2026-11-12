import { Injectable, inject } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { RuntimeConfigService } from '../../../core/config/runtime-config.service';
import type { AskInput, AskMutation, SourceSystem } from '../../../graphql/generated';

/** Events from POST /assistant/stream (Server-Sent Events). `completed` carries the GraphQL AssistantAnswer shape. */
export type AssistantStreamEvent =
  | { readonly type: 'delta'; readonly text: string }
  | { readonly type: 'tool'; readonly tool: string; readonly system: SourceSystem; readonly status: string }
  | { readonly type: 'restart'; readonly reason: string }
  | { readonly type: 'completed'; readonly answer: AskMutation['ask'] }
  | { readonly type: 'error'; readonly message: string; readonly code: string; readonly traceId: string | null };

/** The stream could not be opened (endpoint missing, auth, network); the caller may fall back to GraphQL. */
export class StreamUnavailableError extends Error {
  constructor(readonly status: number) {
    super(`Streaming unavailable (${status})`);
  }
}

/**
 * Reads the assistant's streaming answer. Uses fetch rather than EventSource because the request is a POST
 * with a bearer token.
 */
@Injectable({ providedIn: 'root' })
export class AssistantStreamClient {
  private readonly runtime = inject(RuntimeConfigService);
  private readonly auth = inject(AuthService);

  async stream(input: AskInput, onEvent: (event: AssistantStreamEvent) => void, signal?: AbortSignal): Promise<void> {
    const token = await this.auth.getAccessToken();
    const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'text/event-stream' };
    if (token) headers['Authorization'] = `Bearer ${token}`;

    let response: Response;
    try {
      response = await fetch(`${this.runtime.apiBaseUrl()}/assistant/stream`, {
        method: 'POST',
        headers,
        body: JSON.stringify(input),
        signal,
      });
    } catch {
      throw new StreamUnavailableError(0);
    }
    if (!response.ok || !response.body) throw new StreamUnavailableError(response.status);

    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += value.replace(/\r\n/g, '\n');
      let end: number;
      while ((end = buffer.indexOf('\n\n')) >= 0) {
        const event = parseSseEvent(buffer.slice(0, end));
        buffer = buffer.slice(end + 2);
        if (event) onEvent(event);
      }
    }
  }
}

/** Parses one SSE event block ("event: x\ndata: {...}"). Unknown or malformed events are ignored. */
export function parseSseEvent(block: string): AssistantStreamEvent | null {
  let type = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line.startsWith('event:')) type = line.slice(6).trim();
    else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));
  }
  if (data.length === 0) return null;
  try {
    const payload = JSON.parse(data.join('\n')) as Record<string, unknown>;
    return { ...payload, type } as AssistantStreamEvent;
  } catch {
    return null;
  }
}
