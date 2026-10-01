import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { PRIORITY_LABELS, STATUS_LABELS } from '../../../core/labels';
import {
  PRIORITIES,
  PagedResult,
  STATUSES,
  TicketPriority,
  TicketStatus,
  TicketSummary,
} from '../../../core/models';
import { problemMessage } from '../../../core/problem';
import { TicketsApi } from '../../../core/tickets.api';
import { Badge } from '../../../shared/badge';

const PAGE_SIZE = 25;

/**
 * The ticket queue.
 *
 * The URL is the source of truth for the filters (/tickets?status=Open&priority=High&page=2):
 * changing a filter updates the URL, and the URL change drives the reload.
 * So filtered views can be bookmarked or shared, the back button undoes a
 * filter change, and a refresh keeps the current view.
 */
@Component({
  selector: 'app-ticket-queue',
  imports: [RouterLink, DatePipe, Badge],
  templateUrl: './ticket-queue.html',
})
export class TicketQueue {
  private readonly api = inject(TicketsApi);
  private readonly router = inject(Router);

  // Query parameters, bound by withComponentInputBinding. undefined when absent.
  readonly status = input<string>();
  readonly priority = input<string>();
  readonly page = input<string>();

  protected readonly statuses = STATUSES;
  protected readonly priorities = PRIORITIES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;

  protected readonly result = signal<PagedResult<TicketSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Ignore values that aren't real statuses/priorities (someone edited the URL). */
  protected readonly statusFilter = computed(() => {
    const s = this.status();
    return STATUSES.includes(s as TicketStatus) ? (s as TicketStatus) : null;
  });
  protected readonly priorityFilter = computed(() => {
    const p = this.priority();
    return PRIORITIES.includes(p as TicketPriority) ? (p as TicketPriority) : null;
  });
  protected readonly pageNumber = computed(() => Math.max(1, Number(this.page()) || 1));

  protected readonly totalPages = computed(() => {
    const r = this.result();
    return r ? Math.max(1, Math.ceil(r.totalCount / r.pageSize)) : 1;
  });

  private readonly query = computed(() => ({
    status: this.statusFilter(),
    priority: this.priorityFilter(),
    page: this.pageNumber(),
    pageSize: PAGE_SIZE,
  }));

  constructor() {
    // Whenever the query changes, fetch the page. switchMap cancels the
    // previous request if it's still in flight, so a slow response for an old
    // filter can never overwrite the results of a newer one.
    toObservable(this.query)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((q) =>
          this.api.list(q).pipe(
            catchError((err: unknown) => {
              this.error.set(problemMessage(err));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.result.set(result);
        this.loading.set(false);
      });
  }

  protected setFilter(name: 'status' | 'priority', value: string): void {
    // Any filter change goes back to page 1; null removes the parameter.
    void this.router.navigate([], {
      queryParams: { [name]: value || null, page: null },
      queryParamsHandling: 'merge',
    });
  }

  protected goToPage(page: number): void {
    void this.router.navigate([], {
      queryParams: { page: page === 1 ? null : page },
      queryParamsHandling: 'merge',
    });
  }
}
