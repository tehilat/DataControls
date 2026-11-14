import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

/**
 * Placeholder for stage B: free text → control definition through the AI service.
 * The editor is already prepared: hand the suggestion to ControlDraftService and open /controls/new?draft=1.
 */
@Component({
  selector: 'app-ai-create-page',
  imports: [MatCardModule, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <div class="page-header">
      <a mat-icon-button routerLink="/controls" aria-label="חזרה לרשימת הבקרות"><mat-icon>arrow_forward</mat-icon></a>
      <h1>יצירת בקרה בעזרת AI</h1>
    </div>
    <mat-card appearance="outlined" class="placeholder">
      <mat-card-content>
        <mat-icon class="placeholder-icon" aria-hidden="true">auto_awesome</mat-icon>
        <h2>בקרוב</h2>
        <p>
          כאן אפשר יהיה לתאר את הבקרה במילים, למשל:
          <q>משלוחים שמשקל החבילה בהם גדול מכושר הנשיאה של הרחפן</q>.
          ההצעה תיפתח בעורך הבקרות לבדיקה ולאישור לפני שמירה.
        </p>
        <a mat-flat-button routerLink="/controls/new">
          <mat-icon>add</mat-icon>
          בינתיים — יצירת בקרה ידנית
        </a>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .placeholder { max-width: 640px; margin: 24px auto; text-align: center; }
    .placeholder-icon { font-size: 48px; width: 48px; height: 48px; color: var(--app-ai); margin-top: 16px; }
    p { line-height: 1.7; margin-bottom: 20px; }
  `,
})
export class AiCreatePage {}
