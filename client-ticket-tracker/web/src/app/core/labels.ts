import { TicketCategory, TicketPriority, TicketStatus } from './models';

// Display text for enum values. Kept in one place so a label change is a
// one-line edit, and so templates never contain raw enum strings.

export const STATUS_LABELS: Record<TicketStatus, string> = {
  Open: 'Open',
  InProgress: 'In progress',
  Escalated: 'Escalated',
  Resolved: 'Resolved',
};

export const PRIORITY_LABELS: Record<TicketPriority, string> = {
  Low: 'Low',
  Medium: 'Medium',
  High: 'High',
  Urgent: 'Urgent',
};

export const CATEGORY_LABELS: Record<TicketCategory, string> = {
  Bug: 'Bug',
  Question: 'Question',
  DataFix: 'Data fix',
};

/** Verb shown on the button that moves a ticket INTO this status. */
export const STATUS_ACTIONS: Record<TicketStatus, string> = {
  Open: 'Reopen',
  InProgress: 'Start work',
  Escalated: 'Escalate to engineering',
  Resolved: 'Resolve',
};
