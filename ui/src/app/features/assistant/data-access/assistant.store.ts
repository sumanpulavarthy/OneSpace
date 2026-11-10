import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GraphQLRequestError, GraphqlClient } from '../../../core/graphql/graphql.client';
import type { AskMutation, AskMutationVariables, MeQuery } from '../../../graphql/generated';
import { AskDocument, MeDocument } from './assistant.operations';

export type AssistantAnswer = AskMutation['ask'];

export type ChatMessage =
  | { readonly id: number; readonly role: 'user'; readonly text: string }
  | { readonly id: number; readonly role: 'assistant'; readonly answer: AssistantAnswer }
  | { readonly id: number; readonly role: 'error'; readonly text: string };

export const MAX_QUESTION_LENGTH = 2000;

/** Conversation state for the assistant page (signals; one instance per page). */
@Injectable()
export class AssistantStore {
  private readonly graphql = inject(GraphqlClient);
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
    this.pendingCount.update(n => n + 1);
    try {
      const data = await firstValueFrom(
        this.graphql.request<AskMutation, AskMutationVariables>(AskDocument, {
          input: { question: text, sessionId: this.sessionId() },
        }),
      );
      this.sessionId.set(data.ask.sessionId);
      this.push({ role: 'assistant', answer: data.ask });
    } catch (error) {
      this.push({ role: 'error', text: describe(error) });
    } finally {
      this.pendingCount.update(n => n - 1);
    }
  }

  reset(): void {
    this.messageList.set([]);
    this.sessionId.set(null);
  }

  private push(message: Omit<ChatMessage, 'id'> & Partial<ChatMessage>): void {
    this.messageList.update(list => [...list, { ...message, id: this.nextId++ } as ChatMessage]);
  }
}

function describe(error: unknown): string {
  if (error instanceof GraphQLRequestError) {
    return error.code === 'BAD_USER_INPUT' ? error.message : `${error.message}${error.traceId ? ` (ref ${error.traceId.slice(0, 8)})` : ''}`;
  }
  return 'OneSpace could not be reached. Check your connection and try again.';
}
