/**
 * Operations for the assistant feature. Types are generated into app/graphql/generated.ts by
 * `npm run codegen`, validated against specs/graphql/schema.graphql.
 * The streaming endpoint (/assistant/stream) returns the same AssistantAnswer shape in its `completed` event.
 */
export const AskDocument = /* GraphQL */ `
  mutation Ask($input: AskInput!) {
    ask(input: $input) {
      sessionId
      answer
      intent
      latencyMs
      sources {
        system
        label
        reference
      }
      toolCalls {
        name
        system
        outcome
        durationMs
      }
      blocks {
        id
        kind
        title
        totalRows
        text
        columns {
          key
          label
          format
        }
        rows
        items {
          label
          value
          format
        }
        source {
          system
          label
          reference
        }
      }
      grounding {
        checked
        grounded
        unverifiedValues
      }
    }
  }
`;

export const MeDocument = /* GraphQL */ `
  query Me {
    me {
      id
      displayName
      email
    }
  }
`;
