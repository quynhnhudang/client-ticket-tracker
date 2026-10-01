import { Component, computed, input } from '@angular/core';
import { CATEGORY_LABELS, PRIORITY_LABELS, STATUS_LABELS } from '../core/labels';
import { TicketCategory, TicketPriority, TicketStatus } from '../core/models';

/**
 * A small coloured label for a status, priority or category.
 * Usage: <app-badge kind="status" [value]="t.status" />
 * The CSS class is derived from the value (e.g. "badge status-Escalated"),
 * and the colours live in styles.css.
 */
@Component({
  selector: 'app-badge',
  template: `<span class="badge" [class]="cssClass()">{{ label() }}</span>`,
})
export class Badge {
  readonly kind = input.required<'status' | 'priority' | 'category'>();
  readonly value = input.required<TicketStatus | TicketPriority | TicketCategory>();

  protected readonly label = computed(() => {
    const v = this.value();
    switch (this.kind()) {
      case 'status':
        return STATUS_LABELS[v as TicketStatus];
      case 'priority':
        return PRIORITY_LABELS[v as TicketPriority];
      default:
        return CATEGORY_LABELS[v as TicketCategory];
    }
  });

  protected readonly cssClass = computed(() => `badge ${this.kind()}-${this.value()}`);
}
