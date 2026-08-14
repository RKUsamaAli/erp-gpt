import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ThemeService } from '../../core/theme.service';

@Component({
  selector: 'app-theme-toggle',
  templateUrl: './theme-toggle.component.html',
  styleUrl: './theme-toggle.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemeToggleComponent {
  private readonly themes = inject(ThemeService);

  readonly theme = this.themes.theme;
  /** Describes what the button will DO, which is what a label should say. */
  readonly label = computed(() =>
    this.theme() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme',
  );

  toggle(): void {
    this.themes.toggle();
  }
}
