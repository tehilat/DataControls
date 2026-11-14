import { Component, computed, input, output } from '@angular/core';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { ColumnDto, OptionDto, SortDirection, ViolationDto } from '../core/api/models';
import { formatValue } from '../core/format';

export interface ResultsSort {
  key: string | null;
  dir: SortDirection;
}

/**
 * The failing records of a run or preview: record, reason, then one column per result field.
 * Generic — columns come from the response, never from code. Sorting (if enabled) is done by the server.
 */
@Component({
  selector: 'app-results-table',
  imports: [MatTableModule, MatSortModule],
  template: `
    <div class="table-scroll" tabindex="0" role="region" [attr.aria-label]="caption()">
      <table
        mat-table
        [dataSource]="rows()"
        matSort
        [matSortActive]="sort().key ?? ''"
        [matSortDirection]="sort().key ? sort().dir : ''"
        [matSortDisabled]="!sortable()"
        (matSortChange)="onSort($event)"
        class="results-table"
      >
        <caption class="cdk-visually-hidden">{{ caption() }}</caption>

        <ng-container matColumnDef="__id">
          <th mat-header-cell *matHeaderCellDef scope="col">מזהה</th>
          <td mat-cell *matCellDef="let row" class="record-cell"><bdi>{{ row.recordId ?? '—' }}</bdi></td>
        </ng-container>

        <ng-container matColumnDef="__record">
          <th mat-header-cell *matHeaderCellDef scope="col">רשומה</th>
          <td mat-cell *matCellDef="let row" class="record-cell"><bdi>{{ row.displayValue ?? '—' }}</bdi></td>
        </ng-container>

        <ng-container matColumnDef="__reason">
          <th mat-header-cell *matHeaderCellDef scope="col">סיבת החריגה</th>
          <td mat-cell *matCellDef="let row" class="reason-cell">{{ row.reason }}</td>
        </ng-container>

        @for (column of columns(); track column.key) {
          <ng-container [matColumnDef]="column.key">
            <th mat-header-cell *matHeaderCellDef mat-sort-header scope="col" [sortActionDescription]="'מיון לפי ' + column.displayName">
              {{ column.displayName }}
            </th>
            <td mat-cell *matCellDef="let row" [class.nowrap]="column.dataType !== 'text'">
              <bdi>{{ format(row, column) }}</bdi>
            </td>
          </ng-container>
        }

        <tr mat-header-row *matHeaderRowDef="displayedColumns(); sticky: true"></tr>
        <tr mat-row *matRowDef="let row; columns: displayedColumns()"></tr>
        <tr class="mat-row" *matNoDataRow>
          <td class="mat-cell empty-cell" [attr.colspan]="displayedColumns().length">{{ emptyText() }}</td>
        </tr>
      </table>
    </div>
  `,
  styles: `
    .record-cell { font-weight: 500; white-space: nowrap; }
    .reason-cell { min-width: 280px; max-width: 520px; color: var(--app-danger-text); }
    .nowrap { white-space: nowrap; }
    .empty-cell { padding: 24px; text-align: center; color: var(--app-muted); }
  `,
})
export class ResultsTable {
  readonly columns = input.required<ColumnDto[]>();
  readonly rows = input.required<ViolationDto[]>();
  /** List values by column key, to show "נמסר" rather than "Delivered". */
  readonly options = input<ReadonlyMap<string, OptionDto[]>>(new Map());
  readonly sortable = input(false);
  readonly sort = input<ResultsSort>({ key: null, dir: 'asc' });
  readonly caption = input('רשומות חריגות');
  readonly emptyText = input('לא נמצאו רשומות חריגות');
  readonly sortChange = output<ResultsSort>();

  /** The record's display name gets its own column only when it adds something beyond the id. */
  private readonly hasDisplayName = computed(() =>
    this.rows().some((r) => !!r.displayValue && r.displayValue !== r.recordId),
  );

  protected readonly displayedColumns = computed(() => [
    '__id',
    ...(this.hasDisplayName() ? ['__record'] : []),
    '__reason',
    ...this.columns().map((c) => c.key),
  ]);

  protected format(row: ViolationDto, column: ColumnDto): string {
    return formatValue(row.values[column.key], column.dataType, column.unit, this.options().get(column.key));
  }

  protected onSort(sort: Sort): void {
    this.sortChange.emit({ key: sort.direction ? sort.active : null, dir: sort.direction || 'asc' });
  }
}
