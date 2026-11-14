import { Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { EditorOperand, EditorRule } from '../../../core/definition/definition.model';
import { describePath } from '../../../core/definition/definition-tree';
import { FieldPicker } from '../../../shared/field-picker';
import { OperatorSelect } from '../../../shared/operator-select';
import { ConditionContext } from './condition-context';
import { OperandEditor } from './operand-editor';

/** One condition: field → operator → what to compare to. */
@Component({
  selector: 'app-condition-rule',
  imports: [FieldPicker, OperatorSelect, OperandEditor, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <div class="rule" [class.has-error]="messages().length > 0" role="group" [attr.aria-label]="location()">
      <div class="rule-grid">
        <div class="rule-field">
          <app-field-picker
            [fields]="ctx.fields()"
            [value]="rule().field"
            [invalid]="hasError('.field')"
            (valueChange)="changeField($event)"
          />
        </div>
        <div class="rule-operator">
          <app-operator-select
            [operators]="operators()"
            [value]="rule().operator"
            [disabled]="!field()"
            [invalid]="hasError('.operator')"
            (valueChange)="changeOperator($event)"
          />
        </div>
        <div class="rule-operand">
          @if (field() && operator()?.requiresValue) {
            <app-operand-editor
              [field]="field()!"
              [operator]="operator()!"
              [operand]="rule().operand"
              [allFields]="ctx.fields()"
              [invalid]="hasError('.compareTo')"
              (operandChange)="changeOperand($event)"
            />
          }
        </div>
        <button
          mat-icon-button
          type="button"
          class="rule-remove"
          (click)="remove.emit()"
          [matTooltip]="'מחיקת ' + location()"
          [attr.aria-label]="'מחיקת ' + location()"
        >
          <mat-icon>delete_outline</mat-icon>
        </button>
      </div>
      @if (messages().length) {
        <ul class="inline-errors" role="alert">
          @for (m of messages(); track $index) {
            <li>{{ m }}</li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .rule { padding: 10px 12px; border: 1px solid var(--app-border); border-radius: 10px; background: var(--app-surface); }
    .rule.has-error { border-color: var(--app-danger); }
    .rule-grid {
      display: grid;
      grid-template-columns: minmax(200px, 1.3fr) minmax(150px, 0.8fr) minmax(220px, 1.6fr) auto;
      gap: 8px;
      align-items: start;
    }
    .rule-remove { margin-top: 4px; }
    @media (max-width: 900px) {
      .rule-grid { grid-template-columns: 1fr auto; }
      .rule-field { grid-column: 1; }
      .rule-remove { grid-column: 2; grid-row: 1; }
      .rule-operator, .rule-operand { grid-column: 1 / -1; }
    }
  `,
})
export class ConditionRule {
  protected readonly ctx = inject(ConditionContext);

  readonly rule = input.required<EditorRule>();
  readonly path = input.required<string>();
  readonly ruleChange = output<EditorRule>();
  readonly remove = output<void>();

  protected readonly field = computed(() => this.ctx.field(this.rule().field));
  protected readonly operators = computed(() => this.ctx.operatorsFor(this.field()?.dataType));
  protected readonly operator = computed(() => {
    const field = this.field();
    return field ? this.ctx.operator(field.dataType, this.rule().operator) : undefined;
  });
  protected readonly messages = computed(() => this.ctx.messagesAt(this.path(), false));
  protected readonly location = computed(() => describePath(this.path()));

  protected hasError(suffix: string): boolean {
    const path = this.path() + suffix;
    return this.ctx.errors().some((e) => e.path === path || e.path.startsWith(path + '.'));
  }

  protected changeField(path: string): void {
    const previous = this.field();
    const next = this.ctx.field(path);
    if (!next) return;
    const sameType = previous?.dataType === next.dataType;
    // keep the operator when the type did not change; otherwise default to the first allowed operator
    const operator = sameType && this.operator() ? this.operator()! : this.ctx.operatorsFor(next.dataType)[0];
    // a value of the same type still makes sense — except a list value, which belongs to the previous list
    const current = this.rule().operand;
    const keepOperand = sameType && !!current && (current.kind !== 'value' || next.dataType !== 'enum');
    this.ruleChange.emit({
      ...this.rule(),
      field: next.path,
      operator: operator?.key ?? null,
      operand: operator?.requiresValue ? (keepOperand ? current : { kind: 'value', value: null }) : null,
    });
  }

  protected changeOperator(key: string): void {
    const field = this.field();
    const operator = field ? this.ctx.operator(field.dataType, key) : undefined;
    if (!operator) return;
    let operand = this.rule().operand;
    if (!operator.requiresValue) operand = null;
    else if (!operand || (operand.kind === 'field' && !operator.allowFieldCompare)) operand = { kind: 'value', value: null };
    this.ruleChange.emit({ ...this.rule(), operator: operator.key, operand });
  }

  protected changeOperand(operand: EditorOperand): void {
    this.ruleChange.emit({ ...this.rule(), operand });
  }
}
