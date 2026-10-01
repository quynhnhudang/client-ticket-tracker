import { HttpErrorResponse } from '@angular/common/http';
import { fieldErrors, problemMessage } from './problem';

describe('problemMessage', () => {
  it('uses the problem-details title', () => {
    const err = new HttpErrorResponse({ status: 400, error: { title: 'Resolution notes are required.' } });
    expect(problemMessage(err)).toBe('Resolution notes are required.');
  });

  it('prefers per-field validation messages', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { title: 'One or more validation errors occurred.', errors: { Title: ['Too short.'] } },
    });
    expect(problemMessage(err)).toBe('Too short.');
  });

  it('explains network failures and rate limiting', () => {
    expect(problemMessage(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach');
    expect(problemMessage(new HttpErrorResponse({ status: 429 }))).toContain('Too many');
  });
});

describe('fieldErrors', () => {
  it('maps server field names to form control names', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { errors: { ClientEmail: ['Invalid.'], '$.category': ['Bad value.'] } },
    });
    expect(fieldErrors(err)).toEqual({ clientEmail: 'Invalid.', category: 'Bad value.' });
  });

  it('ignores non-400 responses', () => {
    expect(fieldErrors(new HttpErrorResponse({ status: 500 }))).toEqual({});
  });
});
