import { HttpClient, HttpErrorResponse, HttpHeaders, HttpStatusCode } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, defer, from, map, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { RuntimeConfigService } from '../config/runtime-config.service';

interface GraphQLErrorBody {
  readonly message: string;
  readonly extensions?: { readonly code?: string; readonly traceId?: string };
}

interface GraphQLEnvelope<T> {
  readonly data?: T | null;
  readonly errors?: readonly GraphQLErrorBody[];
}

/** A GraphQL error with the API's stable code (BAD_USER_INPUT, UNAUTHENTICATED, INTERNAL_ERROR, …). */
export class GraphQLRequestError extends Error {
  constructor(
    message: string,
    readonly code: string,
    readonly traceId?: string,
  ) {
    super(message);
  }
}

/** Posts typed operations (generated from specs/graphql) to the API with the employee's bearer token. */
@Injectable({ providedIn: 'root' })
export class GraphqlClient {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly runtime = inject(RuntimeConfigService);
  private readonly auth = inject(AuthService);

  request<TData, TVariables extends object = Record<string, never>>(
    document: string,
    variables?: TVariables,
  ): Observable<TData> {
    // defer: fetch the token per subscription so retries pick up a refreshed token.
    return defer(() => from(this.auth.getAccessToken())).pipe(
      catchError(error => this.endSession(error)),
      switchMap(token => {
        const headers = token ? new HttpHeaders({ Authorization: `Bearer ${token}` }) : new HttpHeaders();
        return this.http.post<GraphQLEnvelope<TData>>(
          `${this.runtime.apiBaseUrl()}/graphql`,
          { query: document, variables: variables ?? {} },
          { headers },
        );
      }),
      catchError(error =>
        error instanceof HttpErrorResponse && error.status === HttpStatusCode.Unauthorized
          ? this.endSession(error)
          : throwError(() => error),
      ),
      map(body => {
        const first = body.errors?.[0];
        if (first) {
          throw new GraphQLRequestError(first.message, first.extensions?.code ?? 'UNKNOWN', first.extensions?.traceId);
        }
        if (!body.data) throw new GraphQLRequestError('Unable to complete the request.', 'NO_DATA');
        return body.data;
      }),
    );
  }

  private endSession(error: unknown): Observable<never> {
    if (this.auth.configured()) void this.router.navigateByUrl('/login');
    return throwError(() => error);
  }
}
