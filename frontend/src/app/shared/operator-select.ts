import { Component, input, output } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { OperatorDto } from '../core/api/models';

/** The operators allowed for the field's type, by their Hebrew names (from meta.Operators). */
@Component({
  selector: 'app-operator-select',
  imports: [MatFormFieldModule, MatSelectModule],
  template: `
    <mat-form-field subscriptSizing="dynamic" class="full-width">
      <mat-label>{{ label() }}</mat-label>
      <mat-select
        [value]="value()"
        (selectionChange)="valueChange.emit($event.value)"
        [disabled]="disabled() || operators().length === 0"
        [attr.aria-invalid]="invalid()"
      >
        @for (op of operators(); track op.key) {
          <mat-option [value]="op.key">{{ op.displayName }}</mat-option>
        }
      </mat-select>
    </mat-form-field>
  `,
})
export class OperatorSelect {
  readonly operators = input.required<OperatorDto[]>();
  readonly value = input<string | null>(null);
  readonly label = input('תנאי');
  readonly disabled = input(false);
  readonly invalid = input(false);
  readonly valueChange = output<string>();
}
