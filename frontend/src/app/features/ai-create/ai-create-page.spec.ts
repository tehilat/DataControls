import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { apiErrorInterceptor } from '../../core/api/api-error';
import { AiSuggestionDto } from '../../core/api/models';
import { ControlDraftService } from '../../core/control-draft.service';
import { AiCreatePage } from './ai-create-page';

const suggestion: AiSuggestionDto = {
  definition: {
    version: 1,
    rootEntity: 'Drone',
    condition: {
      type: 'group',
      logic: 'AND',
      children: [
        { type: 'rule', field: 'Status', operator: 'eq', compareTo: { kind: 'value', value: 'Active' } },
        { type: 'rule', field: 'Hub.IsActive', operator: 'eq', compareTo: { kind: 'value', value: false } },
      ],
    },
  },
  isValid: true,
  rootEntity: 'Drone',
  rootEntityDisplayName: 'רחפן',
  summary: 'יאותרו רחפנים שבהם סטטוס רחפן: בשירות וגם מוקד פעיל (מוקד): לא.',
  explanation: 'הבקרה תאתר רחפנים בסטטוס "בשירות" שמשויכים למוקד שאינו פעיל.',
  confidence: 0.9,
  warnings: ['"רחפן פעיל" פורש כרחפן בסטטוס "בשירות".'],
  validationErrors: [],
};

describe('AiCreatePage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AiCreatePage],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(AiCreatePage);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function buttonWithText(el: HTMLElement, text: string): HTMLButtonElement {
    const button = [...el.querySelectorAll('button')].find((b) => b.textContent?.includes(text));
    if (!button) throw new Error(`no button "${text}"`);
    return button;
  }

  it('offers the five example requests the mock recognizes', async () => {
    const { el } = await render();
    expect(el.querySelectorAll('.example-chip').length).toBe(5);
  });

  it('suggests a control and shows summary, confidence and warnings — then opens it in the editor', async () => {
    const { fixture, el } = await render();
    buttonWithText(el, 'רחפן פעיל במוקד סגור').click();

    const req = http.expectOne('/api/ai/suggest-control');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ text: 'רחפן פעיל במוקד סגור' });
    req.flush(suggestion);
    await fixture.whenStable();

    expect(el.textContent).toContain(suggestion.summary!);
    expect(el.textContent).toContain('ביטחון גבוה');
    expect(el.textContent).toContain('90%');
    expect(el.textContent).toContain(suggestion.warnings[0]);

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    buttonWithText(el, 'פתח בעורך').click();

    // nothing is saved or run: only the draft is handed to the editor
    http.expectNone('/api/controls');
    const draft = TestBed.inject(ControlDraftService).take();
    expect(draft?.source).toBe('AI');
    expect(draft?.definition).toEqual(suggestion.definition);
    expect(draft?.name).toBe('רחפן פעיל במוקד סגור');
    expect(navigate).toHaveBeenCalledWith(['/controls/new'], { queryParams: { draft: 1 } });
  });

  it('shows a clear Hebrew message when the AI service is down', async () => {
    const { fixture, el } = await render();
    buttonWithText(el, 'משלוח באיחור').click();
    http.expectOne('/api/ai/suggest-control').flush(
      { title: 'שירות ה-AI אינו זמין', detail: 'שירות ה-AI אינו זמין כרגע. ניתן להגדיר את הבקרה ידנית.', status: 503 },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();

    expect(el.textContent).toContain('שירות ה-AI אינו זמין כרגע');
    expect(el.querySelector('a[href="/controls/new"]')).not.toBeNull();
  });

  it('has no "open in editor" for a request that could not be translated', async () => {
    const { fixture, el } = await render();
    buttonWithText(el, 'משלוח באיחור').click();
    http.expectOne('/api/ai/suggest-control').flush({
      ...suggestion,
      definition: null,
      isValid: false,
      summary: null,
      confidence: 0,
      explanation: 'לא הצלחתי לתרגם את הבקשה להגדרת בקרה.',
    });
    await fixture.whenStable();

    expect(el.textContent).toContain('לא נמצאה הצעה');
    expect([...el.querySelectorAll('button')].some((b) => b.textContent?.includes('פתח בעורך'))).toBe(false);
  });
});
