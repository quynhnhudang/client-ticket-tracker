import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, firstValueFrom, map, of, tap } from 'rxjs';
import { Agent } from './models';

/**
 * Who is logged in.
 *
 * The session itself lives in an HttpOnly cookie that this code can't read
 * (that's the point: injected script can't steal it either). So the app asks
 * the server, via GET /api/auth/me, once at startup, and keeps the answer in a
 * signal. The browser attaches the cookie to every /api request automatically,
 * because the app and the API are served from the same origin.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly agentSignal = signal<Agent | null>(null);
  readonly agent = this.agentSignal.asReadonly();
  readonly isLoggedIn = computed(() => this.agentSignal() !== null);

  /** Called once before the first route renders (see app.config.ts). Never throws. */
  loadCurrentAgent(): Promise<void> {
    const request = this.http.get<Agent>('/api/auth/me').pipe(
      tap((agent) => this.agentSignal.set(agent)),
      map(() => undefined),
      // 401 just means "not logged in"; anything else (API down) is treated the
      // same way here, and the login page will show the real error on submit.
      catchError(() => {
        this.agentSignal.set(null);
        return of(undefined);
      }),
    );
    return firstValueFrom(request);
  }

  login(email: string, password: string): Observable<Agent> {
    return this.http
      .post<Agent>('/api/auth/login', { email, password })
      .pipe(tap((agent) => this.agentSignal.set(agent)));
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      // Forget the user locally even if the call fails (e.g. already expired).
      catchError(() => of(undefined)),
      tap(() => this.agentSignal.set(null)),
    );
  }

  /** Used by the interceptor when the server says the session has expired. */
  clear(): void {
    this.agentSignal.set(null);
  }
}
