import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { catchError, debounceTime, distinctUntilChanged, map, Observable, of, startWith, switchMap } from 'rxjs';
import { ApiError, toApiError } from '../../core/api/api-error';
import { ControlsApi, MetadataApi } from '../../core/api/api.services';
import { ControlDto, OptionDto, PreviewDto, SaveControlRequest, Severity, ValidationErrorDto } from '../../core/api/models';
import { ControlSeed } from '../../core/control-draft.service';
import { EditorGroup } from '../../core/definition/definition.model';
import { countRules, describePath, localIssues, newGroup, ROOT_PATH, toDefinition, toEditorTree } from '../../core/definition/definition-tree';
import { summarizeControl } from '../../core/definition/definition-summary';
import { formatDuration } from '../../core/format';
import { SEVERITIES, SEVERITY_LABELS, SOURCE_LABELS } from '../../core/labels';
import { Confirmer } from '../../shared/confirm-dialog';
import { Notifier } from '../../shared/notifier.service';
import { ProblemAlert } from '../../shared/problem-alert';
import { ResultsTable } from '../../shared/results-table';
import { ConditionContext } from './condition-builder/condition-context';
import { ConditionGroup } from './condition-builder/condition-group';

type ValidationState =
  | { state: 'idle' }
  | { state: 'incomplete'; count: number }
  | { state: 'checking' }
  | { state: 'valid' }
  | { state: 'invalid'; errors: ValidationErrorDto[] }
  | { state: 'error'; error: ApiError };

interface Details {
  name: string;
  description: string;
}

/**
 * The control editor: details, root entity and the recursive condition builder, with a live Hebrew summary,
 * live server validation (controls/validate) and a preview before saving.
 * Reusable: it accepts a ready-made definition from outside (`seed`) — e.g. an AI suggestion to review.
 */
@Component({
  selector: 'app-control-form',
  imports: [
    FormField,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ConditionGroup,
    ProblemAlert,
    ResultsTable,
  ],
  providers: [ConditionContext],
  templateUrl: './control-form.html',
  styleUrl: './control-form.scss',
})
export class ControlForm {
  private readonly metadataApi = inject(MetadataApi);
  private readonly controlsApi = inject(ControlsApi);
  private readonly notifier = inject(Notifier);
  private readonly confirmer = inject(Confirmer);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ctx = inject(ConditionContext);

  /** Initial content: an existing control, a copy, or a definition from outside (AI). Null = a new, empty control. */
  readonly seed = input<ControlSeed | null>(null);
  /** Set when editing an existing control. */
  readonly controlId = input<number | null>(null);
  readonly rowVersion = input<string | null>(null);
  readonly isActive = input(true);
  readonly saved = output<ControlDto>();
  readonly cancelled = output<void>();

  protected readonly severities = SEVERITIES;
  protected readonly severityLabels = SEVERITY_LABELS;
  protected readonly sourceLabels = SOURCE_LABELS;

  // ---- editable state, re-initialised whenever a new seed arrives ----
  protected readonly details = linkedSignal<Details>(() => ({
    name: this.seed()?.name ?? '',
    description: this.seed()?.description ?? '',
  }));
  protected readonly detailsForm = form(this.details, (p) => {
    required(p.name, { message: 'יש להזין שם לבקרה.' });
    maxLength(p.name, 200, { message: 'שם הבקרה יכול להכיל עד 200 תווים.' });
    maxLength(p.description, 1000, { message: 'התיאור יכול להכיל עד 1000 תווים.' });
  });
  protected readonly severity = linkedSignal<Severity>(() => this.seed()?.severity ?? 'Medium');
  protected readonly rootEntity = linkedSignal<string>(() => this.seed()?.definition.rootEntity ?? '');
  protected readonly tree = linkedSignal<EditorGroup>(() => {
    const seed = this.seed();
    return seed ? toEditorTree(seed.definition) : newGroup();
  });
  protected readonly source = computed(() => this.seed()?.source ?? 'Manual');
  protected readonly submitted = signal(false);

  // ---- catalog ----
  protected readonly entities = toSignal(this.metadataApi.entities(), { initialValue: [] });
  private readonly operators = toSignal(this.metadataApi.operators(), { initialValue: [] });
  protected readonly fieldsResource = rxResource({
    params: () => this.rootEntity() || undefined,
    stream: ({ params }) => this.metadataApi.fields(params),
  });
  protected readonly entity = computed(() => this.entities().find((e) => e.key.toLowerCase() === this.rootEntity().toLowerCase()));

  // ---- derived ----
  protected readonly definition = computed(() => toDefinition(this.rootEntity(), this.tree()));
  private readonly definitionJson = computed(() => JSON.stringify(this.definition()));
  /** What live validation reacts to: the definition, once the entity's fields are loaded (null = nothing to check). */
  private readonly validationKey = computed(() =>
    !this.rootEntity() || this.fieldsResource.isLoading() || !this.ctx.fields().length
      ? null
      : `${this.ctx.fields().length}#${this.definitionJson()}`,
  );
  protected readonly issues = computed(() => (this.ctx.fields().length ? localIssues(this.tree(), this.ctx) : []));
  protected readonly ruleCount = computed(() => countRules(this.tree()));
  protected readonly summary = computed(() =>
    this.rootEntity() ? summarizeControl(this.entity(), this.tree(), this.ctx) : 'יש לבחור ישות ולהגדיר תנאים.',
  );

