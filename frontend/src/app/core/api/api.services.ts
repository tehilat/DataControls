import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { ControlDefinition } from '../definition/definition.model';
import {
  AiPromptPreviewDto,
  AiSuggestionDto,
  ControlDto,
  ControlListQuery,
  ControlSummaryDto,
  DataTypeOperatorsDto,
  DefinitionCheckDto,
  EntityDto,
  FieldDto,
  PageDto,
  PreviewDto,
  RunDto,
  RunListItemDto,
  RunListQuery,
  SaveControlRequest,
  ViolationDto,
  ViolationQuery,
} from './models';

/** All calls are relative to /api — the dev server proxies them (proxy.conf.json), so no CORS is involved. */
const API = '/api';

function toParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
  }
  return params;
}

/** The business catalog. Results are cached for the session (the catalog changes rarely). */
@Service()
export class MetadataApi {
  private readonly http = inject(HttpClient);
  private entities$?: Observable<EntityDto[]>;
  private operators$?: Observable<DataTypeOperatorsDto[]>;
  private readonly fields$ = new Map<string, Observable<FieldDto[]>>();

  entities(): Observable<EntityDto[]> {
    return (this.entities$ ??= this.http.get<EntityDto[]>(`${API}/metadata/entities`).pipe(shareReplay(1)));
  }

  operators(): Observable<DataTypeOperatorsDto[]> {
    return (this.operators$ ??= this.http.get<DataTypeOperatorsDto[]>(`${API}/metadata/operators`).pipe(shareReplay(1)));
  }

  /** Fields of an entity and of related entities (depth = relations to follow). */
  fields(entityKey: string, depth = 2): Observable<FieldDto[]> {
    const cacheKey = `${entityKey}|${depth}`;
    let fields = this.fields$.get(cacheKey);
    if (!fields) {
      fields = this.http
        .get<FieldDto[]>(`${API}/metadata/entities/${encodeURIComponent(entityKey)}/fields`, { params: toParams({ depth }) })
        .pipe(shareReplay(1));
      this.fields$.set(cacheKey, fields);
    }
    return fields;
  }
}

@Service()
export class ControlsApi {
  private readonly http = inject(HttpClient);

  list(query: ControlListQuery = {}): Observable<ControlSummaryDto[]> {
    return this.http.get<ControlSummaryDto[]>(`${API}/controls`, { params: toParams(query) });
  }

  get(id: number): Observable<ControlDto> {
    return this.http.get<ControlDto>(`${API}/controls/${id}`);
  }

  create(request: SaveControlRequest): Observable<ControlDto> {
    return this.http.post<ControlDto>(`${API}/controls`, request);
  }

  update(id: number, request: SaveControlRequest): Observable<ControlDto> {
    return this.http.put<ControlDto>(`${API}/controls/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${API}/controls/${id}`);
  }

  setActive(id: number, active: boolean): Observable<ControlDto> {
    return this.http.post<ControlDto>(`${API}/controls/${id}/${active ? 'activate' : 'deactivate'}`, null);
  }

  validate(definition: ControlDefinition): Observable<DefinitionCheckDto> {
    return this.http.post<DefinitionCheckDto>(`${API}/controls/validate`, { definition });
  }

  preview(definition: ControlDefinition, maxRows = 50): Observable<PreviewDto> {
    return this.http.post<PreviewDto>(`${API}/controls/preview`, { definition, maxRows });
  }

  run(id: number): Observable<RunDto> {
    return this.http.post<RunDto>(`${API}/controls/${id}/runs`, null);
  }
}

/** Stage B: free text → proposed control. The AI service is optional; when it is down these calls fail with 503. */
@Service()
export class AiApi {
  private readonly http = inject(HttpClient);

  suggestControl(text: string): Observable<AiSuggestionDto> {
    return this.http.post<AiSuggestionDto>(`${API}/ai/suggest-control`, { text });
  }

  promptPreview(text: string): Observable<AiPromptPreviewDto> {
    return this.http.get<AiPromptPreviewDto>(`${API}/ai/prompt-preview`, { params: toParams({ text }) });
  }
}

@Service()
export class RunsApi {
  private readonly http = inject(HttpClient);

  list(query: RunListQuery = {}): Observable<PageDto<RunListItemDto>> {
    return this.http.get<PageDto<RunListItemDto>>(`${API}/runs`, { params: toParams(query) });
  }

  get(runId: number): Observable<RunDto> {
    return this.http.get<RunDto>(`${API}/runs/${runId}`);
  }

  violations(runId: number, query: ViolationQuery): Observable<PageDto<ViolationDto>> {
    const { filters, ...rest } = query;
    let params = toParams(rest);
    for (const [key, value] of Object.entries(filters ?? {})) params = params.set(`filter[${key}]`, value);
    return this.http.get<PageDto<ViolationDto>>(`${API}/runs/${runId}/violations`, { params });
  }
}
