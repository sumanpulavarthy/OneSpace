import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GraphQLRequestError, GraphqlClient } from '../../../core/graphql/graphql.client';
import type { AskMutation, AskMutationVariables, MeQuery, SourceSystem } from '../../../graphql/generated';
import { AssistantStreamClient } from './assistant-stream.client';
import { AskDocument, MeDocument } from './assistant.operations';

export type AssistantAnswer = AskMutation['ask'];
export type AnswerBlock = AssistantAnswer['blocks'][number];

export type ChatMessage =
  | { readonly id: number; readonly role: 'user'; readonly text: string }
  | { readonly id: number; readonly role: 'assistant'; readonly answer: AssistantAnswer }
  /** An answer still being produced: streamed text so far and what the assistant is doing. */
  | { readonly id: number; readonly role: 'pending'; readonly draft: string; readonly activity: string | null }
  | { readonly id: number; readonly role: 'error'; readonly text: string };

export const MAX_QUESTION_LENGTH = 2000;

export const SYSTEM_LABELS: Record<SourceSystem, string> = {
  ZOHO_PEOPLE: 'Zoho People',
  COMPLY_KARO: 'ComplyKaro',
  MICROSOFT365: 'Microsoft 365',
  ONE_SPACE: 'OneSpace',
};

type NewMessage =
  | { readonly role: 'user'; readonly text: string }
  | { readonly role: 'assistant'; readonly answer: AssistantAnswer }
  | { readonly role: 'pending'; readonly draft: string; readonly activity: string | null }
  | { readonly role: 'error'; readonly text: string };

/** Conversation state for the assistant page (signals; one instance per page). */
@Injectable()
export class AssistantStore {
  private readonly graphql = inject(GraphqlClient);
  private readonly streamClient = inject(AssistantStreamClient);
  private readonly messageList = signal<readonly ChatMessage[]>([]);
  private readonly sessionId = signal<string | null>(null);
  private readonly pendingCount = signal(0);
  private readonly displayName = signal<string | null>(null);
  private nextId = 1;

  readonly messages = this.messageList.asReadonly();
  readonly pending = computed(() => this.pendingCount() > 0);
  readonly isEmpty = computed(() => this.messageList().length === 0);
  readonly userName = this.displayName.asReadonly();

  async loadUser(): Promise<void> {
    try {
      const data = await firstValueFrom(this.graphql.request<MeQuery>(MeDocument));
      this.displayName.set(data.me.displayName);
    } catch {
      // The greeting is cosmetic; failures surface on the first question instead.
    }
  }

  async ask(question: string): Promise<void> {
    const text = question.trim();
    if (!text || this.pending()) return;
    if (text.length > MAX_QUESTION_LENGTH) {
      this.push({ role: 'error', text: `Questions are limited to ${MAX_QUESTION_LENGTH} characters.` });
      return;
    }

    this.push({ role: 'user', text });
    const pendingId = this.push({ role: 'pending', draft: '', activity: null });
    this.pendingCount.update(n => n + 1);
    try {
      const answer = (await this.askStreaming(text, pendingId)) ?? (await this.askOnce(text));
      this.sessionId.set(answer.sessionId);
      this.replace(pendingId, { role: 'assistant', answer });
    } catch (error) {
      this.replace(pendingId, { role: 'error', text: describe(error) });
    } finally {
      this.pendingCount.update(n => n - 1);
    }
  }

  reset(): void {
    this.messageList.set([]);
    this.sessionId.set(null);
  }

  /**
   * Streams the answer into the pending message. Returns null when the stream could not be started, so the caller
   * falls back to the GraphQL mutation; errors after the stream started are real failures.
   */
  private async askStreaming(question: string, pendingId: number): Promise<AssistantAnswer | null> {
    // An object, not locals: TypeScript does not see assignments made inside the callback.
    const state: { started: boolean; answer: AssistantAnswer | null; failure: GraphQLRequestError | null } = {
      started: false,
      answer: null,
      failure: null,
    };
    try {
      await this.streamClient.stream({ question, sessionId: this.sessionId() }, event => {
        state.started = true;
        switch (event.type) {
          case 'delta':
            this.updatePending(pendingId, p => ({ ...p, draft: p.draft + event.text }));
            break;
          case 'tool':
            this.updatePending(pendingId, p => ({
              ...p,
              activity: event.status === 'started' ? `Checking ${SYSTEM_LABELS[event.system] ?? 'sources'}…` : p.activity,
            }));
            break;
          case 'restart':
            this.updatePending(pendingId, () => ({ draft: '', activity: event.reason }));
            break;
          case 'completed':
            state.answer = event.answer;
            break;
          case 'error':
            state.failure = new GraphQLRequestError(event.message, event.code, event.traceId ?? undefined);
            break;
        }
      });
    } catch (error) {
      if (!state.started) return null;
      throw error;
    }
    if (state.failure) throw state.failure;
    if (!state.answer) {
      if (!state.started) return null;
      throw new Error('The answer was interrupted.');
    }
    return state.answer;
  }

  private async askOnce(question: string): Promise<AssistantAnswer> {
    const data = await firstValueFrom(
      this.graphql.request<AskMutation, AskMutationVariables>(AskDocument, {
        input: { question, sessionId: this.sessionId() },
      }),
    );
    return data.ask;
  }

  private push(message: NewMessage): number {
    const id = this.nextId++;
    this.messageList.update(list => [...list, { ...message, id } as ChatMessage]);
    return id;
  }

  private replace(id: number, message: NewMessage): void {
    this.messageList.update(list => list.map(m => (m.id === id ? ({ ...message, id } as ChatMessage) : m)));
  }

  private updatePending(id: number, change: (p: { draft: string; activity: string | null }) => { draft: string; activity: string | null }): void {
    this.messageList.update(list =>
      list.map(m => (m.id === id && m.role === 'pending' ? { ...m, ...change({ draft: m.draft, activity: m.activity }) } : m)),
    );
  }
}

function describe(error: unknown): string {
  if (error instanceof GraphQLRequestError) {
    return error.code === 'BAD_USER_INPUT' ? error.message : `${error.message}${error.traceId ? ` (ref ${error.traceId.slice(0, 8)})` : ''}`;
  }
  return 'OneSpace could not be reached. Check your connection and try again.';
}
