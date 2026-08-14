import { TestBed } from '@angular/core/testing';
import { ChatMessage } from '../models/chat.models';
import { UNTITLED_CHAT } from '../models/workspace.models';
import { LocalChatStore } from './local-chat-store';

function question(text: string): ChatMessage {
  return { role: 'user', blocks: [{ kind: 'text', text }], time: '10:00' };
}

describe('LocalChatStore', () => {
  let store: LocalChatStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [LocalChatStore] });
    store = TestBed.inject(LocalChatStore);
  });

  it('creates chats outside any project by default', () => {
    const chat = store.createChat();

    expect(chat.projectId).toBeNull();
    expect(store.chatsIn(null).map((c) => c.id)).toContain(chat.id);
  });

  it('nests chats under a project', () => {
    const project = store.createProject('Q3 review');
    const chat = store.createChat(project.id);

    expect(store.chatsIn(project.id)).toEqual([chat]);
    expect(store.chatsIn(null)).toEqual([]);
  });

  it('titles a chat from its first question', () => {
    const chat = store.createChat();
    store.setMessages(chat.id, [question('who are our biggest customers')]);

    expect(store.chat(chat.id)?.title).toBe('who are our biggest customers');
  });

  it('never overwrites a title the user typed', () => {
    const chat = store.createChat();
    store.renameChat(chat.id, 'Customer review');
    store.setMessages(chat.id, [question('who are our biggest customers')]);

    expect(store.chat(chat.id)?.title).toBe('Customer review');
  });

  it('keeps the previous name when a rename is blank', () => {
    const project = store.createProject('Finance');
    store.renameProject(project.id, '   ');

    expect(store.projects()[0].name).toBe('Finance');
  });

  it('lists chats most recently updated first', () => {
    const first = store.createChat();
    const second = store.createChat();
    store.setMessages(first.id, [question('later')]);

    expect(store.chats()[0].id).toBe(first.id);
    expect(store.chats()[1].id).toBe(second.id);
  });

  it('survives a reload', () => {
    const project = store.createProject('Ops');
    store.createChat(project.id);
    TestBed.tick(); // flush the persistence effect

    // A fresh instance reads back what the first one wrote.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [LocalChatStore] });
    const reloaded = TestBed.inject(LocalChatStore);

    expect(reloaded.projects().map((p) => p.name)).toEqual(['Ops']);
    expect(reloaded.chats().length).toBe(1);
    expect(reloaded.chats()[0].title).toBe(UNTITLED_CHAT);
    expect(reloaded.chatsIn(project.id).length).toBe(1);
  });
});
