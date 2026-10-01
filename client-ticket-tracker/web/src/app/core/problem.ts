import { HttpErrorResponse } from '@angular/common/http';

/**
 * The API returns errors as RFC 9457 "problem details":
 *   { "title": "Resolution notes are required...", "status": 400 }
 * and validation failures add a per-field map:
 *   { "title": "One or more validation errors occurred.", "errors": { "Title": ["..."] } }
 * These helpers turn either shape into text the UI can show.
 */
interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the server. Is the API running?';
    if (error.status === 429) return 'Too many attempts. Wait a minute and try again.';

    const body = error.error as ProblemDetails | null;
    const fieldErrors = body?.errors ? Object.values(body.errors).flat() : [];
    if (fieldErrors.length > 0) return fieldErrors.join(' ');
    if (body?.detail) return body.detail;
    if (body?.title) return body.title;
    return `Request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}

/**
 * Field-level validation errors keyed by lower-camel field name, so a form can
 * show the server's message next to the matching input ("Title" -> "title").
 */
export function fieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof HttpErrorResponse) || error.status !== 400) return {};
  const errors = (error.error as ProblemDetails | null)?.errors ?? {};
  const result: Record<string, string> = {};
  for (const [key, messages] of Object.entries(errors)) {
    // Keys look like "Title" or, for JSON conversion errors, "$.category".
    const name = key.replace(/^\$\./, '');
    result[name.charAt(0).toLowerCase() + name.slice(1)] = messages.join(' ');
  }
  return result;
}