  protected readonly validation = signal<ValidationState>({ state: 'idle' });
  protected readonly saving = signal(false);
  protected readonly saveError = signal<ApiError | null>(null);
  protected readonly nameServerErrors = computed(() => this.saveError()?.errorsAt('name').map((e) => e.message) ?? []);

  protected readonly previewing = signal(false);
  protected readonly preview = signal<PreviewDto | null>(null);
  protected readonly previewError = signal<ApiError | null>(null);
  private readonly previewedJson = signal<string | null>(null);
  private readonly previewCard = viewChild('previewCard', { read: ElementRef });
  private readonly injector = inject(Injector);
  protected readonly previewStale = computed(() => this.preview() !== null && this.previewedJson() !== this.definitionJson());
  protected readonly previewOptions = computed(
    () => new Map<string, OptionDto[]>(this.ctx.fields().filter((f) => f.relationPath.length === 0).map((f) => [f.path, f.options])),
  );
  protected readonly formatDuration = formatDuration;

  protected readonly canPreview = computed(() => this.validation().state === 'valid' && !this.previewing());

  constructor() {
    effect(() => this.ctx.fields.set(this.fieldsResource.value() ?? []));
    effect(() => this.ctx.operatorsByType.set(this.operators()));

    // inline errors: the server's validation, plus the local completeness checks once the user tried to save
    effect(() => {
      const v = this.validation();
      const server = v.state === 'invalid' ? v.errors : [];
      this.ctx.errors.set(this.submitted() ? [...this.issues(), ...server] : server);
    });

    // live validation against the server, debounced; only when the tree is complete (otherwise the local checks speak)
    toObservable(this.validationKey)
      .pipe(
        debounceTime(500),
        distinctUntilChanged(),
        switchMap((key) => (key === null ? of<ValidationState>({ state: 'idle' }) : this.check())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.validation.set(state));
  }

  private check(): Observable<ValidationState> {
    const issues = this.issues();
    if (issues.length) return of({ state: 'incomplete', count: issues.length });
    return this.controlsApi.validate(this.definition()).pipe(
      map((result): ValidationState => (result.isValid ? { state: 'valid' } : { state: 'invalid', errors: result.errors })),
      catchError((e: unknown) => of<ValidationState>({ state: 'error', error: toApiError(e) })),
      startWith<ValidationState>({ state: 'checking' }),
    );
  }

  protected location(path: string): string {
    return describePath(path);
  }

  protected changeEntity(key: string, select: MatSelect): void {
    const current = this.rootEntity();
    if (key === current) return;
    const apply = () => {
      this.rootEntity.set(key);
      this.tree.set(newGroup());
      this.preview.set(null);
      this.submitted.set(false);
    };
    const hasConditions = this.tree().children.some((c) => c.type === 'group' || c.field !== null);
    if (!hasConditions) return apply();
    this.confirmer
      .confirm({
        title: 'החלפת ישות',
        message: 'התנאים שהוגדרו מתייחסים לישות הנוכחית ויימחקו. להמשיך?',
        confirmText: 'החלפה ומחיקת התנאים',
        danger: true,
      })
      .subscribe((ok) => (ok ? apply() : (select.value = current)));
  }

  protected runPreview(): void {
    const json = this.definitionJson();
    this.previewing.set(true);
    this.previewError.set(null);
    this.scrollToPreview();
    this.controlsApi.preview(this.definition(), 50).subscribe({
      next: (result) => {
        this.preview.set(result);
        this.previewedJson.set(json);
        this.previewing.set(false);
      },
      error: (e: unknown) => {
        this.previewError.set(toApiError(e));
        this.previewing.set(false);
      },
    });
  }

  /** Brings the preview card into view once it is rendered (smooth, unless the user prefers reduced motion). */
  private scrollToPreview(): void {
    afterNextRender(
      {
        write: () => {
          const card = this.previewCard()?.nativeElement as HTMLElement | undefined;
          if (!card) return;
          const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
          card.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
        },
      },
      { injector: this.injector },
    );
  }

  protected save(): void {
    this.submitted.set(true);
    this.detailsForm().markAsTouched();
    this.saveError.set(null);
    if (this.detailsForm().invalid() || !this.rootEntity() || this.issues().length) {
      this.notifier.error(new ApiError(400, 'לא ניתן לשמור עדיין', 'יש להשלים את השדות המסומנים.'));
      return;
    }

    const details = this.details();
    const request: SaveControlRequest = {
      name: details.name.trim(),
      description: details.description.trim() || null,
      severity: this.severity(),
      source: this.source(),
      isActive: this.isActive(),
      definition: this.definition(),
    };
    const id = this.controlId();
    if (id !== null) request.rowVersion = this.rowVersion() ?? undefined;

    this.saving.set(true);
    (id === null ? this.controlsApi.create(request) : this.controlsApi.update(id, request)).subscribe({
      next: (control) => {
        this.saving.set(false);
        this.notifier.success(`הבקרה "${control.name}" נשמרה`);
        this.saved.emit(control);
      },
      error: (e: unknown) => {
        const error = toApiError(e);
        this.saving.set(false);
        this.saveError.set(error);
        // definition errors from the save are shown inline too
        const definitionErrors = error.errorsAt(ROOT_PATH);
        if (definitionErrors.length) this.validation.set({ state: 'invalid', errors: definitionErrors });
      },
    });
  }
}
