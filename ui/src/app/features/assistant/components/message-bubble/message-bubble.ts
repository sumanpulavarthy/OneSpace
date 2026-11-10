import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import type { SourceSystem } from '../../../../graphql/generated';
import type { ChatMessage } from '../../data-access/assistant.store';

const SYSTEM_LABELS: Record<SourceSystem, string> = {
  ZOHO_PEOPLE: 'Zoho People',
  COMPLY_KARO: 'ComplyKaro',
  MICROSOFT365: 'Microsoft 365',
  ONE_SPACE: 'OneSpace',
};

/** One chat message. Assistant answers show where the information came from (FR-1 attribution). */
@Component({
  selector: 'app-message-bubble',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './message-bubble.html',
  styleUrl: './message-bubble.scss',
})
export class MessageBubble {
  readonly message = input.required<ChatMessage>();
  /** Disables the connect button while a connection is starting. */
  readonly connecting = input(false);
  readonly connectZoho = output<void>();

  protected readonly sources = computed(() => {
    const m = this.message();
    return m.role === 'assistant' ? m.answer.sources : [];
  });

  protected readonly blockedCount = computed(() => {
    const m = this.message();
    return m.role === 'assistant' ? m.answer.toolCalls.filter(t => t.outcome === 'BLOCKED').length : 0;
  });

  /** The answer needed Zoho People data the employee hasn't granted access to yet. */
  protected readonly needsZoho = computed(() => {
    const m = this.message();
    return m.role === 'assistant' && m.answer.toolCalls.some(t => t.outcome === 'CONNECTION_REQUIRED' && t.system === 'ZOHO_PEOPLE');
  });

  protected systemLabel(system: SourceSystem): string {
    return SYSTEM_LABELS[system];
  }
}
