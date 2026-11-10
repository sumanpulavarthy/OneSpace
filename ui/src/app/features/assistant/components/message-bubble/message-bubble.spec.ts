import { TestBed } from '@angular/core/testing';
import type { ChatMessage } from '../../data-access/assistant.store';
import { MessageBubble } from './message-bubble';

function render(message: ChatMessage): HTMLElement {
  const fixture = TestBed.createComponent(MessageBubble);
  fixture.componentRef.setInput('message', message);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('MessageBubble', () => {
  it('shows source attribution for assistant answers', () => {
    const el = render({
      id: 1,
      role: 'assistant',
      answer: {
        __typename: 'AssistantAnswer',
        sessionId: 's',
        answer: 'Two WFH days per week.',
        intent: 'policy_lookup',
        latencyMs: 10,
        sources: [{ __typename: 'SourceAttribution', system: 'COMPLY_KARO', label: 'ComplyKaro policy: Work From Home Policy', reference: null }],
        toolCalls: [],
      },
    });

    expect(el.textContent).toContain('Two WFH days per week.');
    expect(el.querySelector('.source .system')?.textContent).toBe('ComplyKaro');
  });

  it('tells the user when actions were blocked', () => {
    const el = render({
      id: 1,
      role: 'assistant',
      answer: {
        __typename: 'AssistantAnswer',
        sessionId: 's',
        answer: 'I can only read data.',
        intent: 'hr_lookup',
        latencyMs: 10,
        sources: [],
        toolCalls: [{ __typename: 'ToolInvocation', name: 'applyLeave', system: 'ZOHO_PEOPLE', outcome: 'BLOCKED', durationMs: 0 }],
      },
    });

    expect(el.querySelector('.note')?.textContent).toContain('1 action(s) were not performed');
  });

  it('offers to connect Zoho People when the answer needed it', () => {
    const el = render({
      id: 1,
      role: 'assistant',
      answer: {
        __typename: 'AssistantAnswer',
        sessionId: 's',
        answer: 'Connect your Zoho People account so I can look that up.',
        intent: 'leave_lookup',
        latencyMs: 10,
        sources: [],
        toolCalls: [{ __typename: 'ToolInvocation', name: 'zoho_getLeaveBalance', system: 'ZOHO_PEOPLE', outcome: 'CONNECTION_REQUIRED', durationMs: 0 }],
      },
    });

    expect(el.querySelector('.connect button')?.textContent).toContain('Connect Zoho People');
  });

  it('renders errors as alerts', () => {
    const el = render({ id: 1, role: 'error', text: 'Something went wrong.' });
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong.');
  });
});
