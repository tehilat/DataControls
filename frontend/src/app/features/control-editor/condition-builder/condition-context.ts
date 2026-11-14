import { computed, Injectable, signal } from '@angular/core';
import { DataType, DataTypeOperatorsDto, FieldDto, OperatorDto, ValidationErrorDto } from '../../../core/api/models';
import { CatalogLookup, messagesFor } from '../../../core/definition/definition-tree';

/**
 * Shared state of one condition builder: the catalog for the chosen entity and the errors to show inline.
 * Provided by the control form, so nested groups and rules read it without passing inputs down every level.
 */
@Injectable()
export class ConditionContext implements CatalogLookup {
  readonly fields = signal<FieldDto[]>([]);
  readonly operatorsByType = signal<DataTypeOperatorsDto[]>([]);
  /** Errors from the server (validate / save) and, after a save attempt, the local completeness checks. */
  readonly errors = signal<ValidationErrorDto[]>([]);

  private readonly fieldMap = computed(() => new Map(this.fields().map((f) => [f.path.toLowerCase(), f])));
  private readonly operatorMap = computed(() => new Map(this.operatorsByType().map((t) => [t.dataType, t.operators])));

  field(path: string | null | undefined): FieldDto | undefined {
    return path ? this.fieldMap().get(path.toLowerCase()) : undefined;
  }

  operatorsFor(type: DataType | undefined): OperatorDto[] {
    return type ? (this.operatorMap().get(type) ?? []) : [];
  }

  operator(type: DataType, key: string | null): OperatorDto | undefined {
    return key ? this.operatorsFor(type).find((o) => o.key.toLowerCase() === key.toLowerCase()) : undefined;
  }

  messagesAt(path: string, isGroup: boolean): string[] {
    return messagesFor(this.errors(), path, isGroup);
  }
}
