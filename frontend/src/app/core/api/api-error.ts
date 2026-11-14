import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ValidationErrorDto } from './models';

/**
 * A failed API call in a single shape, built from the server's Hebrew ProblemDetails
 * (title, detail, validationErrors[{path,message}], runId, path, traceId).
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    readonly errors: readonly ValidationErrorDto[] = [],
    readonly runId: number | null = null,
    readonly conflictPath: string | null = null,
    readonly traceId: string | null = null,
  ) {
    super(detail ?? title);
  }

  /** Errors whose path starts with the given prefix (e.g. "condition" or "name"). */
  errorsAt(prefix: string): ValidationErrorDto[] {
    return this.errors.filter((e) => e.path === prefix || e.path.startsWith(prefix + '.') || e.path.startsWith(prefix + '['));
  }
}

const FALLBACK_TITLES: Record<number, string> = {
  0: 'לא ניתן להתחבר לשרת',
  400: 'הבקשה אינה תקינה',
  404: 'הפריט המבוקש לא נמצא',
  409: 'הפעולה מתנגשת במצב הנוכחי',
  422: 'הרצת הבקרה נכשלה',
  500: 'שגיאת שרת',
  503: 'השירות אינו זמין כרגע',
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

function readValidationErrors(body: Record<string, unknown>): ValidationErrorDto[] {
  const list = body['validationErrors'];
  if (Array.isArray(list)) {
    return list.filter(isRecord).map((e) => ({ path: asString(e['path']) ?? '', message: asString(e['message']) ?? '' }));
  }
  // the standard ValidationProblemDetails shape: { errors: { path: [messages] } }
  const dict = body['errors'];
  if (isRecord(dict)) {
    return Object.entries(dict).flatMap(([path, messages]) =>
      Array.isArray(messages) ? messages.map((m) => ({ path, message: String(m) })) : [],
    );
  }
  return [];
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (!(error instanceof HttpErrorResponse)) {
    return new ApiError(-1, 'אירעה שגיאה בלתי צפויה', error instanceof Error ? null : String(error));
  }
  if (error.status === 0) {
    return new ApiError(0, FALLBACK_TITLES[0], 'יש לוודא ששרת ה-API פועל ולנסות שוב.');
  }

  let body: unknown = error.error;
  if (typeof body === 'string') {
    try {
      body = JSON.parse(body);
    } catch {
      body = null;
    }
  }

  const fallbackTitle = FALLBACK_TITLES[error.status] ?? (error.status >= 500 ? FALLBACK_TITLES[500]! : 'שגיאה');
  if (!isRecord(body)) return new ApiError(error.status, fallbackTitle, null);

  const runId = body['runId'];
  return new ApiError(
    error.status,
    asString(body['title']) ?? fallbackTitle,
    asString(body['detail']),
    readValidationErrors(body),
    typeof runId === 'number' ? runId : null,
    asString(body['path']),
    asString(body['traceId']),
  );
}

/** Turns every HTTP failure into an ApiError, so callers never parse HttpErrorResponse themselves. */
export const apiErrorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: unknown) => throwError(() => toApiError(error))));
