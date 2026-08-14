import { fakeAsync, tick } from '@angular/core/testing';
import { AnswerBlock, AnswerChunk } from '../models/chat.models';
import { MockChatService } from './mock-chat-service';

describe('MockChatService', () => {
  let service: MockChatService;

  beforeEach(() => {
    service = new MockChatService();
  });

  /** Drains a full answer synchronously. */
  function collect(question: string): { chunks: AnswerChunk[]; done: boolean } {
    const chunks: AnswerChunk[] = [];
    let done = false;
    service.ask({ question }).subscribe({ next: (c) => chunks.push(c), complete: () => (done = true) });
    tick(60_000);
    return { chunks, done };
  }

  it('emits chunks and then completes', fakeAsync(() => {
    const { chunks, done } = collect('top customers');

    expect(chunks.length).toBeGreaterThan(0);
    expect(done).toBeTrue();
  }));

  it('answers the top-customers question with the 3-header, 5-row table', fakeAsync(() => {
    const { chunks } = collect('Show top 5 customers by revenue this quarter');

    const tables = chunks
      .filter((c): c is Extract<AnswerChunk, { kind: 'block' }> => c.kind === 'block')
      .map((c) => c.block)
      .filter((b): b is Extract<AnswerBlock, { kind: 'table' }> => b.kind === 'table');

    expect(tables.length).toBe(1);
    expect(tables[0].headers).toEqual(['Customer', 'Territory', 'Revenue']);
    expect(tables[0].rows.length).toBe(5);
  }));

  it('emits a table as ONE block, never typed out character by character', fakeAsync(() => {
    const { chunks } = collect('top customers');

    // Every delta must belong to a text block; a table arriving as deltas would
    // mean the two-speed streaming from demo/index.html was lost.
    const deltas = chunks.filter((c) => c.kind === 'text-delta');
    expect(deltas.every((c) => !c.text.includes('Adventure Works Cycle'))).toBeTrue();
  }));

  it('each suggestion chip gets its own answer, never the fallback', fakeAsync(() => {
    const questions = [
      'Show top 5 customers by revenue this quarter',
      'What were total sales in Canada last month?',
      'List products with low stock in the Bikes category',
    ];

    const firstLines = questions.map(
      (q) =>
        collect(q)
          .chunks.filter((c): c is Extract<AnswerChunk, { kind: 'text-delta' }> => c.kind === 'text-delta')
          .map((c) => c.text)
          .join('')
          .split('\n')[0],
    );

    expect(new Set(firstLines).size).toBe(3);
    expect(firstLines.some((line) => line.includes('simulated'))).toBeFalse();
  }));

  it('emits an error chunk for the "fail" trigger', fakeAsync(() => {
    const { chunks, done } = collect('fail');

    expect(chunks.length).toBe(1);
    expect(chunks[0].kind).toBe('error');
    expect(done).toBeTrue();
  }));

  // The one that matters: without it, the Stop button is aspirational.
  it('stops emitting after unsubscribe', fakeAsync(() => {
    const chunks: AnswerChunk[] = [];
    const sub = service.ask({ question: 'top customers' }).subscribe((c) => chunks.push(c));

    tick(1000);
    const countAtStop = chunks.length;
    expect(countAtStop).toBeGreaterThan(0); // proves it had actually started

    sub.unsubscribe();
    tick(60_000);

    expect(chunks.length).toBe(countAtStop);
  }));
});
