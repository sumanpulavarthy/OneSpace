import { Observable, of, throwError } from 'rxjs';

type Responder = (document: string, variables?: object) => unknown;

/** Stand-in for GraphqlClient: answers by matching the operation name in the document. */
export class FakeGraphqlClient {
  readonly calls: { document: string; variables?: object }[] = [];
  private readonly responders = new Map<string, Responder>();

  on(operationName: string, responder: Responder): this {
    this.responders.set(operationName, responder);
    return this;
  }

  request<T>(document: string, variables?: object): Observable<T> {
    this.calls.push({ document, variables });
    const name = /(?:query|mutation)\s+(\w+)/.exec(document)?.[1] ?? '';
    const responder = this.responders.get(name);
    if (!responder) return throwError(() => new Error(`No fake response for ${name}`));
    try {
      return of(responder(document, variables) as T);
    } catch (error) {
      return throwError(() => error);
    }
  }
}
