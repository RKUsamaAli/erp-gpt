import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { CHAT_STORE } from '../../core/chat-store';
import { Chat, Project } from '../../models/workspace.models';
import { RenameFieldComponent } from '../rename-field/rename-field.component';

/** Which item, if any, is currently being renamed. */
type Editing = { kind: 'project' | 'chat'; id: string } | null;

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

  readonly projects = this.store.projects;
  /** Chats that belong to no project, shown under "Recent". */
  readonly looseChats = computed(() => this.store.chats().filter((c) => c.projectId === null));
  readonly editing = signal<Editing>(null);
  /** Projects collapse so a long list stays navigable. */
  private readonly collapsed = signal<ReadonlySet<string>>(new Set());

  chatsIn(project: Project): Chat[] {
    return this.store.chats().filter((c) => c.projectId === project.id);
  }

  isCollapsed(project: Project): boolean {
    return this.collapsed().has(project.id);
  }

  toggleCollapsed(project: Project): void {
    this.collapsed.update((open) => {
      const next = new Set(open);
      next.has(project.id) ? next.delete(project.id) : next.add(project.id);
      return next;
    });
  }

  newChat(projectId: string | null = null): void {
    const chat = this.store.createChat(projectId);
    this.router.navigate(['/chat', chat.id]);
  }

  newProject(): void {
    const project = this.store.createProject();
    // Open straight into rename: a project called "New project" is never what
    // anyone wants, and naming it is the next thing they would do anyway.
    this.startEditing('project', project.id);
  }

  startEditing(kind: 'project' | 'chat', id: string): void {
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
}
