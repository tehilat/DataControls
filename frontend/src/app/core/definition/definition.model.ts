// The control definition as stored by the API (ctl.Controls.DefinitionJson) — a JSON condition tree, never SQL.
// { version: 1, rootEntity, condition } — see docs/db-instructions.md.

export type Logic = 'AND' | 'OR';

export type ScalarValue = string | number | boolean | null;

export type Operand =
  | { kind: 'value'; value: ScalarValue }
  | { kind: 'field'; field: string }
  | { kind: 'today'; offsetDays: number }
  | { kind: 'now' };

export type OperandKind = Operand['kind'];

export interface ConditionRule {
  type: 'rule';
  field: string;
  operator: string;
  compareTo?: Operand;
}

export interface ConditionGroup {
  type: 'group';
  logic: Logic;
  children: ConditionNode[];
}

export type ConditionNode = ConditionRule | ConditionGroup;

export interface ControlDefinition {
  version: number;
  rootEntity: string;
  condition: ConditionNode;
}

// ---- Editor model: the same tree, with stable ids and room for unfinished rules. ----

export interface EditorRule {
  id: number;
  type: 'rule';
  field: string | null;
  operator: string | null;
  operand: EditorOperand | null;
}

export type EditorOperand =
  | { kind: 'value'; value: ScalarValue }
  | { kind: 'field'; field: string | null }
  | { kind: 'today'; offsetDays: number }
  | { kind: 'now' };

export interface EditorGroup {
  id: number;
  type: 'group';
  logic: Logic;
  children: EditorNode[];
}

export type EditorNode = EditorRule | EditorGroup;

/** Limits enforced by the server binder; mirrored in the UI to guide the user early. */
export const DEFINITION_LIMITS = { maxDepth: 10, maxConditions: 100, offsetDays: 36500 } as const;
