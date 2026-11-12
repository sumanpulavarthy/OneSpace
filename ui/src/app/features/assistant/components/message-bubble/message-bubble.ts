import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import type { SourceSystem } from '../../../../graphql/generated';
import { type ChatMessage, SYSTEM_LABELS } from '../../data-access/assistant.store';
import { AnswerBlock } from '../answer-block/answer-block';

/**
 * One chat message. Assistant answers show a short narrative, then tables/cards built from the source data
 * (FR-6), and where the information came from (FR-1 attribution).
 */
@Component({
  selector: 'app-message-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AnswerBlock],
  templateUrl: './message-bubble.html',
  styleUrl: './message-bubble.scss',
})
export class MessageBubble {
  readonly message = input.required<ChatMessage>();
  /** Disables the connect button while a connection is starting. */
  readonly connecting = input(false);
  readonly connectZoho = output<void>();

  private readonly answer = computed(() => {
    const m = this.message();
    return m.role === 'assistant' ? m.answer : null;
  });

  protected readonly sources = computed(() => this.answer()?.sources ?? []);
  protected readonly blocks = computed(() => this.answer()?.blocks ?? []);

  /** The grounding check found figures in the narrative that are not in the source data. */
  protected readonly ungrounded = computed(() => {
    const grounding = this.answer()?.grounding;
    return !!grounding && grounding.checked && !grounding.grounded;
  });

  protected readonly draft = computed(() => {
    const m = this.message();
    return m.role === 'pending' ? m.draft : '';
  });

  protected readonly activity = computed(() => {
    const m = this.message();
    return m.role === 'pending' ? m.activity : null;
  });

  protected readonly blockedCount = computed(() => this.answer()?.toolCalls.filter(t => t.outcome === 'BLOCKED').length ?? 0);

  /** The answer needed Zoho People data the employee hasn't granted access to yet. */
  protected readonly needsZoho = computed(
    () => this.answer()?.toolCalls.some(t => t.outcome === 'CONNECTION_REQUIRED' && t.system === 'ZOHO_PEOPLE') ?? false,
  );

  protected systemLabel(system: SourceSystem): string {
    return SYSTEM_LABELS[system];
  }
}
