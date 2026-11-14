import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RunStatus, Severity } from '../core/api/models';
import { RUN_STATUS_LABELS, SEVERITY_LABELS } from '../core/labels';

@Component({
  selector: 'app-severity-chip',
  template: `<span class="chip" [class]="'chip severity-' + severity().toLowerCase()">{{ label() }}</span>`,
})
export class SeverityChip {
  readonly severity = input.required<Severity>();
  protected readonly label = computed(() => SEVERITY_LABELS[this.severity()]);
}

@Component({
  selector: 'app-run-status-chip',
  imports: [MatIconModule],
  template: `
    <span [class]="'chip status-' + status().toLowerCase()">
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>{{ label() }}
    </span>
  `,
})
export class RunStatusChip {
  readonly status = input.required<RunStatus>();
  protected readonly label = computed(() => RUN_STATUS_LABELS[this.status()]);
  protected readonly icon = computed(() =>
    this.status() === 'Succeeded' ? 'check_circle' : this.status() === 'Failed' ? 'cancel' : 'hourglass_top',
  );
}
