import { inject, Service } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { toApiError } from '../core/api/api-error';

@Service()
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, 'סגירה', { duration: 4000, direction: 'rtl', panelClass: 'snack-success' });
  }

  error(error: unknown, prefix?: string): void {
    const apiError = toApiError(error);
    const text = apiError.detail && apiError.detail !== apiError.title ? `${apiError.title}: ${apiError.detail}` : apiError.title;
    this.snackBar.open(prefix ? `${prefix} — ${text}` : text, 'סגירה', {
      duration: 8000,
      direction: 'rtl',
      panelClass: 'snack-error',
      politeness: 'assertive',
    });
  }
}
