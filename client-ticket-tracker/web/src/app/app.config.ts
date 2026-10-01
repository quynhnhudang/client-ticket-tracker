import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { AuthService } from './core/auth.service';
import { authInterceptor } from './core/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: route params (:id) and query params (?status=)
    // are passed straight into component input() signals.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Ask the server "who am I?" before the first route is resolved, so the
    // auth guard already knows the answer on a page refresh.
    provideAppInitializer(() => inject(AuthService).loadCurrentAgent()),
  ],
};
