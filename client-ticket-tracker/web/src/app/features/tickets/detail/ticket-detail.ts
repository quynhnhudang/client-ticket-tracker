import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap, tap } from 'rxjs';
import { STATUS_ACTIONS, STATUS_LABELS } from '../../../core/labels';
import { TicketDetail, TicketStatus } from '../../../core/models';
import { problemMessage } from '../../../core/problem';
import { TicketsApi } from '../../../core/tickets.api';
import { Badge } from '../../../shared/badge';

@Component({
  selector: 'app-ticket-detail',
  imports: [DatePipe, FormsModule, RouterLink, Badge],
  templateUrl: './ticket-detail.html',
})
export class TicketDetailPage {
  private readonly api = inject(TicketsApi);

  /** Route parameter :id, bound by withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly statusActions = STATUS_ACTIONS;

  protected readonly ticket = signal<TicketDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  private readonly reloadCount = signal(0);

  // Status-change panel state.
  protected readonly pendingStatus = signal<TicketStatus | null>(null);
  protected note = '';
  protected resolutionNotes = '';
  protected readonly actionError = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly saving = signal(false);

  // Comment form state.
  protected commentBody = '';
  protected readonly commentError = signal<string | null>(null);
  protected readonly postingComment = signal(false);

  /** The same rules as TicketStatusRules on the server, for instant feedback. The server still enforces them. */
  protected readonly noteRequired = computed(() => this.pendingStatus() === 'Escalated');
  protected readonly resolutionRequired = computed(() => this.pendingStatus() === 'Resolved');

  constructor() {
    // Reload when the id changes (navigating ticket to ticket) or when reload() is called.
    const request = computed(() => ({ id: Number(this.id()), n: this.reloadCount() }));

    toObservable(request)
      .pipe(
        tap(() => this.loadError.set(null)),
        switchMap(({ id }) =>
          Number.isInteger(id) && id > 0
            ? this.api.get(id).pipe(
                catchError((err: unknown) => {
                  this.loadError.set(problemMessage(err));
                  return of(null);
                }),
              )
            : of(null),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((ticket) => this.ticket.set(ticket));
  }

  protected reload(): void {
    this.cancelStatusChange();
    this.reloadCount.update((n) => n + 1);
  }

  protected startStatusChange(status: TicketStatus): void {
    this.pendingStatus.set(status);
    this.note = '';
    this.resolutionNotes = this.ticket()?.resolutionNotes ?? '';
    this.actionError.set(null);
    this.conflict.set(false);
  }

  protected cancelStatusChange(): void {
    this.pendingStatus.set(null);
    this.actionError.set(null);
    this.conflict.set(false);
  }

  protected canSubmitStatus(): boolean {
    if (this.saving()) return false;
    if (this.noteRequired() && !this.note.trim()) return false;
    if (this.resolutionRequired() && !this.resolutionNotes.trim()) return false;
    return true;
  }

  protected submitStatusChange(): void {
    const ticket = this.ticket();
    const newStatus = this.pendingStatus();
    if (!ticket || !newStatus || !this.canSubmitStatus()) return;

    this.saving.set(true);
    this.actionError.set(null);
    this.api
      .changeStatus(ticket.id, {
        newStatus,
        note: this.note.trim() || null,
        resolutionNotes: this.resolutionRequired() ? this.resolutionNotes.trim() : null,
      })
      .subscribe({
        next: (updated) => {
          // The API returns the whole updated ticket (new status, new history row,
          // new allowed transitions), so there's no second request to refresh it.
          this.ticket.set(updated);
          this.pendingStatus.set(null);
          this.saving.set(false);
        },
        error: (err: unknown) => {
          // 409 = someone else changed this ticket since it was loaded (RowVersion check).
          this.conflict.set(err instanceof HttpErrorResponse && err.status === 409);
          this.actionError.set(problemMessage(err));
          this.saving.set(false);
        },
      });
  }

  protected addComment(): void {
    const ticket = this.ticket();
    const body = this.commentBody.trim();
    if (!ticket || !body) return;

    this.postingComment.set(true);
    this.commentError.set(null);
    this.api.addComment(ticket.id, body).subscribe({
      next: (comment) => {
        // Update the signal immutably (new object, new array) so anything
        // computed from it recalculates.
        this.ticket.update((t) => (t ? { ...t, comments: [...t.comments, comment] } : t));
        this.commentBody = '';
        this.postingComment.set(false);
      },
      error: (err: unknown) => {
        this.commentError.set(problemMessage(err));
        this.postingComment.set(false);
      },
    });
  }
}
