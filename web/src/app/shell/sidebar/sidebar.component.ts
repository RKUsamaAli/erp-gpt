import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { CHAT_STORE } from '../../core/chat-store';
import { LayoutService } from '../../core/layout.service';
import { Chat, Project } from '../../models/workspace.models';
import { RenameFieldComponent } from '../rename-field/rename-field.component';

/** Which item, if any, is currently being renamed. */
type Editing = { kind: 'project' | 'chat'; id: string } | null;
type OpenMenu = { kind: 'project' | 'chat'; id: string } | null;

@Component({
  selector: 'app-sidebar',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, RenameFieldComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SidebarComponent {
  private readonly store = inject(CHAT_STORE);
  private readonly router = inject(Router);
  private readonly layout = inject(LayoutService);

  readonly projects = this.store.projects;
  /** Chats that belong to no project, shown under "Recent". */
  readonly looseChats = computed(() => this.store.chats().filter((c) => c.projectId === null));
  readonly editing = signal<Editing>(null);
  readonly openMenu = signal<OpenMenu>(null);
  /** Projects collapse so a long list stays navigable. */
  private readonly collapsed = signal<ReadonlySet<string>>(new Set());

  chatsIn(project: Project): Chat[] {
    return this.store.chats().filter((c) => c.projectId === project.id);
  }

  isCollapsed(project: Project): boolean {
    return this.collapsed().has(project.id);
  }

  toggleCollapsed(project: Project): void {
    this.openMenu.set(null);
    this.collapsed.update((open) => {
      const next = new Set(open);
      next.has(project.id) ? next.delete(project.id) : next.add(project.id);
      return next;
    });
  }

  newChat(projectId: string | null = null): void {
    this.openMenu.set(null);
    const chat = this.store.createChat(projectId);
    this.layout.closeSidebar();
    this.router.navigate(['/chat', chat.id]);
  }

  newProject(): void {
    this.openMenu.set(null);
    const project = this.store.createProject();
    // Open straight into rename: a project called "New project" is never what
    // anyone wants, and naming it is the next thing they would do anyway.
    this.startEditing('project', project.id);
  }

  startEditing(kind: 'project' | 'chat', id: string): void {
    this.openMenu.set(null);
    this.editing.set({ kind, id });
  }

  isEditing(kind: 'project' | 'chat', id: string): boolean {
    const editing = this.editing();
    return editing?.kind === kind && editing.id === id;
  }

  commitRename(kind: 'project' | 'chat', id: string, name: string): void {
    kind === 'project' ? this.store.renameProject(id, name) : this.store.renameChat(id, name);
    this.editing.set(null);
  }

  toggleMenu(kind: 'project' | 'chat', id: string): void {
    const open = this.openMenu();
    this.openMenu.set(open?.kind === kind && open.id === id ? null : { kind, id });
  }

  isMenuOpen(kind: 'project' | 'chat', id: string): boolean {
    const open = this.openMenu();
    return open?.kind === kind && open.id === id;
  }

  archiveChat(chat: Chat): void {
    this.openMenu.set(null);
    this.store.archiveChat(chat.id);
    this.openFallbackIfCurrent(chat.id);
  }

  removeChat(chat: Chat): void {
    this.openMenu.set(null);
    if (!confirm(`Remove "${chat.title}"?`)) return;
    this.store.removeChat(chat.id);
    this.openFallbackIfCurrent(chat.id);
  }

  archiveProject(project: Project): void {
    this.openMenu.set(null);
    const currentId = this.currentChatId();
    const containsCurrent = currentId ? this.chatsIn(project).some((chat) => chat.id === currentId) : false;
    this.store.archiveProject(project.id);
    if (containsCurrent && currentId) this.openFallbackIfCurrent(currentId);
  }

  removeProject(project: Project): void {
    this.openMenu.set(null);
    if (!confirm(`Remove "${project.name}" and its chats?`)) return;
    const currentId = this.currentChatId();
    const containsCurrent = currentId ? this.chatsIn(project).some((chat) => chat.id === currentId) : false;
    this.store.removeProject(project.id);
    if (containsCurrent && currentId) this.openFallbackIfCurrent(currentId);
  }

  closeSidebar(): void {
    this.openMenu.set(null);
    this.layout.closeSidebar();
  }

  collapseSidebar(): void {
    this.openMenu.set(null);
    this.layout.collapseSidebar();
  }

  expandSidebar(): void {
    this.layout.expandSidebar();
  }

  private currentChatId(): string | null {
    return this.router.url.match(/^\/chat\/([^/?#]+)/)?.[1] ?? null;
  }

  private openFallbackIfCurrent(removedId: string): void {
    if (this.currentChatId() !== removedId) return;
    const fallback = this.store.chats()[0] ?? this.store.createChat();
    this.router.navigate(['/chat', fallback.id]);
  }
}
