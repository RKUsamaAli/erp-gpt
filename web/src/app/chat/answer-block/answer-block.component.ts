import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AnswerBlock } from '../../models/chat.models';

/**
 * Renders one AnswerBlock through ordinary Angular interpolation.
 *
 * This component is the reason no `bypassSecurityTrustHtml` is needed anywhere:
 * answers arrive as data, not markup, so escaping is automatic.
 */
@Component({
  selector: 'app-answer-block',
  templateUrl: './answer-block.component.html',
  styleUrl: './answer-block.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnswerBlockComponent {
  readonly block = input.required<AnswerBlock>();
}
