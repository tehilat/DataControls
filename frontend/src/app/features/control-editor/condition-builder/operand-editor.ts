import { Component, computed, input, output } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { FieldDto, OperatorDto } from '../../../core/api/models';
import { EditorOperand, OperandKind, ScalarValue } from '../../../core/definition/definition.model';
import { isComparableField, isTemporal } from '../../../core/definition/definition-tree';
import { FieldPicker } from '../../../shared/field-picker';
import { ValueInput } from '../../../shared/value-input';

interface KindOption {
  kind: OperandKind;
  label: string;
}

/**
 * What a condition compares to: a fixed value, another field, "today ± days" or "now".
 * Only the kinds that the field type and operator allow are offered.
 */
@Component({
  selector: 'app-operand-editor',
  imports: [MatFormFieldModule, MatInputModule, MatSelectModule, ValueInput, FieldPicker],
  template: `
    <div class="operand">
      @if (kinds().length > 1) {
        <mat-form-field subscriptSizing="dynamic" class="kind-select">
          <mat-label>השוואה ל</mat-label>
          <mat-select [value]="operand()?.kind ?? 'value'" (selectionChange)="changeKind($event.value)">
            @for (k of kinds(); track k.kind) {
              <mat-option [value]="k.kind">{{ k.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }

      @switch (operand()?.kind ?? 'value') {
        @case ('field') {
          <div class="operand-main">
            <app-field-picker
              [fields]="comparableFields()"
              [value]="fieldValue()"
              label="שדה להשוואה"
              [invalid]="invalid()"
              (valueChange)="operandChange.emit({ kind: 'field', field: $event })"
            />
          </div>
        }
        @case ('today') {
          <div class="operand-main today">
            <mat-form-field subscriptSizing="dynamic" class="days">
              <mat-label>מספר ימים</mat-label>
              <input
                matInput
                type="number"
                min="0"
                max="36500"
                step="1"
                dir="ltr"
                [value]="todayDays()"
                (input)="changeDays($any($event.target).value)"
                [attr.aria-invalid]="invalid()"
              />
            </mat-form-field>
            <mat-form-field subscriptSizing="dynamic" class="direction">
              <mat-label>כיוון</mat-label>
              <mat-select [value]="todaySign()" (selectionChange)="changeSign($event.value)">
                <mat-option [value]="1">אחרי היום (+)</mat-option>
                <mat-option [value]="-1">לפני היום (−)</mat-option>
              </mat-select>
            </mat-form-field>
          </div>
        }
        @case ('now') {
          <div class="operand-main now muted">המועד הנוכחי בעת ההרצה</div>
        }
        @default {
          <div class="operand-main">
            <app-value-input
              [dataType]="field().dataType"
              [unit]="field().unit"
              [options]="field().options"
              [value]="scalarValue()"
              [invalid]="invalid()"
              (valueChange)="changeValue($event)"
            />
          </div>
        }
      }
    </div>
  `,
  styles: `
    .operand { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-start; width: 100%; }
    .kind-select { width: 160px; }
    .operand-main { flex: 1 1 200px; min-width: 0; }
    .today { display: flex; gap: 8px; flex-wrap: wrap; }
    .days { width: 120px; }
    .direction { flex: 1 1 150px; }
    .now { padding-block: 16px; }
  `,
})
export class OperandEditor {
  readonly field = input.required<FieldDto>();
  readonly operator = input.required<OperatorDto>();
  readonly operand = input<EditorOperand | null>(null);
  readonly allFields = input.required<FieldDto[]>();
  readonly invalid = input(false);
  readonly operandChange = output<EditorOperand>();

  protected readonly comparableFields = computed(() => this.allFields().filter((f) => isComparableField(this.field(), f)));

  protected readonly kinds = computed<KindOption[]>(() => {
    const kinds: KindOption[] = [{ kind: 'value', label: 'ערך קבוע' }];
    if (this.operator().allowFieldCompare && this.comparableFields().length) kinds.push({ kind: 'field', label: 'שדה אחר' });
    if (isTemporal(this.field().dataType)) {
      kinds.push({ kind: 'today', label: 'היום ± ימים' });
      if (this.field().dataType === 'datetime') kinds.push({ kind: 'now', label: 'עכשיו' });
    }
    return kinds;
  });

  protected readonly scalarValue = computed<ScalarValue>(() => {
    const op = this.operand();
    return op?.kind === 'value' ? op.value : null;
  });
  protected readonly fieldValue = computed(() => {
    const op = this.operand();
    return op?.kind === 'field' ? op.field : null;
  });
  private readonly offset = computed(() => {
    const op = this.operand();
    return op?.kind === 'today' ? op.offsetDays : 0;
  });
  protected readonly todayDays = computed(() => Math.abs(this.offset()));
  protected readonly todaySign = computed(() => (this.offset() < 0 ? -1 : 1));

  protected changeKind(kind: OperandKind): void {
    switch (kind) {
      case 'field':
        return this.operandChange.emit({ kind: 'field', field: null });
      case 'today':
        return this.operandChange.emit({ kind: 'today', offsetDays: 0 });
      case 'now':
        return this.operandChange.emit({ kind: 'now' });
      default:
        return this.operandChange.emit({ kind: 'value', value: null });
    }
  }

  protected changeValue(value: ScalarValue): void {
    this.operandChange.emit({ kind: 'value', value });
  }

  protected changeDays(text: string): void {
    const days = Math.trunc(Math.abs(Number(text)) || 0);
    this.operandChange.emit({ kind: 'today', offsetDays: days * this.todaySign() });
  }

  protected changeSign(sign: number): void {
    this.operandChange.emit({ kind: 'today', offsetDays: this.todayDays() * sign });
  }
}
