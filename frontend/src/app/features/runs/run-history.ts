import { DatePipe } from '@angular/common';
import { Component, computed, input, inject, linkedSignal, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { toApiError } from '../../core/api/api-error';
import { RunsApi } from '../../core/api/api.services';
import { PageDto, RunListItemDto, RunStatus } from '../../core/api/models';
import { formatDuration } from '../../core/format';
import { HEBREW_PAGINATOR } from '../../core/hebrew-paginator-intl';
import { RUN_STATUS_LABELS } from '../../core/labels';
import { RunStatusChip } from '../../shared/chips';
import { ProblemAlert } from '../../shared/problem-alert';

/** Run history, newest first, with paging and a status filter. Optionally limited to one control. */
@Component({
  selector: 'app-run-history',
  imports: [DatePipe, RouterLink, MatTableModule, MatPaginatorModule, MatProgressBarModule, MatFormFieldModule, MatSelectModule, RunStatusChip, ProblemAlert],
  providers: [HEBREW_PAGINATOR],
  template: `
    <div class="toolbar">
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>סטטוס</mat-label>
        <mat-select [value]="status()" (selectionChange)="setStatus($event.value)">
          <mat-option value="">הכול</mat-option>
          @for (s of statuses; track s) {
            <mat-option [value]="s">{{ statusLabels[s] }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
    </div>

    <app-problem-alert [error]="error()" />

    <div class="table-card">
      @if (runs.isLoading()) {
        <mat-progress-bar mode="indeterminate" aria-label="טוען הרצות" />
      }
      <div class="table-scroll">
        <table mat-table [dataSource]="page().items">
          <caption class="cdk-visually-hidden">היסטוריית הרצות</caption>
          <ng-container matColumnDef="startedAt">
            <th mat-header-cell *matHeaderCellDef scope="col">מועד</th>
            <td mat-cell *matCellDef="let r">
              <a [routerLink]="['/runs', r.runId]" [class.current]="r.runId === currentRunId()">
                {{ r.startedAt | date: 'dd/MM/yyyy HH:mm:ss' }}
              </a>
              @if (r.runId === currentRunId()) {
                <span class="muted small"> (מוצגת)</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="control">
            <th mat-header-cell *matHeaderCellDef scope="col">בקרה</th>
            <td mat-cell *matCellDef="let r">{{ r.controlName }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef scope="col">סטטוס</th>
            <td mat-cell *matCellDef="let r">
              <app-run-status-chip [status]="r.status" />
              @if (r.errorMessage) {
                <div class="small danger-text">{{ r.errorMessage }}</div>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="violations">
            <th mat-header-cell *matHeaderCellDef scope="col">חריגות</th>
            <td mat-cell *matCellDef="let r" class="violations">
              {{ r.violationCount ?? '—' }}
              @if (r.truncated) {
                <span class="muted small">(נשמר חלק)</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="duration">
            <th mat-header-cell *matHeaderCellDef scope="col" class="hide-sm">משך</th>
            <td mat-cell *matCellDef="let r" class="hide-sm">{{ duration(r.durationMs) }}</td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns()"></tr>
          <tr mat-row *matRowDef="let r; columns: columns()"></tr>
          <tr class="mat-row" *matNoDataRow>
            <td class="mat-cell empty-cell" [attr.colspan]="columns().length">
              @if (!runs.isLoading()) {
                לא נמצאו הרצות.
              }
            </td>
          </tr>
        </table>
      </div>
      <mat-paginator
        [length]="page().totalCount"
        [pageIndex]="pageIndex()"
        [pageSize]="pageSize()"
        [pageSizeOptions]="[10, 20, 50, 100]"
        (page)="onPage($event)"
        aria-label="דפדוף בהיסטוריית ההרצות"
      />
    </div>
  `,
  styles: `
    .violations { font-weight: 600; }
    .current { font-weight: 700; }
    .empty-cell { padding: 24px; text-align: center; color: var(--app-muted); }
    @media (max-width: 700px) { .hide-sm { display: none; } }
  `,
})
export class RunHistory {
  private readonly runsApi = inject(RunsApi);

  readonly controlId = input<number | null>(null);
  /** Highlights the run currently shown on the page. */
  readonly currentRunId = input<number | null>(null);

  protected readonly statuses: RunStatus[] = ['Succeeded', 'Failed', 'Running'];
  protected readonly statusLabels = RUN_STATUS_LABELS;
  protected readonly status = signal<RunStatus | ''>('');
  /** Back to the first page whenever the control or the status filter changes. */
  protected readonly pageIndex = linkedSignal({ source: () => [this.controlId(), this.status()], computation: () => 0 });
  protected readonly pageSize = signal(20);

  protected readonly columns = computed(() =>
    this.controlId() === null ? ['startedAt', 'control', 'status', 'violations', 'duration'] : ['startedAt', 'status', 'violations', 'duration'],
  );

  protected readonly runs = rxResource({
    params: () => ({
      controlId: this.controlId() ?? undefined,
      status: this.status() || undefined,
      page: this.pageIndex() + 1,
      pageSize: this.pageSize(),
    }),
    stream: ({ params }) => this.runsApi.list(params),
  });
  protected readonly page = linkedSignal<PageDto<RunListItemDto> | undefined, PageDto<RunListItemDto>>({
    source: () => this.runs.value(),
    computation: (value, previous) => value ?? previous?.value ?? { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 },
  });
  protected readonly error = computed(() => (this.runs.error() ? toApiError(this.runs.error()) : null));

  protected setStatus(status: RunStatus | ''): void {
    this.status.set(status);
  }

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  protected duration(ms: number | null): string {
    return formatDuration(ms);
  }

  reload(): void {
    this.runs.reload();
  }
}
