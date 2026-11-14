import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PreviewDto } from '../../core/api/models';
import { ControlSeed } from '../../core/control-draft.service';
import { newRule } from '../../core/definition/definition-tree';
import { EditorGroup } from '../../core/definition/definition.model';
import { ControlForm } from './control-form';

const seed: ControlSeed = {
  name: 'משקל חבילה שלילי',
  definition: {
    version: 1,
    rootEntity: 'Delivery',
    condition: {
      type: 'rule',
      field: 'PackageWeightKg',
      operator: 'lt',
      compareTo: { kind: 'value', value: 0 },
    },
  },
};

const preview: PreviewDto = {
  rootEntity: 'Delivery',
  rootEntityDisplayNamePlural: 'משלוחים',
  totalCount: 1,
  truncated: false,
  durationMs: 5,
  columns: [{ key: 'PackageWeightKg', displayName: 'משקל חבילה', dataType: 'number', unit: 'ק"ג' }],
  items: [
    {
      rowNumber: 1,
      recordId: '8',
      displayValue: '8',
      values: { PackageWeightKg: -2 },
      reason: 'משקל חבילה (-2 ק"ג) קטן מ-0 ק"ג',
    },
  ],
};

describe('ControlForm preview', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ControlForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  // the form validates live (debounced); only the preview request matters here
  afterEach(() => http.match(() => true).forEach((r) => r.flush({ isValid: true, errors: [] })));

  /** The catalog the form loads: entities, operators and the entity's fields (business terms only). */
  function answerCatalog(): void {
    http
      .expectOne('/api/metadata/entities')
      .flush([
        { key: 'Delivery', displayName: 'משלוח', displayNamePlural: 'משלוחים', description: null },
      ]);
    http.expectOne('/api/metadata/operators').flush([
      {
        dataType: 'number',
        displayName: 'מספר',
        operators: [
          { key: 'lt', displayName: 'קטן מ', requiresValue: true, allowFieldCompare: true },
        ],
      },
    ]);
    http
      .expectOne((r) => r.url === '/api/metadata/entities/Delivery/fields')
      .flush([
        {
          path: 'PackageWeightKg',
          fieldKey: 'PackageWeightKg',
          displayName: 'משקל חבילה',
          fullDisplayName: 'משקל חבילה',
          description: null,
          entityKey: 'Delivery',
          entityDisplayName: 'משלוח',
          relationPath: [],
          dataType: 'number',
          unit: 'ק"ג',
          options: [],
        },
      ]);
  }

  it('scrolls to the results and marks them when the definition changes afterwards', async () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;

    const fixture = TestBed.createComponent(ControlForm);
    fixture.componentRef.setInput('seed', seed);
    fixture.detectChanges();
    answerCatalog();
    await fixture.whenStable();
    const form = fixture.componentInstance as unknown as {
      runPreview(): void;
      tree: { set(g: EditorGroup): void; (): EditorGroup };
    };
    const el = fixture.nativeElement as HTMLElement;

    form.runPreview();
    await fixture.whenStable();
    expect(scroll).toHaveBeenCalledWith(expect.objectContaining({ block: 'start' }));

    http.expectOne('/api/controls/preview').flush(preview);
    await fixture.whenStable();
    expect(el.textContent).toContain('משקל חבילה (-2 ק"ג) קטן מ-0 ק"ג');
    expect(el.textContent).not.toContain('ההגדרה השתנתה - הרץ שוב תצוגה מקדימה');

    // the user changes a condition after the preview
    form.tree.set({ ...form.tree(), children: [...form.tree().children, newRule()] });
    await fixture.whenStable();
    expect(el.textContent).toContain('ההגדרה השתנתה - הרץ שוב תצוגה מקדימה');
    expect(el.querySelector('.preview-results.stale')).not.toBeNull();
  });
});
