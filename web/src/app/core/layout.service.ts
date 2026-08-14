import { Injectable, signal } from '@angular/core';

/**
 * Shared layout state. The mobile drawer is controlled from the chat header;
 * desktop collapse is controlled from the sidebar/app shell.
 */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly sidebarOpen = signal(false);
  readonly sidebarCollapsed = signal(false);

  toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }

  collapseSidebar(): void {
    this.sidebarCollapsed.set(true);
    this.closeSidebar();
  }

  expandSidebar(): void {
    this.sidebarCollapsed.set(false);
  }
}
