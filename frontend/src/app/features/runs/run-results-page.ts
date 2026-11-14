import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ControlsApi, MetadataApi, RunsApi } from '../../core/api/api.services';
import { ColumnDto, OptionDto, PageDto, ViolationDto } from '../../core/api/models';
import { ScalarValue } from '../../core/definition/definition.model';
import { formatDuration, formatValue } from '../../core/format';
import { HEBREW_PAGINATOR } from '../../core/hebrew-paginator-intl';
import { RunStatusChip, SeverityChip } from '../../shared/chips';
import { Notifier } from '../../shared/notifier.service';
import { ProblemAlert } from '../../shared/problem-alert';
import { ResultsSort, ResultsTable } from '../../shared/results-table';
import { ValueInput } from '../../shared/value-input';
import { RunHistory } from './run-history';

interface ActiveFilter {
  key: string;
  value: string;
  label: string;
}

const EMPTY_PAGE: PageDto<ViolationDto> = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

/** /runs/:runId — the failing records of a run (stored on the server) with paging, search, typed filters and sort. */
@Component({
  selector: 'app-run-results-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTabsModule,
    MatTooltipModule,
    ProblemAlert,
    ResultsTable,
    RunHistory,
    RunStatusChip,
    SeverityChip,
    ValueInput,
  ],
  providers: [HEBREW_PAGINATOR],
  templateUrl: './run-results-page.html',
  styleUrl: './run-results-page.scss',
})
export class RunResultsPage {
  private readonly runsApi = inject(RunsApi);
  private readonly controlsApi = inject(ControlsApi);
  private readonly metadataApi = inject(MetadataApi);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);

  /** Route parameter. */
  readonly runId = input.required<string>();
  protected readonly id = computed(() => Number(this.runId()));

  protected readonly run = rxResource({ params: () => this.id(), stream: ({ params }) => this.runsApi.get(params) });
  protected readonly runError = computed(() => (this.run.error() ? toApiError(this.run.error()) : null));

  /** The control (for its entity and severity) and the entity's own fields (to show list values by name). */
  private readonly controlId = computed(() => this.run.value()?.controlId);
  protected readonly control = rxResource({
    params: () => this.controlId(),
    stream: ({ params }) => this.controlsApi.get(params),
  });
  private readonly fields = toSignal(
    toObservable(computed(() => this.control.value()?.rootEntity)).pipe(
      switchMap((key) => (key ? this.metadataApi.fields(key, 0).pipe(catchError(() => of([]))) : of([]))),
    ),
    { initialValue: [] },
  );
  protected readonly options = computed(() => new Map<string, OptionDto[]>(this.fields().map((f) => [f.path, f.options])));
  protected readonly columns = computed(() => this.run.value()?.columns ?? []);

  // ---- query state; every change of the run resets it ----
  protected readonly search = linkedSignal({ source: this.id, computation: () => '' });
  private readonly debouncedSearch = toSignal(toObservable(this.search).pipe(debounceTime(350), distinctUntilChanged()), {
    initialValue: '',
  });
  protected readonly filters = linkedSignal<number, ActiveFilter[]>({ source: this.id, computation: () => [] });
  protected readonly sort = linkedSignal<number, ResultsSort>({ source: this.id, computation: () => ({ key: null, dir: 'asc' }) });
  protected readonly pageSize = signal(25);
  protected readonly pageIndex = linkedSignal({
    source: () => [this.id(), this.debouncedSearch(), this.filters(), this.sort(), this.pageSize()],
    computation: () => 0,
  });

  protected readonly violations = rxResource({
    params: () => {
      const run = this.run.value();
      // only a finished, successful run has stored records
      if (!run || run.runId !== this.id() || run.status !== 'Succeeded') return undefined;
      return {
        runId: run.runId,
        query: {
          page: this.pageIndex() + 1,
          pageSize: this.pageSize(),
          search: this.debouncedSearch().trim() || undefined,
          filters: Object.fromEntries(this.filters().map((f) => [f.key, f.value])),
          sortBy: this.sort().key ?? undefined,
          sortDir: this.sort().key ? this.sort().dir : undefined,
        },
      };
    },
    stream: ({ params }) => this.runsApi.violations(params.runId, params.query),
  });
  protected readonly page = linkedSignal<PageDto<ViolationDto> | undefined, PageDto<ViolationDto>>({
    source: () => this.violations.value(),
    computation: (value, previous) => value ?? previous?.value ?? EMPTY_PAGE,
  });
  protected readonly violationsError = computed(() => (this.violations.error() ? toApiError(this.violations.error()) : null));
  protected readonly hasQuery = computed(() => !!this.debouncedSearch().trim() || this.filters().length > 0);

  // ---- new filter being composed ----
  protected readonly filterKey = signal<string | null>(null);
  protected readonly filterColumn = computed(() => this.columns().find((c) => c.key === this.filterKey()));
  protected readonly filterValue = linkedSignal<string | null, ScalarValue>({ source: this.filterKey, computation: () => null });

  protected readonly rerunning = signal(false);
  protected readonly formatDuration = formatDuration;

  protected addFilter(): void {
    const column = this.filterColumn();
    const value = this.filterValue();
    if (!column || value === null || value === '') return;
    const filter: ActiveFilter = {
      key: column.key,
      value: String(value),
      label: `${column.displayName}: ${formatValue(value, column.dataType, column.unit, this.options().get(column.key))}`,
    };
    this.filters.update((list) => [...list.filter((f) => f.key !== column.key), filter]);
    this.filterKey.set(null);
  }

  protected removeFilter(key: string): void {
    this.filters.update((list) => list.filter((f) => f.key !== key));
  }

  protected clearQuery(): void {
    this.search.set('');
    this.filters.set([]);
  }

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  protected columnLabel(column: ColumnDto): string {
    return column.unit ? `${column.displayName} (${column.unit})` : column.displayName;
  }

  protected rerun(): void {
    const controlId = this.controlId();
    if (!controlId) return;
    this.rerunning.set(true);
    this.controlsApi.run(controlId).subscribe({
      next: (run) => {
        this.rerunning.set(false);
        this.notifier.success('הבקרה הורצה מחדש');
        this.router.navigate(['/runs', run.runId]);
      },
      error: (e: unknown) => {
        this.rerunning.set(false);
        const error = toApiError(e);
        this.notifier.error(error);
        if (error.runId) this.router.navigate(['/runs', error.runId]);
      },
    });
  }
}
