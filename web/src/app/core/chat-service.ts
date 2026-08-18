import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { AnswerChunk, AskRequest } from '../models/chat.models';

/**
 * The seam between the UI and whatever answers questions.
 *
 * Components hand questions to this interface and render structured answer
 * blocks. Transport details, GraphQL documents, variables, and response
 * formatting stay behind the implementation.
 *
 * Returns an Observable rather than a Promise so that unsubscribing cancels
 * the work — that is what makes the Stop button possible, and HttpClient
 * aborts the underlying request on unsubscribe, so cancellation behaves the
 * same for the mock and the real thing.
 */
export interface ChatService {
  ask(request: AskRequest): Observable<AnswerChunk>;

  /**
   * Starter prompts for the empty state. They live here, not in the component,
   * because only the implementation knows what it can actually answer — a
   * suggestion the backend does not recognise falls through to a generic reply
   * and makes the chips look broken.
   */
  readonly suggestions: readonly string[];
}

/**
 * Deliberately a token rather than an abstract class: it makes every consumer
 * and the single provider greppable (`CHAT_SERVICE`), and nothing can quietly
 * bypass the seam with `providedIn: 'root'`.
 */
export const CHAT_SERVICE = new InjectionToken<ChatService>('CHAT_SERVICE');
