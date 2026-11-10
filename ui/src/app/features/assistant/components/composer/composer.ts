import { ChangeDetectionStrategy, Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MAX_QUESTION_LENGTH } from '../../data-access/assistant.store';

/** Question input. Enter sends, Shift+Enter adds a new line. */
@Component({
  selector: 'app-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="composer" (submit)="$event.preventDefault(); send()">
      <label class="visually-hidden" for="question">Ask OneSpace</label>
      <textarea
        #box
        id="question"
        rows="1"
        [maxLength]="maxLength"
        placeholder="Ask about leave, holidays, policies or your calendar…"
        [value]="draft()"
        (input)="draft.set(box.value)"
        (keydown.enter)="onEnter($any($event))"
      ></textarea>
      <button type="submit" class="primary" [disabled]="disabled() || !draft().trim()">
        {{ disabled() ? 'Thinking…' : 'Ask' }}
      </button>
    </form>
  `,
  styles: `
    .composer { display: flex; gap: 8px; align-items: flex-end; padding: 12px;
      border: 1px solid var(--border); border-radius: 16px; background: var(--surface); }
    textarea { flex: 1; resize: none; border: 0; outline: none; background: transparent; color: var(--text);
      font: inherit; line-height: 1.5; max-height: 160px; min-height: 24px; padding: 6px 4px; }
    button { flex: none; }
  `,
})
export class Composer {
  readonly disabled = input(false);
  readonly submitted = output<string>();

  protected readonly maxLength = MAX_QUESTION_LENGTH;
  protected readonly draft = signal('');
  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  /** Lets suggestion chips fill and send a question. */
  submit(text: string): void {
    this.draft.set(text);
    this.send();
  }

  protected onEnter(event: KeyboardEvent): void {
    if (event.shiftKey) return;
    event.preventDefault();
    this.send();
  }

  protected send(): void {
    const text = this.draft().trim();
    if (!text || this.disabled()) return;
    this.submitted.emit(text);
    this.draft.set('');
    this.box().nativeElement.focus();
  }
}
