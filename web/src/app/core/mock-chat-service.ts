import { Injectable } from '@angular/core';
import {
  EMPTY,
  Observable,
  concat,
  concatMap,
  from,
  ignoreElements,
  map,
  throwError,
  timer,
} from 'rxjs';
import { AnswerBlock, AnswerChunk, AskRequest } from '../models/chat.models';
import { ChatService } from './chat-service';

/**
 * Canned answers, ported from demo/index.html — the agreed spec for how this
 * should feel. No network, no LLM, no query construction.
 *
 * Everything is built from RxJS timers rather than setTimeout, so unsubscribing
 * tears the whole chain down. A hand-rolled `new Observable(...)` without a
 * teardown is the classic way a mock keeps "typing" after Stop is pressed.
 */

/** Timings lifted from demo/index.html so the feel matches. */
const THINKING_MIN_MS = 450;
const THINKING_JITTER_MS = 350;
const CHAR_MS = 18;
const MAX_DELTA_MS = 120;
/** Tables and lists land whole, after a beat — they are never typed out. */
const WHOLE_BLOCK_MS = 180;
const BETWEEN_BLOCKS_MS = 90;

/** Types `fail` to exercise the error path, which nothing else can reach now. */
const FAIL_PATTERN = /^fail$/i;

interface CannedReply {
  /** A suggestion chip is only offered if this matcher recognises it. */
  match: RegExp;
  suggestion: string;
  blocks: AnswerBlock[];
}

const REPLIES: CannedReply[] = [
  {
    match: /top|customer|revenue/i,
    suggestion: 'Show top 5 customers by revenue this quarter',
    blocks: [
      { kind: 'text', text: 'Here are the top 5 customers by revenue for the current quarter:' },
      {
        kind: 'table',
        headers: ['Customer', 'Territory', 'Revenue'],
        rows: [
          ['Adventure Works Cycle', 'Southwest', '$248,420'],
          ['Metropolitan Bike Supply', 'Canada', '$191,055'],
          ['Northwest Traders', 'Northwest', '$167,880'],
          ['Contoso Retail', 'Central', '$142,310'],
          ['Fabrikam Bikes', 'Southeast', '$128,640'],
        ],
      },
      {
        kind: 'text',
        text: 'Southwest accounts for the largest share (~28%). Want a breakdown by product category?',
      },
    ],
  },
  {
    match: /canada|territory|sales/i,
    suggestion: 'What were total sales in Canada last month?',
    blocks: [
      { kind: 'text', text: 'Total sales in Canada for last month came to $412,780.' },
      {
        kind: 'list',
        items: ['Orders: 186', 'Average order value: $2,219', 'Top category: Mountain Bikes (34%)'],
      },
      { kind: 'text', text: 'Revenue was up 6.2% versus the prior month.' },
    ],
  },
  {
    match: /stock|product|bike|inventory/i,
    suggestion: 'List products with low stock in the Bikes category',
    blocks: [
      { kind: 'text', text: 'Products in Bikes with low stock (25 units or fewer):' },
      {
        kind: 'table',
        headers: ['SKU', 'Product', 'On hand'],
        rows: [
          ['BK-M82B-42', 'Mountain-200 Black, 42', '8'],
          ['BK-R93R-44', 'Road-250 Red, 44', '14'],
          ['BK-T79Y-46', 'Touring-1000 Yellow, 46', '21'],
        ],
      },
      { kind: 'text', text: 'I can draft a reorder suggestion if you want.' },
    ],
  },
];

const FALLBACK: AnswerBlock[] = [
  { kind: 'text', text: 'I looked across sales, customers, and inventory for that question.' },
  {
    kind: 'text',
    text: 'Answers are simulated for now. Once the agent is wired up, this pane will stream live ERP data the same way.',
  },
  { kind: 'text', text: 'Try “top customers”, “Canada sales”, or “low stock bikes”.' },
];

/** Splits on word boundaries so deltas never land mid-word. */
function chunkText(text: string): string[] {
  return text.match(/\S+\s*/g) ?? [];
}

/** Waits, emits nothing. */
function pause(ms: number): Observable<never> {
  return timer(ms).pipe(ignoreElements());
}

function typeOut(text: string): Observable<AnswerChunk> {
  return from(chunkText(text)).pipe(
    concatMap((piece) =>
      timer(Math.min(piece.length * CHAR_MS, MAX_DELTA_MS)).pipe(
        map((): AnswerChunk => ({ kind: 'text-delta', text: piece })),
      ),
    ),
  );
}

function pickReply(question: string): AnswerBlock[] {
  return REPLIES.find((reply) => reply.match.test(question))?.blocks ?? FALLBACK;
}

@Injectable()
export class MockChatService implements ChatService {
  /** Derived from REPLIES, so a chip can never drift out of sync with a matcher. */
  readonly suggestions: readonly string[] = REPLIES.map((reply) => reply.suggestion);

  ask({ question }: AskRequest): Observable<AnswerChunk> {
    const body = FAIL_PATTERN.test(question.trim())
      ? throwError(() => new Error('Simulated backend failure.'))
      : from(pickReply(question)).pipe(
          concatMap((block, index) =>
            concat(
              index > 0 ? pause(BETWEEN_BLOCKS_MS) : EMPTY,
              block.kind === 'text'
                ? typeOut(block.text)
                : timer(WHOLE_BLOCK_MS).pipe(map((): AnswerChunk => ({ kind: 'block', block }))),
            ),
          ),
        );

    return concat(pause(THINKING_MIN_MS + Math.random() * THINKING_JITTER_MS), body);
  }
}
