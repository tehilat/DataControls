import { DatePipe } from '@angular/common';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ControlsApi, MetadataApi } from '../../core/api/api.services';
import { ControlSummaryDto } from '../../core/api/models';
import { RunStatusChip, SeverityChip } from '../../shared/chips';
import { Confirmer } from '../../shared/confirm-dialog';
import { Notifier } from '../../shared/notifier.service';
import { ProblemAlert } from '../../shared/problem-alert';

type ActiveFilter = 'all' | 'active' | 'inactive';

@Component({
  selector: 'app-controls-list-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatDividerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTableModule,
    MatTooltipModule,
    SeverityChip,
    RunStatusChip,
    ProblemAlert,
  ],
  templateUrl: './controls-list-page.html',
  styleUrl: './controls-list-page.scss',
})
export class ControlsListPage {
  private readonly controlsApi = inject(ControlsApi);
  private readonly metadataApi = inject(MetadataApi);
  private readonly notifier = inject(Notifier);
  private readonly confirmer = inject(Confirmer);
  private readonly router = inject(Router);

  protected readonly entities = toSignal(this.metadataApi.entities(), { initialValue: [] });

  protected readonly search = signal('');
  private readonly debouncedSearch = toSignal(toObservable(this.search).pipe(debounceTime(300), distinctUntilChanged()), {
    initialValue: '',
  });
  protected readonly entityFilter = signal('');
  protected readonly activeFilter = signal<ActiveFilter>('all');

  protected readonly controls = rxResource({
    params: () => ({
      search: this.debouncedSearch().trim() || undefined,
      rootEntity: this.entityFilter() || undefined,
      isActive: this.activeFilter() === 'all' ? undefined : this.activeFilter() === 'active',
    }),
    stream: ({ params }) => this.controlsApi.list(params),
  });
  /** Keeps the previous rows while a new filter loads (no flicker). */
  protected readonly rows = linkedSignal<ControlSummaryDto[] | undefined, ControlSummaryDto[]>({
    source: () => this.controls.value(),
    computation: (value, previous) => value ?? previous?.value ?? [],
  });
  protected readonly loadError = computed(() => (this.controls.error() ? toApiError(this.controls.error()) : null));
  protected readonly hasFilters = computed(() => !!this.search() || !!this.entityFilter() || this.activeFilter() !== 'all');

  /** Controls with an action in progress (run / activate / delete). */
  protected readonly busy = signal<ReadonlySet<number>>(new Set());

  protected readonly columns = ['name', 'entity', 'severity', 'lastRun', 'active', 'actions'];

  protected clearFilters(): void {
    this.search.set('');
    this.entityFilter.set('');
    this.activeFilter.set('all');
  }

  protected isBusy(id: number): boolean {
    return this.busy().has(id);
  }

  private setBusy(id: number, value: boolean): void {
    this.busy.update((set) => {
      const next = new Set(set);
      if (value) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected run(control: ControlSummaryDto): void {
    this.setBusy(control.id, true);
    this.controlsApi.run(control.id).subscribe({
      next: (run) => {
        this.setBusy(control.id, false);
        this.router.navigate(['/runs', run.runId]);
      },
      error: (e: unknown) => {
        this.setBusy(control.id, false);
        const error = toApiError(e);
        this.notifier.error(error, `הרצת "${control.name}"`);
        this.controls.reload();
        // a failed run is still recorded — show it
        if (error.runId) this.router.navigate(['/runs', error.runId]);
      },
    });
  }

  protected toggleActive(control: ControlSummaryDto): void {
    const activate = !control.isActive;
    const confirm$ = activate
      ? this.confirmer.confirm({ title: 'הפעלת בקרה', message: `להפעיל את "${control.name}"?`, confirmText: 'הפעלה' })
      : this.confirmer.confirm({
          title: 'השבתת בקרה',
          message: `"${control.name}" לא תרוץ עד שתופעל מחדש. היסטוריית ההרצות נשמרת.`,
          confirmText: 'השבתה',
          danger: true,
        });
    confirm$
      .pipe(
        filter(Boolean),
        switchMap(() => {
          this.setBusy(control.id, true);
          return this.controlsApi.setActive(control.id, activate);
        }),
      )
      .subscribe({
        next: () => {
          this.setBusy(control.id, false);
          this.notifier.success(activate ? 'הבקרה הופעלה' : 'הבקרה הושבתה');
          this.controls.reload();
        },
        error: (e: unknown) => {
          this.setBusy(control.id, false);
          this.notifier.error(e);
        },
      });
  }

  protected remove(control: ControlSummaryDto): void {
    this.confirmer
      .confirm({ title: 'מחיקת בקרה', message: `למחוק את "${control.name}" לצמיתות?`, confirmText: 'מחיקה', danger: true })
      .pipe(
        filter(Boolean),
        switchMap(() => {
          this.setBusy(control.id, true);
          return this.controlsApi.delete(control.id);
        }),
      )
      .subscribe({
        next: () => {
          this.setBusy(control.id, false);
          this.notifier.success('הבקרה נמחקה');
          this.controls.reload();
        },
        error: (e: unknown) => {
          this.setBusy(control.id, false);
          this.notifier.error(e);
        },
      });
  }
}
