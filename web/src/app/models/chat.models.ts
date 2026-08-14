/**
 * Chat domain types.
 *
 * Answers are STRUCTURED, never HTML strings. The eventual answers are ERP data
 * — tables of customers and revenue — and the project treats model output as
 * untrusted text until it passes the validation gate (docs/architecture.md).
 * Rendering these through Angular's normal interpolation means no
 * `bypassSecurityTrustHtml` anywhere in this codebase.
 */

/** One renderable piece of an answer. */
export type AnswerBlock =
  | { kind: 'text'; text: string }
  | { kind: 'list'; items: string[] }
  | { kind: 'table'; headers: string[]; rows: string[][] }
  | { kind: 'code'; lang: string; source: string };

/**
 * One emission from a ChatService.
 *
 * The split between `text-delta` and a whole `block` is deliberate: it is what
 * reproduces the two-speed streaming of demo/index.html, where paragraphs type
 * out character by character but tables and lists appear as complete blocks.
 *
 * There is no `error` variant: failures travel on the Observable's error
 * channel, so the mock and the real service fail through the same path and
 * consumers have one place to handle it.
 */
export type AnswerChunk =
  | { kind: 'text-delta'; text: string }
  | { kind: 'block'; block: AnswerBlock };

/**
 * Folds one chunk into an answer's blocks. Pure, so it is testable on its own
 * and every consumer of a ChatService assembles answers identically — this is
 * shared contract knowledge, not component state management.
 */
export function appendChunk(blocks: AnswerBlock[], chunk: AnswerChunk): AnswerBlock[] {
  if (chunk.kind === 'block') {
    return [...blocks, chunk.block];
  }

  const tail = blocks[blocks.length - 1];
  // Extend the trailing text block, or start a new one if the previous block
  // was a table or list.
  return tail?.kind === 'text'
    ? [...blocks.slice(0, -1), { kind: 'text', text: tail.text + chunk.text }]
    : [...blocks, { kind: 'text', text: chunk.text }];
}

export interface ChatMessage {
  role: 'user' | 'assistant';
  blocks: AnswerBlock[];
  isError?: boolean;
  time: string;
}

/**
 * Object parameter, not positional args: the real backend will need
 * conversation history, so this grows without breaking every call site.
 */
export interface AskRequest {
  question: string;
  threadId?: string;
}

/** Flattens blocks to plain text — used for copy-to-clipboard. */
export function blocksToText(blocks: AnswerBlock[]): string {
  return blocks
    .map((block) => {
      switch (block.kind) {
        case 'text':
          return block.text;
        case 'list':
          return block.items.map((item) => `- ${item}`).join('\n');
        case 'table':
          return [block.headers, ...block.rows].map((row) => row.join('\t')).join('\n');
        case 'code':
          return block.source;
      }
    })
    .join('\n\n');
}
