import { InjectionToken, Signal } from '@angular/core';
import { ChatMessage } from '../models/chat.models';
import { Chat, Project } from '../models/workspace.models';

/**
 * Where projects, chats and their messages live.
 *
 * The second seam in this app, for the same reason as CHAT_SERVICE: the backend
 * already plans ChatThread / ChatTurn entities (agent/README.md, roadmap steps
 * 9-12). When those exist, an ApiChatStore replaces LocalChatStore and nothing
 * above this interface changes.
 */
export interface ChatStore {
  readonly projects: Signal<Project[]>;
  /** Most recently updated first. */
  readonly chats: Signal<Chat[]>;

  chat(id: string): Chat | undefined;
  chatsIn(projectId: string | null): Chat[];

  createProject(name?: string): Project;
  renameProject(id: string, name: string): void;

  createChat(projectId?: string | null): Chat;
  renameChat(id: string, title: string): void;

  /** Replaces a chat's transcript; also retitles it from the first question. */
  setMessages(chatId: string, messages: ChatMessage[]): void;
}

export const CHAT_STORE = new InjectionToken<ChatStore>('CHAT_STORE');
