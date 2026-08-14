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
import { AnswerBlock, AnswerChunk, ChatMessage, appendChunk, blocksToText } from '../models/chat.models';
import { AnswerBlockComponent } from './answer-block/answer-block.component';

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
  readonly suggestions = this.chat.suggestions;
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

  private shouldScroll = false;
  /** The in-flight request, so Stop can cancel it. */
  private stream?: Subscription;

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

  /**
   * Folds a chunk into the answer being streamed, opening the assistant turn on
   * the first one. Single update, so "which message" and "which block" are
   * decided in one place from the array that is already in hand.
   */
  private applyChunk(chunk: AnswerChunk): void {
    this.messages.update((msgs) => {
      const open = this.openAssistantTurn(msgs);
      const rest = open ? msgs.slice(0, -1) : msgs;
      const target: ChatMessage = open ?? { role: 'assistant', blocks: [], time: this.now() };
      return [...rest, { ...target, blocks: appendChunk(target.blocks, chunk) }];
    });
    this.shouldScroll = true;
  }

  private pushError(message: string): void {
    this.messages.update((msgs) => {
      const open = this.openAssistantTurn(msgs);
      // If the answer failed before producing anything, replace the empty
      // bubble rather than leaving a blank one above the error.
      const rest = open && open.blocks.length === 0 ? msgs.slice(0, -1) : msgs;
      return [
        ...rest,
        { role: 'assistant', blocks: [{ kind: 'text', text: message }], isError: true, time: this.now() },
      ];
    });
    this.shouldScroll = true;
  }

  /**
   * The assistant turn currently being filled, if there is one — derived from
   * the messages themselves rather than tracked in a parallel flag that has to
   * be kept in lockstep with them.
   */
  private openAssistantTurn(msgs: ChatMessage[]): ChatMessage | undefined {
    // send() always appends the user's message first, so a trailing assistant
    // turn can only belong to the answer in flight.
    const last = msgs[msgs.length - 1];
    return last?.role === 'assistant' && !last.isError ? last : undefined;
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
