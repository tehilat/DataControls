import { Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { ControlsApi } from '../../core/api/api.services';
import { RunHistory } from './run-history';

/** /runs and /runs?controlId=7 */
@Component({
  selector: 'app-run-history-page',
  imports: [RunHistory, MatFormFieldModule, MatSelectModule],
  template: `
    <div class="page-header">
      <h1>היסטוריית הרצות</h1>
    </div>
    <app-run-history [controlId]="selectedId()">
      <mat-form-field historyFilters subscriptSizing="dynamic" class="control-filter">
        <mat-label>בקרה</mat-label>
        <!-- 0 = all controls (a null value would show an empty select) -->
        <mat-select [value]="selectedId() ?? 0" (selectionChange)="selectControl($event.value)">
          <mat-option [value]="0">כל הבקרות</mat-option>
          @for (c of controls(); track c.id) {
            <mat-option [value]="c.id">{{ c.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </app-run-history>
  `,
  styles: `.control-filter { width: 320px; max-width: 100%; }`,
})
export class RunHistoryPage {
  private readonly router = inject(Router);
  private readonly controlsApi = inject(ControlsApi);

  /** Query parameter. */
  readonly controlId = input<string>();

  protected readonly selectedId = computed(() => (this.controlId() ? Number(this.controlId()) : null));
  protected readonly controls = toSignal(this.controlsApi.list().pipe(catchError(() => of([]))), { initialValue: [] });

  protected selectControl(id: number): void {
    this.router.navigate([], { queryParams: { controlId: id || undefined } });
  }
}
