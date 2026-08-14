import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * ---------------------------------------------------------------------------
 * TODO: replace with real API details
 * ---------------------------------------------------------------------------
 * Everything below is a placeholder. Update these four things to point this
 * app at your real GraphQL backend — nothing else in the app needs to change.
 *
 * 1. GRAPHQL_ENDPOINT — the URL of your GraphQL API.
 * 2. CHAT_QUERY        — the query/mutation string, incl. the variable name(s).
 * 3. buildVariables()  — how the user's prompt maps to the query's variables.
 * 4. extractAnswer()   — how the answer text is read out of the response.
 * ---------------------------------------------------------------------------
 */
const GRAPHQL_ENDPOINT = 'https://example.com/graphql'; // TODO: real endpoint

const CHAT_QUERY = `
  query AskAssistant($input: String!) {
    askAssistant(input: $input) {
      response
    }
  }
`; // TODO: real query/mutation + field names

function buildVariables(prompt: string): Record<string, unknown> {
  return { input: prompt }; // TODO: match the real variable name(s)
}

function extractAnswer(data: any): string {
  return data?.askAssistant?.response ?? ''; // TODO: match the real response shape
}

export interface GraphqlChatResult {
  answer: string;
}

interface GraphqlEnvelope<T> {
  data?: T;
  errors?: Array<{ message: string }>;
}

@Injectable({ providedIn: 'root' })
export class GraphqlService {
  constructor(private readonly http: HttpClient) {}

  /**
   * Sends the user's prompt to the GraphQL API and returns the assistant's answer.
   * Throws an Error with a human-readable message on network or GraphQL errors.
   */
  async ask(prompt: string): Promise<GraphqlChatResult> {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
      // TODO: add auth headers here if the real API requires them, e.g.:
      // Authorization: `Bearer ${environment.apiKey}`,
    });

    const body = {
      query: CHAT_QUERY,
      variables: buildVariables(prompt),
    };

    let envelope: GraphqlEnvelope<any>;
    try {
      envelope = await firstValueFrom(
        this.http.post<GraphqlEnvelope<any>>(GRAPHQL_ENDPOINT, body, { headers })
      );
    } catch (err: any) {
      const message = err?.error?.message ?? err?.message ?? 'Network request failed.';
      throw new Error(`Could not reach the GraphQL API: ${message}`);
    }

    if (envelope.errors?.length) {
      throw new Error(envelope.errors.map((e) => e.message).join('; '));
    }

    return { answer: extractAnswer(envelope.data) };
  }
}
