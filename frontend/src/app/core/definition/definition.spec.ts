import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '../api/api-error';
import { DataType, FieldDto, OperatorDto } from '../api/models';
import { summarizeControl } from './definition-summary';
import { CatalogLookup, describePath, localIssues, messagesFor, newRule, replaceNode, toDefinition, toEditorTree } from './definition-tree';
import { ControlDefinition, EditorRule } from './definition.model';

const field = (path: string, dataType: DataType, displayName: string, extra: Partial<FieldDto> = {}): FieldDto => ({
  path,
  fieldKey: path.split('.').pop()!,
  displayName,
  fullDisplayName: displayName,
  description: null,
  entityKey: 'E',
  entityDisplayName: 'משלוח',
  relationPath: [],
  dataType,
  unit: null,
  options: [],
  ...extra,
});

const fields = [
  field('PackageWeightKg', 'number', 'משקל חבילה', { unit: 'ק"ג' }),
  field('Drone.MaxPayloadKg', 'number', 'כושר נשיאה', { fullDisplayName: 'כושר נשיאה (רחפן)' }),
  field('Status', 'enum', 'סטטוס משלוח', { options: [{ value: 'Delivered', displayName: 'נמסר' }] }),
  field('DeliveredAt', 'datetime', 'זמן מסירה'),
];
const ops: OperatorDto[] = [
  { key: 'eq', displayName: 'הוא', requiresValue: true, allowFieldCompare: false },
  { key: 'gt', displayName: 'גדול מ', requiresValue: true, allowFieldCompare: true },
  { key: 'isEmpty', displayName: 'ריק', requiresValue: false, allowFieldCompare: false },
];
const catalog: CatalogLookup = {
  field: (p) => fields.find((f) => f.path === p),
  operator: (_t, k) => ops.find((o) => o.key === k),
};

const definition: ControlDefinition = {
  version: 1,
  rootEntity: 'Delivery',
  condition: {
    type: 'group',
    logic: 'OR',
    children: [
      { type: 'rule', field: 'PackageWeightKg', operator: 'gt', compareTo: { kind: 'field', field: 'Drone.MaxPayloadKg' } },
      {
        type: 'group',
        logic: 'AND',
        children: [
          { type: 'rule', field: 'Status', operator: 'eq', compareTo: { kind: 'value', value: 'Delivered' } },
          { type: 'rule', field: 'DeliveredAt', operator: 'isEmpty' },
        ],
      },
    ],
  },
};

describe('definition tree', () => {
  it('round-trips a nested definition', () => {
    expect(toDefinition('Delivery', toEditorTree(definition))).toEqual(definition);
  });

  it('wraps a root rule in an AND group', () => {
    const tree = toEditorTree({ version: 1, rootEntity: 'X', condition: { type: 'rule', field: 'Status', operator: 'isEmpty' } });
    expect(tree.type).toBe('group');
    expect(tree.logic).toBe('AND');
    expect(tree.children.length).toBe(1);
  });

  it('replaces and removes nodes immutably', () => {
    const tree = toEditorTree(definition);
    const target = tree.children[0] as EditorRule;
    const replaced = replaceNode(tree, target.id, { ...target, operator: 'eq' });
    expect((replaced.children[0] as EditorRule).operator).toBe('eq');
    expect((tree.children[0] as EditorRule).operator).toBe('gt');
    expect(replaceNode(tree, target.id, null).children.length).toBe(1);
  });

  it('reports unfinished rules with server-style paths', () => {
    const tree = toEditorTree(definition);
    const withEmpty = { ...tree, children: [...tree.children, newRule()] };
    expect(localIssues(tree, catalog)).toEqual([]);
    expect(localIssues(withEmpty, catalog).map((i) => i.path)).toEqual(['condition.children[2].field']);
  });

  it('maps error paths to rules and locations', () => {
    const errors = [
      { path: 'condition.children[1].children[0].compareTo.value', message: 'x' },
      { path: 'condition.children[1].logic', message: 'y' },
    ];
    expect(messagesFor(errors, 'condition.children[1]', true)).toEqual(['y']);
    expect(messagesFor(errors, 'condition.children[1].children[0]', false)).toEqual(['x']);
    expect(describePath(errors[0].path)).toBe('תנאי 2 › 1');
  });

  it('builds a Hebrew summary from business names only', () => {
    const text = summarizeControl(
      { key: 'Delivery', displayName: 'משלוח', displayNamePlural: 'משלוחים', description: null },
      toEditorTree(definition),
      catalog,
    );
    expect(text).toBe('יוצגו כחריגה רשומות מסוג "משלוח" שבהן משקל חבילה גדול מ-כושר נשיאה (רחפן) או (סטטוס משלוח הוא נמסר וגם זמן מסירה ריק).');
  });
});

describe('toApiError', () => {
  it('reads Hebrew ProblemDetails with validation errors', () => {
    const error = toApiError(
      new HttpErrorResponse({
        status: 400,
        error: {
          title: 'הגדרת הבקרה אינה תקינה',
          detail: 'נמצאו 2 שגיאות.',
          validationErrors: [
            { path: 'name', message: 'חסר שם' },
            { path: 'condition.children[0].field', message: 'שדה לא קיים' },
          ],
        },
      }),
    );
    expect(error.title).toBe('הגדרת הבקרה אינה תקינה');
    expect(error.errorsAt('condition').length).toBe(1);
    expect(error.errorsAt('name')[0].message).toBe('חסר שם');
  });

  it('explains a connection failure', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 })).title).toBe('לא ניתן להתחבר לשרת');
  });
});
