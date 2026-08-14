import {
  AfterViewChecked,
  Component,
  DestroyRef,
  ElementRef,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { CHAT_SERVICE } from '../core/chat-service';
import { AnswerBlock, AnswerChunk, ChatMessage, blocksToText } from '../models/chat.models';
import { AnswerBlockComponent } from './answer-block/answer-block.component';

/**
 * These must stay in step with MockChatService's matchers — a suggestion that
 * matches nothing returns the fallback, which makes the chips look broken.
 */
const SUGGESTIONS = [
  'Show top 5 customers by revenue this quarter',
  'What were total sales in Canada last month?',
  'List products with low stock in the Bikes category',
];

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [FormsModule, AnswerBlockComponent],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss',
})
export class ChatComponent implements AfterViewChecked {
  @ViewChild('historyEl') private historyEl?: ElementRef<HTMLDivElement>;
  @ViewChild('promptEl') private promptEl?: ElementRef<HTMLTextAreaElement>;

  private readonly chat = inject(CHAT_SERVICE);
  private readonly destroyRef = inject(DestroyRef);

  readonly messages = signal<ChatMessage[]>([]);
  readonly prompt = signal('');
  readonly isLoading = signal(false);
  readonly suggestions = SUGGESTIONS;
  readonly copiedIndex = signal<number | null>(null);

  /**
   * The "thinking" dots belong to the gap before the first chunk lands. Once
   * the answer starts streaming, the bubble itself is the progress indicator —
   * showing both at once reads as two replies in flight.
   */
  readonly showTyping = computed(() => {
    if (!this.isLoading()) return false;
    const msgs = this.messages();
    const last = msgs[msgs.length - 1];
    return !last || last.role === 'user';
  });

  /** Exposed for the template: user bubbles render as plain text. */
  readonly plain = blocksToText;

  private shouldScroll = false;
  /** The in-flight request, so Stop can cancel it. */
  private stream?: Subscription;
  /** Whether the trailing message is an assistant turn still being filled. */
  private assistantOpen = false;

  ngAfterViewChecked(): void {
    if (this.shouldScroll && this.historyEl) {
      this.historyEl.nativeElement.scrollTop = this.historyEl.nativeElement.scrollHeight;
      this.shouldScroll = false;
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  send(text = this.prompt().trim()): void {
    if (!text || this.isLoading()) {
      return;
    }

    this.messages.update((msgs) => [
      ...msgs,
      { role: 'user', blocks: [{ kind: 'text', text }], time: this.now() },
    ]);
    this.prompt.set('');
    this.resizeTextarea();
    this.isLoading.set(true);
    this.assistantOpen = false;
    this.shouldScroll = true;

    this.stream = this.chat
      .ask({ question: text })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        // finalize, not complete: unsubscribe() fires neither complete nor
        // error, so putting this anywhere else leaves Stop with a permanently
        // disabled composer.
        finalize(() => {
          this.isLoading.set(false);
          this.assistantOpen = false;
          this.stream = undefined;
          this.shouldScroll = true;
          this.promptEl?.nativeElement.focus();
        }),
      )
      .subscribe({
        next: (chunk) => this.applyChunk(chunk),
        error: (err: unknown) =>
          this.pushError(err instanceof Error ? err.message : 'Something went wrong.'),
      });
  }

  /** Cancels the in-flight answer, keeping whatever has already streamed. */
  stop(): void {
    this.stream?.unsubscribe();
  }

  useSuggestion(text: string): void {
    this.send(text);
  }

  async copyMessage(blocks: AnswerBlock[], index: number): Promise<void> {
    try {
      await navigator.clipboard.writeText(blocksToText(blocks));
      this.copiedIndex.set(index);
      setTimeout(
        () => this.copiedIndex.update((current) => (current === index ? null : current)),
        1500,
      );
    } catch {
      // Clipboard access denied; silently ignore.
    }
  }

  onInput(value: string): void {
    this.prompt.set(value);
    this.resizeTextarea();
  }

  private applyChunk(chunk: AnswerChunk): void {
    if (chunk.kind === 'error') {
      this.pushError(chunk.message);
      return;
    }

    this.openAssistantMessage();
    this.messages.update((msgs) => {
      const next = [...msgs];
      const last = next[next.length - 1];
      const blocks = [...last.blocks];

      if (chunk.kind === 'text-delta') {
        const tail = blocks[blocks.length - 1];
        // Append to the trailing text block, or start a new one if the
        // previous block was a table or list.
        if (tail?.kind === 'text') {
          blocks[blocks.length - 1] = { kind: 'text', text: tail.text + chunk.text };
        } else {
          blocks.push({ kind: 'text', text: chunk.text });
        }
      } else {
        blocks.push(chunk.block);
      }

      next[next.length - 1] = { ...last, blocks };
      return next;
    });
    this.shouldScroll = true;
  }

  private openAssistantMessage(): void {
    if (this.assistantOpen) {
      return;
    }
    this.messages.update((msgs) => [
      ...msgs,
      { role: 'assistant', blocks: [], time: this.now() },
    ]);
    this.assistantOpen = true;
  }

  private pushError(message: string): void {
    const error: ChatMessage = {
      role: 'assistant',
      blocks: [{ kind: 'text', text: message }],
      isError: true,
      time: this.now(),
    };

    this.messages.update((msgs) => {
      const last = msgs[msgs.length - 1];
      // If the answer failed before producing anything, replace the empty
      // bubble rather than leaving a blank one above the error.
      if (this.assistantOpen && last?.role === 'assistant' && last.blocks.length === 0) {
        return [...msgs.slice(0, -1), error];
      }
      return [...msgs, error];
    });

    this.assistantOpen = false;
    this.shouldScroll = true;
  }

  private now(): string {
    return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  private resizeTextarea(): void {
    const el = this.promptEl?.nativeElement;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 200)}px`;
  }
}
