import { fakeAsync, tick } from '@angular/core/testing';
import { AnswerBlock, AnswerChunk, appendChunk } from '../models/chat.models';
import { MockChatService } from './mock-chat-service';

/** Whole blocks (tables, lists) emitted during the answer. */
function blocksOf(chunks: AnswerChunk[]): AnswerBlock[] {
  return chunks.filter((c) => c.kind === 'block').map((c) => c.block);
}

/** Everything that was typed out, concatenated. */
function deltaText(chunks: AnswerChunk[]): string {
  return chunks
    .filter((c) => c.kind === 'text-delta')
    .map((c) => c.text)
    .join('');
}

describe('MockChatService', () => {
  let service: MockChatService;

  beforeEach(() => {
    service = new MockChatService();
  });

  /** Drains a full answer synchronously. */
  function collect(question: string): { chunks: AnswerChunk[]; done: boolean; error?: Error } {
    const chunks: AnswerChunk[] = [];
    let done = false;
    let error: Error | undefined;
    service.ask({ question }).subscribe({
      next: (c) => chunks.push(c),
      complete: () => (done = true),
      error: (e: Error) => (error = e),
    });
    tick(60_000);
    return { chunks, done, error };
  }

  it('emits chunks and then completes', fakeAsync(() => {
    const { chunks, done } = collect('top customers');

    expect(chunks.length).toBeGreaterThan(0);
    expect(done).toBeTrue();
  }));

  it('answers the top-customers question with the 3-header, 5-row table', fakeAsync(() => {
    const tables = blocksOf(collect('Show top 5 customers by revenue this quarter').chunks).filter(
      (b) => b.kind === 'table',
    );

    expect(tables.length).toBe(1);
    expect(tables[0].headers).toEqual(['Customer', 'Territory', 'Revenue']);
    expect(tables[0].rows.length).toBe(5);
  }));

  it('emits a table as ONE block, never typed out character by character', fakeAsync(() => {
    // A table arriving as deltas would mean the two-speed streaming from
    // demo/index.html was lost.
    expect(deltaText(collect('top customers').chunks)).not.toContain('Adventure Works Cycle');
  }));

  it('offers only suggestions it can actually answer', fakeAsync(() => {
    const openings = service.suggestions.map((q) => deltaText(collect(q).chunks).split('\n')[0]);

    expect(service.suggestions.length).toBe(3);
    expect(new Set(openings).size).toBe(3);
    expect(openings.some((line) => line.includes('simulated'))).toBeFalse();
  }));

  it('fails on the error channel for the "fail" trigger', fakeAsync(() => {
    const { chunks, error, done } = collect('fail');

    expect(chunks.length).toBe(0);
    expect(error?.message).toBe('Simulated backend failure.');
    expect(done).toBeFalse();
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

describe('appendChunk', () => {
  const text = (t: string): AnswerChunk => ({ kind: 'text-delta', text: t });
  const table: AnswerBlock = { kind: 'table', headers: ['a'], rows: [['1']] };

  it('starts a text block when there is nothing to extend', () => {
    expect(appendChunk([], text('Hi'))).toEqual([{ kind: 'text', text: 'Hi' }]);
  });

  it('extends the trailing text block rather than adding another', () => {
    const blocks = appendChunk(appendChunk([], text('Hi ')), text('there'));

    expect(blocks).toEqual([{ kind: 'text', text: 'Hi there' }]);
  });

  it('starts a new text block after a table', () => {
    const blocks = appendChunk(appendChunk([], { kind: 'block', block: table }), text('After'));

    expect(blocks.length).toBe(2);
    expect(blocks[1]).toEqual({ kind: 'text', text: 'After' });
  });

  it('leaves earlier blocks referentially stable, so OnPush can skip them', () => {
    const first = appendChunk([], { kind: 'block', block: table });
    const second = appendChunk(first, text('After'));

    expect(second[0]).toBe(first[0]);
  });

  it('does not mutate the input', () => {
    const before: AnswerBlock[] = [{ kind: 'text', text: 'a' }];
    appendChunk(before, text('b'));

    expect(before).toEqual([{ kind: 'text', text: 'a' }]);
  });
});
