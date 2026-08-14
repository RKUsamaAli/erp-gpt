import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  input,
  output,
  viewChild,
} from '@angular/core';

/**
 * Inline rename box, used for both projects and chats — the rename interaction
 * is identical for each, so it lives once here rather than twice in the tree.
 *
 * Enter or blur commits, Escape cancels. Blank input is treated as a cancel by
 * the store, which keeps the previous name.
 */
@Component({
  selector: 'app-rename-field',
  template: `
    <input
      #field
      type="text"
      class="form-control form-control-sm"
      [value]="value()"
      (keydown.enter)="commit(field.value)"
      (keydown.escape)="cancelled.emit()"
      (blur)="commit(field.value)"
      [attr.aria-label]="label()"
    />
  `,
  styles: `
    :host {
      display: block;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RenameFieldComponent {
  readonly value = input.required<string>();
  readonly label = input('Rename');
  readonly committed = output<string>();
  readonly cancelled = output<void>();

  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  private done = false;

  constructor() {
    afterNextRender(() => {
      const el = this.field().nativeElement;
      el.focus();
      el.select();
    });
  }

  /** Enter fires, then blur fires on teardown — only the first should count. */
  commit(next: string): void {
    if (this.done) return;
    this.done = true;
    this.committed.emit(next);
  }
}
