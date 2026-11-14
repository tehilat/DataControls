import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ApiError } from '../core/api/api-error';
import { describePath } from '../core/definition/definition-tree';

/** Shows a ProblemDetails error in Hebrew, including each validation error and where it is. */
@Component({
  selector: 'app-problem-alert',
  imports: [MatIconModule],
  template: `
    @if (error(); as e) {
      <div class="alert alert-error" role="alert">
        <mat-icon aria-hidden="true">error_outline</mat-icon>
        <div class="alert-body">
          <strong>{{ e.title }}</strong>
          @if (showDetail()) {
            <div>{{ e.detail }}</div>
          }
          @if (e.errors.length) {
            <ul>
              @for (item of e.errors; track $index) {
                <li>
                  @if (item.path) {
                    <span class="error-location">{{ location(item.path) }}:</span>
                  }
                  {{ item.message }}
                </li>
              }
            </ul>
          }
          @if (e.traceId && e.status >= 500) {
            <div class="muted small">מזהה בקשה: <bdi>{{ e.traceId }}</bdi></div>
          }
        </div>
      </div>
    }
  `,
})
export class ProblemAlert {
  readonly error = input<ApiError | null>(null);

  /** The detail repeats the only validation message, or just says "N errors" — the list says it better. */
  protected readonly showDetail = computed(() => {
    const e = this.error();
    return !!e?.detail && e.detail !== e.title && e.errors.length === 0;
  });

  protected location(path: string): string {
    return describePath(path);
  }
}
