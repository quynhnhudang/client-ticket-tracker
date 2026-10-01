import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps logged-out users on the login page, remembering where they were going
 * (so a link in the escalation email lands on the ticket after login).
 *
 * This is a UX convenience, not security: anyone can bypass client-side code.
 * The real protection is the API's fallback authorization policy, which
 * rejects every request without a valid cookie.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isLoggedIn()) return true;

  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** The login page itself: skip it if already logged in. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() ? inject(Router).createUrlTree(['/tickets']) : true;
};

/**
 * Only follow returnUrl if it is a path inside this app. Without this check,
 * a crafted link like /login?returnUrl=//evil.example would send the user to
 * another site right after they log in (an "open redirect").
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/tickets';
  }
  return value;
}
