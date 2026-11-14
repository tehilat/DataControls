import { Component, computed, input, linkedSignal, output } from '@angular/core';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { FieldDto } from '../core/api/models';
import { DATA_TYPE_ICONS } from '../core/labels';

interface FieldGroup {
  label: string;
  fields: FieldDto[];
}

/**
 * Picks a field by its business name. Fields are grouped by the relation they are reached through
 * ("משלוח", "רחפן", "רחפן › מוקד") and can be searched by name or description.
 */
@Component({
  selector: 'app-field-picker',
  imports: [MatAutocompleteModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <mat-form-field class="field-picker" subscriptSizing="dynamic">
      <mat-label>{{ label() }}</mat-label>
      @if (selected(); as f) {
        <mat-icon matPrefix aria-hidden="true">{{ icon(f) }}</mat-icon>
      }
      <input
        matInput
        [value]="text()"
        (input)="text.set($any($event.target).value)"
        (focus)="selectAllOnFocus($event)"
        [matAutocomplete]="auto"
        [placeholder]="placeholder()"
        [attr.aria-invalid]="invalid()"
      />
      <mat-autocomplete #auto (optionSelected)="select($event)" (closed)="restoreText()" class="field-picker-panel">
        @for (group of groups(); track group.label) {
          <mat-optgroup [label]="group.label">
            @for (f of group.fields; track f.path) {
              <mat-option [value]="f.path">
                <span class="option-line">
                  <mat-icon aria-hidden="true" class="option-icon">{{ icon(f) }}</mat-icon>
                  <span>{{ f.displayName }}</span>
                  @if (f.unit) {
                    <span class="muted small">({{ f.unit }})</span>
                  }
                </span>
              </mat-option>
            }
          </mat-optgroup>
        } @empty {
          <mat-option disabled>לא נמצאו שדות מתאימים</mat-option>
        }
      </mat-autocomplete>
      @if (selected()?.description; as description) {
        <mat-hint>{{ description }}</mat-hint>
      }
    </mat-form-field>
  `,
  styles: `
    .field-picker { width: 100%; }
    .option-line { display: inline-flex; align-items: center; gap: 6px; }
    .option-icon { font-size: 18px; width: 18px; height: 18px; color: var(--app-muted); }
  `,
})
export class FieldPicker {
  readonly fields = input.required<FieldDto[]>();
  readonly value = input<string | null>(null);
  readonly label = input('שדה');
  readonly placeholder = input('חיפוש שדה…');
  readonly invalid = input(false);
  readonly valueChange = output<string>();

  protected readonly selected = computed(() => {
    const value = this.value()?.toLowerCase();
    return value ? this.fields().find((f) => f.path.toLowerCase() === value) : undefined;
  });

  /** The text in the box: the selected field's full name, or what the user is typing. */
  protected readonly text = linkedSignal(() => this.selected()?.fullDisplayName ?? '');

  protected readonly groups = computed<FieldGroup[]>(() => {
    const typed = this.text().trim().toLowerCase();
    const query = typed === (this.selected()?.fullDisplayName ?? '').toLowerCase() ? '' : typed;
    const groups = new Map<string, FieldGroup>();
    for (const f of this.fields()) {
      if (query && !`${f.fullDisplayName} ${f.description ?? ''}`.toLowerCase().includes(query)) continue;
      const label = f.relationPath.length ? f.relationPath.join(' › ') : `${f.entityDisplayName} (שדות ישירים)`;
      let group = groups.get(label);
      if (!group) groups.set(label, (group = { label, fields: [] }));
      group.fields.push(f);
    }
    return [...groups.values()];
  });

  protected icon(f: FieldDto): string {
    return DATA_TYPE_ICONS[f.dataType];
  }

  protected select(event: MatAutocompleteSelectedEvent): void {
    const path = event.option.value as string;
    this.valueChange.emit(path);
    this.text.set(this.fields().find((f) => f.path === path)?.fullDisplayName ?? '');
  }

  protected restoreText(): void {
    this.text.set(this.selected()?.fullDisplayName ?? '');
  }

  protected selectAllOnFocus(event: FocusEvent): void {
    (event.target as HTMLInputElement).select();
  }
}
