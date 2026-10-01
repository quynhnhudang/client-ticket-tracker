import { safeReturnUrl } from './auth.guard';

describe('safeReturnUrl', () => {
  it('keeps paths inside the app', () => {
    expect(safeReturnUrl('/tickets/42')).toBe('/tickets/42');
    expect(safeReturnUrl('/tickets?status=Open')).toBe('/tickets?status=Open');
  });

  it('rejects anything that could leave the app (open redirect)', () => {
    expect(safeReturnUrl('//evil.example')).toBe('/tickets');
    expect(safeReturnUrl('/\\evil.example')).toBe('/tickets');
    expect(safeReturnUrl('https://evil.example')).toBe('/tickets');
    expect(safeReturnUrl('javascript:alert(1)')).toBe('/tickets');
  });

  it('defaults when missing', () => {
    expect(safeReturnUrl(undefined)).toBe('/tickets');
    expect(safeReturnUrl('')).toBe('/tickets');
  });
});
