import {
  AfterViewChecked,
  Component,
  ElementRef,
  ViewChild,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GraphqlService } from '../services/graphql.service';

export interface ChatMessage {
  role: 'user' | 'assistant';
  text: string;
  isError?: boolean;
  time: string;
}

const SUGGESTIONS = [
  'Summarize this article in 3 bullet points',
  'Write a GraphQL query to fetch user orders',
  'Explain the difference between a query and a mutation',
  'Draft a polite follow-up email to a client',
];

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss',
})
export class ChatComponent implements AfterViewChecked {
  @ViewChild('historyEl') private historyEl?: ElementRef<HTMLDivElement>;
  @ViewChild('promptEl') private promptEl?: ElementRef<HTMLTextAreaElement>;

  readonly messages = signal<ChatMessage[]>([]);
  readonly prompt = signal('');
  readonly isLoading = signal(false);
  readonly suggestions = SUGGESTIONS;
  readonly copiedIndex = signal<number | null>(null);

  private shouldScroll = false;

  constructor(private readonly graphql: GraphqlService) {}

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

  async send(text = this.prompt().trim()): Promise<void> {
    if (!text || this.isLoading()) {
      return;
    }

    this.messages.update((msgs) => [...msgs, { role: 'user', text, time: this.now() }]);
    this.prompt.set('');
    this.resizeTextarea();
    this.isLoading.set(true);
    this.shouldScroll = true;

    try {
      const result = await this.graphql.ask(text);
      this.messages.update((msgs) => [
        ...msgs,
        { role: 'assistant', text: result.answer || '(empty response)', time: this.now() },
      ]);
    } catch (err: any) {
      this.messages.update((msgs) => [
        ...msgs,
        {
          role: 'assistant',
          text: err?.message ?? 'Something went wrong.',
          isError: true,
          time: this.now(),
        },
      ]);
    } finally {
      this.isLoading.set(false);
      this.shouldScroll = true;
      this.promptEl?.nativeElement.focus();
    }
  }

  useSuggestion(text: string): void {
    this.send(text);
  }

  async copyMessage(text: string, index: number): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copiedIndex.set(index);
      setTimeout(() => this.copiedIndex.update((current) => (current === index ? null : current)), 1500);
    } catch {
      // Clipboard access denied; silently ignore.
    }
  }

  onInput(value: string): void {
    this.prompt.set(value);
    this.resizeTextarea();
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
