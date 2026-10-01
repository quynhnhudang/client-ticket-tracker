import { DatePipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { SearchResult } from '../../../core/models';
import { problemMessage } from '../../../core/problem';
import { TicketsApi } from '../../../core/tickets.api';
import { Badge } from '../../../shared/badge';

/** /search?q=... The top-bar search box navigates here. */
@Component({
  selector: 'app-ticket-search',
  imports: [DatePipe, RouterLink, Badge],
  template: `
    <h1>Search</h1>

    @if (!q()?.trim()) {
      <p class="muted">Type in the search box: a ticket number (#12), the start of a client's name, or keywords.</p>
    }

    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }

    @if (result(); as r) {
      <p class="muted">
        {{ r.items.length }} result{{ r.items.length === 1 ? '' : 's' }} for “{{ r.query }}”
        @if (r.items.length === 50) { (showing the first 50) }
        · <span class="mode" [title]="modeHelp[r.mode]">{{ modeLabel[r.mode] }}</span>
      </p>

      <div class="table-wrap" [class.loading]="loading()">
        <table>
          <thead>
            <tr><th>#</th><th>Title</th><th>Client</th><th>Priority</th><th>Status</th><th>Created</th><th>Matched on</th></tr>
          </thead>
          <tbody>
            @for (h of r.items; track h.id) {
              <tr>
                <td class="num">{{ h.id }}</td>
                <td><a [routerLink]="['/tickets', h.id]">{{ h.title }}</a></td>
                <td>{{ h.clientName }}</td>
                <td><app-badge kind="priority" [value]="h.priority" /></td>
                <td><app-badge kind="status" [value]="h.status" /></td>
                <td class="nowrap">{{ h.createdAt | date: 'MMM d, y' }}</td>
                <td>{{ matchedLabel[h.matchedOn] }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="empty">No tickets found.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class TicketSearch {
  private readonly api = inject(TicketsApi);

  readonly q = input<string>();

  protected readonly result = signal<SearchResult | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  // The API reports which SQL actually ran; showing it makes the fallback visible.
  protected readonly modeLabel: Record<SearchResult['mode'], string> = {
    id: 'ticket ID lookup',
    fulltext: 'full-text search',
    'like-fallback': 'LIKE fallback search',
  };
  protected readonly modeHelp: Record<SearchResult['mode'], string> = {
    id: 'Exact match on the ticket number.',
    fulltext: 'Client-name prefix + SQL Server full-text index on title and description.',
    'like-fallback': 'Client-name prefix + LIKE on title/description (full-text index not available).',
  };
  protected readonly matchedLabel = { id: 'Ticket #', client: 'Client name', keyword: 'Keywords' } as const;

  constructor() {
    toObservable(this.q)
      .pipe(
        tap(() => this.error.set(null)),
        switchMap((q) => {
          const text = q?.trim() ?? '';
          if (!text) return of(null);
          this.loading.set(true);
          return this.api.search(text.slice(0, 200)).pipe(
            catchError((err: unknown) => {
              this.error.set(problemMessage(err));
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((r) => {
        this.result.set(r);
        this.loading.set(false);
      });
  }
}
