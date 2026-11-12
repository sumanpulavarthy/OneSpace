import { TestBed } from '@angular/core/testing';
import { GraphQLRequestError, GraphqlClient } from '../../../core/graphql/graphql.client';
import type { AskMutation } from '../../../graphql/generated';
import { FakeGraphqlClient } from '../../../testing/fake-graphql-client';
import { AssistantStreamClient, type AssistantStreamEvent, StreamUnavailableError } from './assistant-stream.client';
import { AssistantStore, MAX_QUESTION_LENGTH } from './assistant.store';

const answer = (sessionId: string): AskMutation => ({
  ask: {
    __typename: 'AssistantAnswer',
    sessionId,
    answer: 'You have 12 days of earned leave.',
    intent: 'leave_lookup',
    latencyMs: 120,
    sources: [{ __typename: 'SourceAttribution', system: 'ZOHO_PEOPLE', label: 'From Zoho People', reference: 'getLeaveBalance' }],
    toolCalls: [],
    blocks: [],
    grounding: { __typename: 'GroundingReport', checked: true, grounded: true, unverifiedValues: [] },
  },
});

/** Scripted stream: either unavailable (fall back to GraphQL) or a list of events per call. */
class FakeStreamClient {
  readonly inputs: unknown[] = [];
  script: ((emit: (e: AssistantStreamEvent) => void) => Promise<void>) | null = null;

  stream(input: unknown, onEvent: (e: AssistantStreamEvent) => void): Promise<void> {
    this.inputs.push(input);
    return this.script ? this.script(onEvent) : Promise.reject(new StreamUnavailableError(404));
  }
}

describe('AssistantStore', () => {
  let graphql: FakeGraphqlClient;
  let stream: FakeStreamClient;
  let store: AssistantStore;

  beforeEach(() => {
    graphql = new FakeGraphqlClient();
    stream = new FakeStreamClient();
    TestBed.configureTestingModule({
      providers: [AssistantStore, { provide: GraphqlClient, useValue: graphql }, { provide: AssistantStreamClient, useValue: stream }],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('falls back to GraphQL when streaming is unavailable, and reuses the session id', async () => {
    graphql.on('Ask', () => answer('s-1'));

    await store.ask("What's my leave balance?");
    await store.ask('And holidays?');

    expect(store.messages().map(m => m.role)).toEqual(['user', 'assistant', 'user', 'assistant']);
    expect(graphql.calls[0].variables).toEqual({ input: { question: "What's my leave balance?", sessionId: null } });
    expect(graphql.calls[1].variables).toEqual({ input: { question: 'And holidays?', sessionId: 's-1' } });
    expect(store.pending()).toBe(false);
  });

  it('streams text into a pending message, then replaces it with the final answer', async () => {
    const drafts: string[] = [];
    stream.script = async emit => {
      emit({ type: 'tool', tool: 'zoho_getLeaveBalance', system: 'ZOHO_PEOPLE', status: 'started' });
      emit({ type: 'delta', text: 'You have ' });
      emit({ type: 'delta', text: '12 days' });
      const pending = store.messages().at(-1);
      if (pending?.role === 'pending') drafts.push(pending.draft, pending.activity ?? '');
      emit({ type: 'completed', answer: answer('s-9').ask });
    };

    await store.ask('leave?');

    expect(drafts).toEqual(['You have 12 days', 'Checking Zoho People…']);
    expect(store.messages().map(m => m.role)).toEqual(['user', 'assistant']);
    expect(graphql.calls).toHaveLength(0);

    stream.script = async emit => emit({ type: 'completed', answer: answer('s-9').ask });
    await store.ask('again');
    expect(stream.inputs[1]).toEqual({ question: 'again', sessionId: 's-9' });
  });

  it('a restart clears the streamed draft', async () => {
    let draftAfterRestart: string | null = null;
    stream.script = async emit => {
      emit({ type: 'delta', text: 'You have 9 days' });
      emit({ type: 'restart', reason: 'Double-checking that answer.' });
      const pending = store.messages().at(-1);
      draftAfterRestart = pending?.role === 'pending' ? pending.draft : null;
      emit({ type: 'completed', answer: answer('s-1').ask });
    };

    await store.ask('leave?');

    expect(draftAfterRestart).toBe('');
  });

  it('shows a streamed error without retrying over GraphQL', async () => {
    stream.script = async emit => emit({ type: 'error', message: 'Ask a question.', code: 'BAD_USER_INPUT', traceId: null });

    await store.ask('hello');

    expect(store.messages().at(-1)).toEqual(expect.objectContaining({ role: 'error', text: 'Ask a question.' }));
    expect(graphql.calls).toHaveLength(0);
  });

  it('shows API validation messages as they are', async () => {
    graphql.on('Ask', () => {
      throw new GraphQLRequestError('Ask a question.', 'BAD_USER_INPUT');
    });

    await store.ask('hello');

    const last = store.messages().at(-1);
    expect(last).toEqual(expect.objectContaining({ role: 'error', text: 'Ask a question.' }));
  });

  it('ignores blank input and rejects over-long questions without calling the API', async () => {
    await store.ask('   ');
    await store.ask('x'.repeat(MAX_QUESTION_LENGTH + 1));

    expect(graphql.calls).toHaveLength(0);
    expect(stream.inputs).toHaveLength(0);
    expect(store.messages()).toHaveLength(1);
    expect(store.messages()[0].role).toBe('error');
  });

  it('reset starts a new session', async () => {
    graphql.on('Ask', () => answer('s-1'));
    await store.ask('first');

    store.reset();
    await store.ask('second');

    expect(store.messages()).toHaveLength(2);
    expect(graphql.calls[1].variables).toEqual({ input: { question: 'second', sessionId: null } });
  });
});
