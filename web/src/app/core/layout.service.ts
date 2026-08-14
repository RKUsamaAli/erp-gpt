import { Injectable, signal } from '@angular/core';

/**
 * Whether the sidebar is showing on narrow screens. It lives here rather than
 * in the shell because the button that opens it sits in the chat header, and a
 * shared signal is lighter than threading an output up through the tree.
 */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly sidebarOpen = signal(false);

  toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }
}
