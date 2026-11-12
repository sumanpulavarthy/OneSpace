import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import type { FieldFormat } from '../../../../graphql/generated';
import { type AnswerBlock as Block, SYSTEM_LABELS } from '../../data-access/assistant.store';
import { formatValue, isNumeric, safeLink } from './format-value';

/**
 * A table, card or note built by the API from source data (never written by the LLM), so the figures shown
 * are exactly what Zoho People, ComplyKaro or Microsoft 365 returned.
 */
@Component({
  selector: 'app-answer-block',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './answer-block.html',
  styleUrl: './answer-block.scss',
})
export class AnswerBlock {
  readonly block = input.required<Block>();

  private readonly locale = inject(DOCUMENT).defaultView?.navigator.language || 'en-US';

  protected readonly truncated = computed(() => this.block().totalRows > this.block().rows.length);
  protected readonly sourceLabel = computed(() => {
    const source = this.block().source;
    return source ? SYSTEM_LABELS[source.system] : null;
  });

  protected format(value: string, format: FieldFormat): string {
    return formatValue(value, format, this.locale);
  }

  protected link(value: string): string | null {
    return safeLink(value);
  }

  protected numeric(format: FieldFormat): boolean {
    return isNumeric(format);
  }
}
