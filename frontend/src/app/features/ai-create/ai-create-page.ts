import { JsonPipe, PercentPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { ApiError, toApiError } from '../../core/api/api-error';
import { AiApi } from '../../core/api/api.services';
import { AiPromptPreviewDto, AiSuggestionDto } from '../../core/api/models';
import { ControlDraftService } from '../../core/control-draft.service';
import { describePath } from '../../core/definition/definition-tree';
import { ProblemAlert } from '../../shared/problem-alert';

/** The requests the demo (Mock) provider recognizes — the same as docs/requests/1–5. */
const EXAMPLES = [
  'משלוחים שמשקל החבילה בהם מעל כושר הנשיאה של הרחפן',
  'רחפן פעיל במוקד סגור',
  'לקוח בלי טלפון ובלי אימייל',
  'משלוח באיחור',
  'מפעיל עם רישיון שפג',
];

const MAX_TEXT = 2000;

/**
 * Stage B: free text → a proposed control. The proposal is only shown here; "פתח בעורך" hands it to the regular
 * editor (source AI) where the user reviews, previews and saves it. Nothing is saved or run on this screen.
 */
@Component({
  selector: 'app-ai-create-page',
  imports: [
    JsonPipe,
    PercentPipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    ProblemAlert,
  ],
  templateUrl: './ai-create-page.html',
  styleUrl: './ai-create-page.scss',
})
export class AiCreatePage {
  private readonly aiApi = inject(AiApi);
  private readonly drafts = inject(ControlDraftService);
  private readonly router = inject(Router);

  protected readonly examples = EXAMPLES;
  protected readonly maxText = MAX_TEXT;

  protected readonly text = signal('');
  protected readonly canSuggest = computed(() => {
    const t = this.text().trim();
    return t.length > 0 && t.length <= MAX_TEXT && !this.loading();
  });

  protected readonly loading = signal(false);
  protected readonly suggestion = signal<AiSuggestionDto | null>(null);
  /** The text the current suggestion was made for (the prompt preview uses it too). */
  protected readonly suggestedText = signal('');
  protected readonly error = signal<ApiError | null>(null);
  protected readonly unavailable = computed(() => this.error()?.status === 503 || this.error()?.status === 0);

  protected readonly confidenceLevel = computed(() => {
    const c = this.suggestion()?.confidence ?? 0;
    return c >= 0.8 ? 'high' : c >= 0.5 ? 'medium' : 'low';
  });
  protected readonly confidenceLabel = computed(
    () => ({ high: 'ביטחון גבוה', medium: 'ביטחון בינוני — מומלץ לבדוק', low: 'ביטחון נמוך' })[this.confidenceLevel()],
  );

  // ---- "מה נשלח ל-AI" (loaded when the panel is opened) ----
  protected readonly prompt = signal<AiPromptPreviewDto | null>(null);
  protected readonly promptFor = signal<string | null>(null);
  protected readonly promptLoading = signal(false);
  protected readonly promptError = signal<ApiError | null>(null);

  protected useExample(example: string): void {
    this.text.set(example);
    this.suggest();
  }

  protected suggest(): void {
    const text = this.text().trim();
    if (!text || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);
    this.suggestion.set(null);
    this.prompt.set(null);
    this.promptFor.set(null);
    this.aiApi.suggestControl(text).subscribe({
      next: (result) => {
        this.suggestion.set(result);
        this.suggestedText.set(text);
        this.loading.set(false);
      },
      error: (e: unknown) => {
        this.error.set(toApiError(e));
        this.loading.set(false);
      },
    });
  }

  protected loadPrompt(): void {
    const text = this.suggestedText() || this.text().trim();
    if (!text || this.promptFor() === text || this.promptLoading()) return;
    this.promptLoading.set(true);
    this.promptError.set(null);
    this.aiApi.promptPreview(text).subscribe({
      next: (p) => {
        this.prompt.set(p);
        this.promptFor.set(text);
        this.promptLoading.set(false);
      },
      error: (e: unknown) => {
        this.promptError.set(toApiError(e));
        this.promptLoading.set(false);
      },
    });
  }

  /** Opens the proposal in the regular editor for review. Saving / running happen only there, by the user. */
  protected openInEditor(): void {
    const s = this.suggestion();
    if (!s?.definition) return;
    const name = this.suggestedText().length > 200 ? this.suggestedText().slice(0, 197) + '…' : this.suggestedText();
    this.drafts.set({
      name,
      description: s.explanation || null,
      source: 'AI',
      definition: s.definition,
    });
    this.router.navigate(['/controls/new'], { queryParams: { draft: 1 } });
  }

  protected location(path: string): string {
    return describePath(path);
  }
}
