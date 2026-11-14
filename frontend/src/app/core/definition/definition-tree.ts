import { DataType, FieldDto, OperatorDto, ValidationErrorDto } from '../api/models';
import {
  ConditionNode,
  ControlDefinition,
  DEFINITION_LIMITS,
  EditorGroup,
  EditorNode,
  EditorOperand,
  EditorRule,
  Logic,
  Operand,
} from './definition.model';

let nextId = 1;
const newId = () => nextId++;

export function newRule(): EditorRule {
  return { id: newId(), type: 'rule', field: null, operator: null, operand: null };
}

export function newGroup(logic: Logic = 'AND', children: EditorNode[] = [newRule()]): EditorGroup {
  return { id: newId(), type: 'group', logic, children };
}

/** Builds the editor tree from a stored definition. A root rule is wrapped in an AND group (same meaning). */
export function toEditorTree(definition: ControlDefinition): EditorGroup {
  const root = toEditorNode(definition.condition);
  return root.type === 'group' ? root : newGroup('AND', [root]);
}

function toEditorNode(node: ConditionNode): EditorNode {
  if (node.type === 'group') {
    return { id: newId(), type: 'group', logic: node.logic === 'OR' ? 'OR' : 'AND', children: (node.children ?? []).map(toEditorNode) };
  }
  return { id: newId(), type: 'rule', field: node.field ?? null, operator: node.operator ?? null, operand: toEditorOperand(node.compareTo) };
}

function toEditorOperand(operand: Operand | undefined): EditorOperand | null {
  if (!operand) return null;
  switch (operand.kind) {
    case 'value':
      return { kind: 'value', value: operand.value ?? null };
    case 'field':
      return { kind: 'field', field: operand.field ?? null };
    case 'today':
      return { kind: 'today', offsetDays: operand.offsetDays ?? 0 };
    case 'now':
      return { kind: 'now' };
  }
}

/** The definition JSON in the API schema. Unfinished parts are sent as-is (empty strings) — see `localIssues`. */
export function toDefinition(rootEntity: string, root: EditorGroup): ControlDefinition {
  return { version: 1, rootEntity, condition: toConditionNode(root) };
}

function toConditionNode(node: EditorNode): ConditionNode {
  if (node.type === 'group') return { type: 'group', logic: node.logic, children: node.children.map(toConditionNode) };
  const rule: ConditionNode = { type: 'rule', field: node.field ?? '', operator: node.operator ?? '' };
  if (node.operand) rule.compareTo = toOperand(node.operand);
  return rule;
}

function toOperand(operand: EditorOperand): Operand {
  return operand.kind === 'field' ? { kind: 'field', field: operand.field ?? '' } : operand;
}

// ---- Paths: the same JSON paths the server uses in its errors ("condition.children[1].compareTo.value"). ----

export const ROOT_PATH = 'condition';

export function childPath(parentPath: string, index: number): string {
  return `${parentPath}.children[${index}]`;
}

/** "condition.children[1].children[0].field" → "תנאי 2 › 1" (a location a business user can find on screen). */
export function describePath(path: string): string {
  if (!path.startsWith(ROOT_PATH)) {
    return path === '' ? 'הגדרה כללית' : path === 'rootEntity' ? 'ישות' : path === 'name' ? 'שם הבקרה' : path === 'description' ? 'תיאור' : path;
  }
  const indexes = [...path.matchAll(/children\[(\d+)]/g)].map((m) => Number(m[1]) + 1);
  return indexes.length === 0 ? 'התנאי הראשי' : `תנאי ${indexes.join(' › ')}`;
}

// ---- Immutable updates by node id ----

export function replaceNode(root: EditorGroup, id: number, replacement: EditorNode | null): EditorGroup {
  return replaceIn(root, id, replacement) as EditorGroup;
}

