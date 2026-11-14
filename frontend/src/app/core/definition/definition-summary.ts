import { EntityDto } from '../api/models';
import { LOGIC_LABELS } from '../labels';
import { formatValue } from '../format';
import { CatalogLookup } from './definition-tree';
import { EditorGroup, EditorNode, EditorOperand, EditorRule } from './definition.model';

const INCOMPLETE = '…';

/** A Hebrew sentence that describes the control, built only from business names in the catalog. */
export function summarizeControl(entity: EntityDto | undefined, root: EditorGroup, catalog: CatalogLookup): string {
  const body = describeNode(root, catalog, true);
  const subject = entity ? `רשומות מסוג "${entity.displayName}"` : 'רשומות';
  return `יוצגו כחריגה ${subject} שבהן ${body}.`;
}

export function describeNode(node: EditorNode, catalog: CatalogLookup, isRoot = false): string {
  if (node.type === 'rule') return describeRule(node, catalog);
  const parts = node.children.map((c) => describeNode(c, catalog));
  if (parts.length === 0) return INCOMPLETE;
  const text = parts.join(` ${LOGIC_LABELS[node.logic]} `);
  return isRoot || parts.length === 1 ? text : `(${text})`;
}

export function describeRule(rule: EditorRule, catalog: CatalogLookup): string {
  const field = catalog.field(rule.field);
  if (!field) return INCOMPLETE;
  const operator = catalog.operator(field.dataType, rule.operator);
  if (!operator) return `${field.fullDisplayName} ${INCOMPLETE}`;
  if (!operator.requiresValue) return `${field.fullDisplayName} ${operator.displayName}`;
  return `${field.fullDisplayName} ${joinOperator(operator.displayName, describeOperand(rule.operand, rule, catalog))}`;
}

/** "גדול מ" + "10" → "גדול מ-10"; "לפני" + "היום" → "לפני היום". */
function joinOperator(operator: string, operand: string): string {
  const lastWord = operator.trim().split(/\s+/).pop() ?? '';
  return lastWord.length === 1 ? `${operator}-${operand}` : `${operator} ${operand}`;
}

function describeOperand(operand: EditorOperand | null, rule: EditorRule, catalog: CatalogLookup): string {
  if (!operand) return INCOMPLETE;
  switch (operand.kind) {
    case 'value': {
      const field = catalog.field(rule.field);
      if (!field || operand.value === null || operand.value === '') return INCOMPLETE;
      const text = formatValue(operand.value, field.dataType, field.unit, field.options);
      return field.dataType === 'text' ? `"${text}"` : text;
    }
    case 'field':
      return catalog.field(operand.field)?.fullDisplayName ?? INCOMPLETE;
    case 'today':
      return describeToday(operand.offsetDays);
    case 'now':
      return 'עכשיו';
  }
}

export function describeToday(offsetDays: number): string {
  if (!offsetDays) return 'היום';
  const days = Math.abs(offsetDays);
  const amount = days === 1 ? 'יום אחד' : `${days} ימים`;
  return offsetDays > 0 ? `היום + ${amount}` : `היום − ${amount}`;
}
