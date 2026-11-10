import { TestBed } from '@angular/core/testing';
import { GraphQLRequestError, GraphqlClient } from '../../../core/graphql/graphql.client';
import type { AskMutation } from '../../../graphql/generated';
import { FakeGraphqlClient } from '../../../testing/fake-graphql-client';
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
  },
});

describe('AssistantStore', () => {
  let graphql: FakeGraphqlClient;
  let store: AssistantStore;

  beforeEach(() => {
    graphql = new FakeGraphqlClient();
    TestBed.configureTestingModule({
      providers: [AssistantStore, { provide: GraphqlClient, useValue: graphql }],
    });
    store = TestBed.inject(AssistantStore);
  });

  it('adds the question and the answer, and reuses the session id', async () => {
    graphql.on('Ask', () => answer('s-1'));

    await store.ask("What's my leave balance?");
    await store.ask('And holidays?');

    expect(store.messages().map(m => m.role)).toEqual(['user', 'assistant', 'user', 'assistant']);
    expect(graphql.calls[0].variables).toEqual({ input: { question: "What's my leave balance?", sessionId: null } });
    expect(graphql.calls[1].variables).toEqual({ input: { question: 'And holidays?', sessionId: 's-1' } });
    expect(store.pending()).toBe(false);
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
