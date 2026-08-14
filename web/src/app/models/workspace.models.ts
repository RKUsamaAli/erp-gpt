import { ChatMessage } from './chat.models';

/**
 * A named folder of chats, like ChatGPT's projects. A chat may sit outside any
 * project (`projectId: null`), which is where "New chat" puts it.
 *
 * NOTE: agent/README.md plans ChatThread / ChatTurn entities server-side
 * (roadmap steps 9-12). These shapes are the client's view of the same idea and
 * will need reconciling when that lands, which is exactly why they are reached
 * only through CHAT_STORE.
 */
export interface Project {
  id: string;
  name: string;
  createdAt: number;
}

export interface Chat {
  id: string;
  title: string;
  projectId: string | null;
  messages: ChatMessage[];
  updatedAt: number;
  archivedAt?: number;
}

/** Untitled chats show this until their first question names them. */
export const UNTITLED_CHAT = 'New chat';

/** Chat titles are derived from the first question, as in demo/index.html. */
export function titleFromQuestion(question: string): string {
  const cleaned = question.trim().replace(/\s+/g, ' ');
  return cleaned.length > 42 ? `${cleaned.slice(0, 42)}...` : cleaned;
}
