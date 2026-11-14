// Typed contracts of the Data Controls API (backend/DataControls.Api/Contracts).
// Business terms only: there are no table or column names anywhere in these types.

import { ControlDefinition } from '../definition/definition.model';

export type DataType = 'number' | 'text' | 'date' | 'datetime' | 'boolean' | 'enum';
export type Severity = 'Low' | 'Medium' | 'High';
export type ControlSource = 'Manual' | 'AI';
export type RunStatus = 'Running' | 'Succeeded' | 'Failed';

export interface EntityDto {
  key: string;
  displayName: string;
  displayNamePlural: string;
  description: string | null;
}

export interface OptionDto {
  value: string;
  displayName: string;
}

/** A field reachable from an entity, possibly through relations. `path` is what goes into a condition. */
export interface FieldDto {
  path: string;
  fieldKey: string;
  displayName: string;
  fullDisplayName: string;
  description: string | null;
  entityKey: string;
  entityDisplayName: string;
  relationPath: string[];
  dataType: DataType;
  unit: string | null;
  options: OptionDto[];
}

export interface OperatorDto {
  key: string;
  displayName: string;
  requiresValue: boolean;
  allowFieldCompare: boolean;
}

export interface DataTypeOperatorsDto {
  dataType: DataType;
  displayName: string;
  operators: OperatorDto[];
}

export interface LastRunDto {
  runId: number;
  startedAt: string;
  status: RunStatus;
  violationCount: number | null;
}

export interface ControlSummaryDto {
  id: number;
  name: string;
  description: string | null;
  rootEntity: string;
  rootEntityDisplayName: string | null;
  severity: Severity;
  source: ControlSource;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  lastRun: LastRunDto | null;
}

export interface ControlDto extends Omit<ControlSummaryDto, 'lastRun'> {
  definition: ControlDefinition;
  rowVersion: string;
}

export interface ControlListQuery {
  search?: string;
  rootEntity?: string;
  isActive?: boolean;
  source?: ControlSource;
}

export interface SaveControlRequest {
  name: string;
  description: string | null;
  severity: Severity;
  source: ControlSource;
  isActive: boolean;
  definition: ControlDefinition;
  rowVersion?: string;
}

export interface ValidationErrorDto {
  path: string;
  message: string;
}

export interface DefinitionCheckDto {
  isValid: boolean;
  rootEntity: string | null;
  rootEntityDisplayName: string | null;
  errors: ValidationErrorDto[];
}

export interface ColumnDto {
  key: string;
  displayName: string;
  dataType: DataType;
  unit: string | null;
}

export interface RunListItemDto {
  runId: number;
  controlId: number;
  controlName: string;
  startedAt: string;
  finishedAt: string | null;
  status: RunStatus;
  violationCount: number | null;
  truncated: boolean;
  durationMs: number | null;
  errorMessage: string | null;
}

export interface RunDto extends RunListItemDto {
  storedCount: number;
  columns: ColumnDto[];
}

/** A failing record. `values` are in canonical form (numbers, yyyy-MM-dd, ISO date-time, true/false, raw list value). */
export interface ViolationDto {
  rowNumber: number;
  recordId: string | null;
  displayValue: string | null;
  values: Record<string, unknown>;
  reason: string;
}

export interface PageDto<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PreviewDto {
  rootEntity: string;
  rootEntityDisplayNamePlural: string;
  totalCount: number;
  truncated: boolean;
  durationMs: number;
  columns: ColumnDto[];
  items: ViolationDto[];
}

export interface RunListQuery {
  controlId?: number;
  status?: RunStatus;
  page?: number;
  pageSize?: number;
}

export type SortDirection = 'asc' | 'desc';

export interface ViolationQuery {
  page: number;
  pageSize: number;
  search?: string;
  /** column key → canonical value */
  filters?: Record<string, string>;
  sortBy?: string;
  sortDir?: SortDirection;
}
