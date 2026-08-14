import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'erp-gpt.theme';

/**
 * Owns the one attribute both palettes hang off: `data-bs-theme` on <html>.
 * Setting it switches this app's tokens (styles.scss) and Bootstrap 5.3's own
 * at the same time.
 *
 * Until someone chooses explicitly we follow the operating system, and we keep
 * following it — a user who has never touched the toggle should see the app
 * turn dark when their machine does.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly media = window.matchMedia('(prefers-color-scheme: dark)');
  private readonly stored = localStorage.getItem(STORAGE_KEY) as Theme | null;

  readonly theme = signal<Theme>(this.stored ?? this.systemTheme());
  /** True while no explicit choice has been made, so we still track the OS. */
  private following = this.stored === null;

  constructor() {
    effect(() => document.documentElement.setAttribute('data-bs-theme', this.theme()));

    this.media.addEventListener('change', () => {
      if (this.following) this.theme.set(this.systemTheme());
    });
  }

  toggle(): void {
    this.set(this.theme() === 'dark' ? 'light' : 'dark');
  }

  set(theme: Theme): void {
    this.following = false;
    localStorage.setItem(STORAGE_KEY, theme);
    this.theme.set(theme);
  }

  private systemTheme(): Theme {
    return this.media.matches ? 'dark' : 'light';
  }
}
