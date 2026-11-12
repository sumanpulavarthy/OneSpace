import { TestBed } from '@angular/core/testing';
import type { ChatMessage } from '../../data-access/assistant.store';
import { MessageBubble } from './message-bubble';

const GROUNDED = { __typename: 'GroundingReport' as const, checked: true, grounded: true, unverifiedValues: [] };

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
        blocks: [],
        grounding: GROUNDED,
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
        blocks: [],
        grounding: GROUNDED,
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
        blocks: [],
        grounding: GROUNDED,
      },
    });

    expect(el.querySelector('.connect button')?.textContent).toContain('Connect Zoho People');
  });

  it('renders errors as alerts', () => {
    const el = render({ id: 1, role: 'error', text: 'Something went wrong.' });
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong.');
  });

  it('renders server-built tables with locale formatting and the source', () => {
    const el = render({
      id: 1,
      role: 'assistant',
      answer: {
        sessionId: 's',
        answer: 'You have 4.5 days of casual leave left.',
        intent: 'leave_lookup',
        latencyMs: 10,
        sources: [],
        toolCalls: [],
        grounding: GROUNDED,
        blocks: [
          {
            id: 't1',
            kind: 'TABLE',
            title: 'Leave balance',
            totalRows: 3,
            text: null,
            columns: [
              { key: 'Name', label: 'Leave type', format: 'TEXT' },
              { key: 'available', label: 'Available', format: 'NUMBER' },
            ],
            rows: [['Casual Leave', '4.5']],
            items: [],
            source: { system: 'ZOHO_PEOPLE', label: 'From Zoho People', reference: null },
          },
        ],
      },
    });

    expect(el.querySelector('caption')?.textContent).toContain('Leave balance');
    expect([...el.querySelectorAll('th')].map(th => th.getAttribute('scope'))).toEqual(['col', 'col']);
    expect(el.querySelector('td.num')?.textContent?.trim()).toBe('4.5');
    expect(el.querySelector('figcaption')?.textContent).toContain('Showing 1 of 3');
    expect(el.querySelector('figcaption')?.textContent).toContain('From Zoho People');
  });

  it('warns when figures could not be matched to the source data', () => {
    const el = render({
      id: 1,
      role: 'assistant',
      answer: {
        sessionId: 's',
        answer: 'You have 9 days left.',
        intent: 'leave_lookup',
        latencyMs: 10,
        sources: [],
        toolCalls: [],
        blocks: [],
        grounding: { checked: true, grounded: false, unverifiedValues: ['9'] },
      },
    });

    expect(el.querySelector('.warning')?.textContent).toContain("couldn't be matched");
  });

  it('shows streamed text while the answer is pending', () => {
    const el = render({ id: 1, role: 'pending', draft: 'You have', activity: 'Checking Zoho People…' });
    expect(el.querySelector('.answer')?.textContent).toBe('You have');
    expect(el.querySelector('.activity')?.textContent).toContain('Checking Zoho People');
  });
});
