import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * If any API call comes back 401 (the 8-hour cookie expired, or the server
 * restarted and lost its keys), forget the user and go to the login page,
 * keeping the current URL so they return to it afterwards.
 *
 * Calls to /api/auth/* are skipped: a 401 from /me at startup is normal, and a
 * 401 from /login should be shown as "invalid email or password", not redirect.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !req.url.startsWith('/api/auth/')) {
        auth.clear();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    }),
  );
};
