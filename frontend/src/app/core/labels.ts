import { ControlSource, DataType, RunStatus, Severity } from './api/models';

export const SEVERITY_LABELS: Record<Severity, string> = { Low: 'נמוכה', Medium: 'בינונית', High: 'גבוהה' };
export const SEVERITIES: Severity[] = ['High', 'Medium', 'Low'];

export const RUN_STATUS_LABELS: Record<RunStatus, string> = { Running: 'בהרצה', Succeeded: 'הושלמה', Failed: 'נכשלה' };

export const SOURCE_LABELS: Record<ControlSource, string> = { Manual: 'ידנית', AI: 'בעזרת AI' };

export const DATA_TYPE_ICONS: Record<DataType, string> = {
  number: 'tag',
  text: 'notes',
  date: 'calendar_today',
  datetime: 'schedule',
  boolean: 'toggle_on',
  enum: 'list',
};

export const LOGIC_LABELS = { AND: 'וגם', OR: 'או' } as const;