function replaceIn(node: EditorNode, id: number, replacement: EditorNode | null): EditorNode | null {
  if (node.id === id) return replacement;
  if (node.type === 'rule') return node;
  let changed = false;
  const children: EditorNode[] = [];
  for (const child of node.children) {
    const next = replaceIn(child, id, replacement);
    if (next !== child) changed = true;
    if (next) children.push(next);
  }
  return changed ? { ...node, children } : node;
}

export function countRules(node: EditorNode): number {
  return node.type === 'rule' ? 1 : node.children.reduce((sum, c) => sum + countRules(c), 0);
}

// ---- Client-side completeness checks (the server remains the authority on validity) ----

export function isTemporal(type: DataType | undefined): boolean {
  return type === 'date' || type === 'datetime';
}

/** Whether `other` may be compared to `field` (same type; date ↔ datetime is allowed — as in the server binder). */
export function isComparableField(field: FieldDto, other: FieldDto): boolean {
  if (other.path === field.path) return false;
  return other.dataType === field.dataType || (isTemporal(field.dataType) && isTemporal(other.dataType));
}

export interface CatalogLookup {
  field(path: string | null): FieldDto | undefined;
  operator(type: DataType, key: string | null): OperatorDto | undefined;
}

export function localIssues(root: EditorGroup, catalog: CatalogLookup): ValidationErrorDto[] {
  const issues: ValidationErrorDto[] = [];
  const total = countRules(root);
  if (total > DEFINITION_LIMITS.maxConditions) {
    issues.push({ path: ROOT_PATH, message: `ניתן להגדיר עד ${DEFINITION_LIMITS.maxConditions} תנאים (כרגע ${total}).` });
  }
  visit(root, ROOT_PATH, 1, catalog, issues);
  return issues;
}

function visit(node: EditorNode, path: string, depth: number, catalog: CatalogLookup, issues: ValidationErrorDto[]): void {
  if (node.type === 'group') {
    if (node.children.length === 0) issues.push({ path, message: 'הקבוצה ריקה. יש להוסיף לה תנאי או למחוק אותה.' });
    if (depth > DEFINITION_LIMITS.maxDepth) issues.push({ path, message: `ניתן לקנן קבוצות עד ${DEFINITION_LIMITS.maxDepth} רמות.` });
    node.children.forEach((child, i) => visit(child, childPath(path, i), depth + 1, catalog, issues));
    return;
  }

  const field = catalog.field(node.field);
  if (!field) {
    issues.push({ path: `${path}.field`, message: 'יש לבחור שדה.' });
    return;
  }
  const operator = catalog.operator(field.dataType, node.operator);
  if (!operator) {
    issues.push({ path: `${path}.operator`, message: `יש לבחור תנאי עבור "${field.displayName}".` });
    return;
  }
  if (!operator.requiresValue) return;

  const operand = node.operand;
  if (!operand) {
    issues.push({ path: `${path}.compareTo`, message: `יש להזין ערך להשוואה עבור "${field.displayName}".` });
  } else if (operand.kind === 'value' && (operand.value === null || operand.value === '')) {
    issues.push({ path: `${path}.compareTo.value`, message: `יש להזין ערך עבור "${field.displayName}".` });
  } else if (operand.kind === 'field' && !catalog.field(operand.field)) {
    issues.push({ path: `${path}.compareTo.field`, message: 'יש לבחור את השדה להשוואה.' });
  } else if (operand.kind === 'today' && Math.abs(operand.offsetDays) > DEFINITION_LIMITS.offsetDays) {
    issues.push({ path: `${path}.compareTo.offsetDays`, message: `מספר הימים חייב להיות עד ${DEFINITION_LIMITS.offsetDays}.` });
  }
}

/** Messages for one node: its own path and anything below it, but not inside child nodes of a group. */
export function messagesFor(errors: readonly ValidationErrorDto[], path: string, isGroup: boolean): string[] {
  return errors
    .filter((e) => {
      if (e.path === path) return true;
      if (!e.path.startsWith(path + '.')) return false;
      return !isGroup || !e.path.startsWith(path + '.children[');
    })
    .map((e) => e.message);
}
