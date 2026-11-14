import { Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ControlsApi } from '../../core/api/api.services';
import { ControlDraftService, ControlSeed } from '../../core/control-draft.service';
import { ProblemAlert } from '../../shared/problem-alert';
import { ControlForm } from './control-form';

/**
 * Routes: /controls/new, /controls/new?copyFrom=7 (duplicate), /controls/new?draft=1 (a definition handed over
 * through ControlDraftService, e.g. from the AI screen) and /controls/7/edit.
 */
@Component({
  selector: 'app-control-editor-page',
  imports: [ControlForm, ProblemAlert, MatProgressBarModule, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <div class="page-header">
      <a mat-icon-button routerLink="/controls" aria-label="חזרה לרשימת הבקרות"><mat-icon>arrow_forward</mat-icon></a>
      <h1>{{ title() }}</h1>
    </div>

    @if (control.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="טוען את הבקרה" />
    } @else if (control.error()) {
      <app-problem-alert [error]="loadError()" />
    } @else {
      <app-control-form
        [seed]="seed()"
        [controlId]="editId()"
        [rowVersion]="control.value()?.rowVersion ?? null"
        [isActive]="isNew() ? true : (control.value()?.isActive ?? true)"
        (saved)="router.navigate(['/controls'])"
        (cancelled)="router.navigate(['/controls'])"
      />
    }
  `,
})
export class ControlEditorPage {
  private readonly controlsApi = inject(ControlsApi);
  private readonly drafts = inject(ControlDraftService);
  protected readonly router = inject(Router);

  /** Route parameter (edit). */
  readonly id = input<string>();
  /** Query parameter: duplicate an existing control. */
  readonly copyFrom = input<string>();
  /** Query parameter: take the definition handed over by another screen. */
  readonly draft = input<string>();

  protected readonly editId = computed(() => (this.id() ? Number(this.id()) : null));
  protected readonly isNew = computed(() => this.editId() === null);
  private readonly sourceId = computed(() => this.editId() ?? (this.copyFrom() ? Number(this.copyFrom()) : null));

  // read once on creation: the draft service hands the definition over a single time
  private readonly draftSeed: ControlSeed | null = this.drafts.take();

  protected readonly control = rxResource({
    params: () => this.sourceId() ?? undefined,
    stream: ({ params }) => (params ? this.controlsApi.get(params) : of(undefined)),
  });

  protected readonly loadError = computed(() => (this.control.error() ? toApiError(this.control.error()) : null));

  protected readonly seed = computed<ControlSeed | null>(() => {
    const c = this.control.value();
    if (c) {
      const copy = this.isNew();
      return {
        name: copy ? `${c.name} (עותק)` : c.name,
        description: c.description,
        severity: c.severity,
        source: copy ? 'Manual' : c.source,
        definition: c.definition,
      };
    }
    return this.draft() ? this.draftSeed : null;
  });

  protected readonly title = computed(() => {
    if (!this.isNew()) return 'עריכת בקרה';
    if (this.copyFrom()) return 'שכפול בקרה';
    return this.seed()?.source === 'AI' ? 'בדיקת בקרה שהוצעה' : 'בקרה חדשה';
  });
}
