import { Component, computed, input, output } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDatepickerInputEvent, MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { DataType, OptionDto } from '../core/api/models';
import { ScalarValue } from '../core/definition/definition.model';
import { fromIsoDate, toIsoDate } from '../core/format';

/**
 * A value input that matches the field type: number (with its unit), text, date picker, date + time,
 * a list of values, or yes/no. Emits values in the API's canonical form
 * (number, "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", true/false, raw list value).
 */
@Component({
  selector: 'app-value-input',
  imports: [MatFormFieldModule, MatInputModule, MatSelectModule, MatDatepickerModule, MatButtonToggleModule],
  template: `
    @switch (dataType()) {
      @case ('boolean') {
        <mat-button-toggle-group
          [value]="value()"
          (change)="valueChange.emit($event.value)"
          [attr.aria-label]="label()"
          hideSingleSelectionIndicator
          class="bool-toggle"
        >
          <mat-button-toggle [value]="true">כן</mat-button-toggle>
          <mat-button-toggle [value]="false">לא</mat-button-toggle>
        </mat-button-toggle-group>
      }
      @case ('enum') {
        <mat-form-field subscriptSizing="dynamic" class="full-width">
          <mat-label>{{ label() }}</mat-label>
          <mat-select [value]="value()" (selectionChange)="valueChange.emit($event.value)" [attr.aria-invalid]="invalid()">
            @for (option of options(); track option.value) {
              <mat-option [value]="option.value">{{ option.displayName }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      @case ('date') {
        <mat-form-field subscriptSizing="dynamic" class="full-width">
          <mat-label>{{ label() }}</mat-label>
          <input
            matInput
            [matDatepicker]="picker"
            [value]="dateValue()"
            (dateChange)="onDate($event)"
            placeholder="dd/mm/yyyy"
            [attr.aria-invalid]="invalid()"
          />
          <mat-datepicker-toggle matIconSuffix [for]="picker" aria-label="בחירת תאריך" />
          <mat-datepicker #picker />
        </mat-form-field>
      }
      @case ('datetime') {
        <mat-form-field subscriptSizing="dynamic" class="full-width">
          <mat-label>{{ label() }}</mat-label>
          <input
            matInput
            type="datetime-local"
            [value]="dateTimeValue()"
            (change)="onText($any($event.target).value)"
            [attr.aria-invalid]="invalid()"
          />
        </mat-form-field>
      }
      @case ('number') {
        <mat-form-field subscriptSizing="dynamic" class="full-width">
          <mat-label>{{ label() }}</mat-label>
          <input
            matInput
            type="number"
            inputmode="decimal"
            dir="ltr"
            [value]="value() ?? ''"
            (input)="onNumber($any($event.target).value)"
            [attr.aria-invalid]="invalid()"
          />
          @if (unit()) {
            <span matTextSuffix>&nbsp;{{ unit() }}</span>
          }
        </mat-form-field>
      }
      @default {
        <mat-form-field subscriptSizing="dynamic" class="full-width">
          <mat-label>{{ label() }}</mat-label>
          <input matInput [value]="value() ?? ''" (input)="onText($any($event.target).value)" maxlength="4000" [attr.aria-invalid]="invalid()" />
        </mat-form-field>
      }
    }
  `,
  styles: `
    .bool-toggle { margin-block: 4px; }
  `,
})
export class ValueInput {
  readonly dataType = input.required<DataType>();
  readonly value = input<ScalarValue>(null);
  readonly unit = input<string | null>(null);
  readonly options = input<OptionDto[]>([]);
  readonly label = input('ערך');
  readonly invalid = input(false);
  readonly valueChange = output<ScalarValue>();

  protected readonly dateValue = computed(() => fromIsoDate(this.value()));
  /** datetime-local needs "yyyy-MM-ddTHH:mm". */
  protected readonly dateTimeValue = computed(() => {
    const v = this.value();
    return typeof v === 'string' ? v.slice(0, 16) : '';
  });

  protected onNumber(text: string): void {
    const trimmed = text.trim();
    const n = Number(trimmed);
    this.valueChange.emit(trimmed === '' || !Number.isFinite(n) ? null : n);
  }

  protected onText(text: string): void {
    this.valueChange.emit(text === '' ? null : text);
  }

  protected onDate(event: MatDatepickerInputEvent<Date>): void {
    this.valueChange.emit(event.value ? toIsoDate(event.value) : null);
  }
}
