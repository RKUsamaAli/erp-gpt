import { Injectable } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AnswerChunk, AskRequest } from '../models/chat.models';
import { ChatService } from './chat-service';

/**
 * The real implementation, deliberately not implemented yet.
 *
 * There is no chat endpoint to call. Turning a question into a query is the
 * job of the Phase 4 agent (agent/README.md), which does not exist yet, and the
 * UI must never do it itself (docs/architecture.md). This class exists so the
 * shape is settled and swapping it in is one line in app.config.ts.
 *
 * No `providedIn` on purpose: it stays tree-shaken until someone deliberately
 * provides it.
 */
@Injectable()
export class HttpChatService implements ChatService {
  /** The real backend will serve these; nothing is offered until it does. */
  readonly suggestions: readonly string[] = [];

  private readonly chatUrl = `${environment.apiUrl}/api/chat`;

  ask(_request: AskRequest): Observable<AnswerChunk> {
    // Once POST /api/chat exists, this becomes roughly:
    //
    //   private readonly http = inject(HttpClient);
    //   return this.http
    //     .post<AnswerChunk[]>(this.chatUrl, _request)
    //     .pipe(mergeMap((chunks) => from(chunks)));
    //
    // Streaming will likely want SSE rather than a JSON array, but the
    // Observable<AnswerChunk> contract is the same either way.

    // throwError, NOT `throw`. A synchronous throw never reaches subscribe(),
    // so the component's error handler and finalize() are both bypassed. You
    // get an unhandled exception and a permanently stuck spinner instead of a
    // styled error bubble.
    return throwError(
      () =>
        new Error(
          `HttpChatService is not implemented yet. Configured chat endpoint: ${this.chatUrl}`,
        ),
    );
  }
}
