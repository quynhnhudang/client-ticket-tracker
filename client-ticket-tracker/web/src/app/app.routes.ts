import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

// loadComponent = lazy loading: each page's code is a separate JavaScript
// chunk, downloaded the first time that page is visited, which keeps the
// initial download small.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tickets' },
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    children: [
      {
        path: 'tickets',
        title: 'Ticket queue',
        loadComponent: () => import('./features/tickets/queue/ticket-queue').then((m) => m.TicketQueue),
      },
      {
        path: 'tickets/new',
        title: 'New ticket',
        loadComponent: () => import('./features/tickets/new/ticket-new').then((m) => m.TicketNew),
      },
      {
        path: 'tickets/:id',
        title: 'Ticket',
        loadComponent: () => import('./features/tickets/detail/ticket-detail').then((m) => m.TicketDetailPage),
      },
      {
        path: 'search',
        title: 'Search',
        loadComponent: () => import('./features/tickets/search/ticket-search').then((m) => m.TicketSearch),
      },
    ],
  },
  { path: '**', redirectTo: 'tickets' },
];
