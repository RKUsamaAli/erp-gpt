import { Injectable, computed, effect, signal } from '@angular/core';
import { ChatMessage } from '../models/chat.models';
import { Chat, Project, UNTITLED_CHAT, titleFromQuestion } from '../models/workspace.models';
import { ChatStore } from './chat-store';

const STORAGE_KEY = 'erp-gpt.workspace';

interface Persisted {
  projects: Project[];
  chats: Chat[];
}

function uid(): string {
  return crypto.randomUUID();
}

/**
 * localStorage-backed workspace. Deliberately the simplest thing that survives
 * a refresh — the real home for this is the backend (see ChatStore).
 */
@Injectable()
export class LocalChatStore implements ChatStore {
  private readonly state = signal<Persisted>(load());

  readonly projects = computed(() =>
    [...this.state().projects].sort((a, b) => a.createdAt - b.createdAt),
  );
  readonly chats = computed(() =>
    [...this.state().chats].sort((a, b) => b.updatedAt - a.updatedAt),
  );

  constructor() {
    effect(() => localStorage.setItem(STORAGE_KEY, JSON.stringify(this.state())));
  }

  chat(id: string): Chat | undefined {
    return this.state().chats.find((c) => c.id === id);
  }

  chatsIn(projectId: string | null): Chat[] {
    return this.chats().filter((c) => c.projectId === projectId);
  }

  createProject(name = 'New project'): Project {
    const project: Project = { id: uid(), name, createdAt: Date.now() };
    this.state.update((s) => ({ ...s, projects: [...s.projects, project] }));
    return project;
  }

  renameProject(id: string, name: string): void {
    this.updateProject(id, (p) => ({ ...p, name: clean(p.name, name) }));
  }

  createChat(projectId: string | null = null): Chat {
    const chat: Chat = {
      id: uid(),
      title: UNTITLED_CHAT,
      projectId,
      messages: [],
      updatedAt: Date.now(),
    };
    this.state.update((s) => ({ ...s, chats: [...s.chats, chat] }));
    return chat;
  }

  renameChat(id: string, title: string): void {
    this.updateChat(id, (c) => ({ ...c, title: clean(c.title, title) }));
  }

  setMessages(chatId: string, messages: ChatMessage[]): void {
    this.updateChat(chatId, (chat) => ({
      ...chat,
      messages,
      updatedAt: Date.now(),
      // Name the chat from its opening question, but never overwrite a title
      // the user typed themselves.
      title:
        chat.title === UNTITLED_CHAT && messages.length
          ? titleFromQuestion(plainText(messages[0]))
          : chat.title,
    }));
  }

  private updateChat(id: string, change: (chat: Chat) => Chat): void {
    this.state.update((s) => ({
      ...s,
      chats: s.chats.map((c) => (c.id === id ? change(c) : c)),
    }));
  }

  private updateProject(id: string, change: (project: Project) => Project): void {
    this.state.update((s) => ({
      ...s,
      projects: s.projects.map((p) => (p.id === id ? change(p) : p)),
    }));
  }
}

/** Blank or whitespace-only renames keep the previous name. */
function clean(previous: string, next: string): string {
  return next.trim() || previous;
}

function plainText(message: ChatMessage): string {
  const first = message.blocks[0];
  return first?.kind === 'text' ? first.text : UNTITLED_CHAT;
}

function load(): Persisted {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { projects: [], chats: [] };
    const parsed = JSON.parse(raw) as Partial<Persisted>;
    return { projects: parsed.projects ?? [], chats: parsed.chats ?? [] };
  } catch {
    // Corrupt or unreadable storage should not stop the app booting.
    return { projects: [], chats: [] };
  }
}
