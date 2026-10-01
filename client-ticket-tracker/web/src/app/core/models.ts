// TypeScript mirrors of the API's DTOs (api/src/TicketTracker.Api/Dtos).
// Enums travel as strings ("InProgress"), because the API registers
// JsonStringEnumConverter. String-literal union types give the same
// compile-time checking as an enum, with no runtime code.

export type TicketStatus = 'Open' | 'InProgress' | 'Escalated' | 'Resolved';
export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Urgent';
export type TicketCategory = 'Bug' | 'Question' | 'DataFix';

export const STATUSES: readonly TicketStatus[] = ['Open', 'InProgress', 'Escalated', 'Resolved'];
export const PRIORITIES: readonly TicketPriority[] = ['Urgent', 'High', 'Medium', 'Low'];
export const CATEGORIES: readonly TicketCategory[] = ['Bug', 'Question', 'DataFix'];

export interface Agent {
  id: number;
  email: string;
  displayName: string;
}

export interface Client {
  id: number;
  name: string;
  email: string;
}

export interface TicketSummary {
  id: number;
  title: string;
  status: TicketStatus;
  priority: TicketPriority;
  category: TicketCategory;
  clientName: string;
  createdAt: string; // ISO 8601 with offset, e.g. "2026-09-25T18:46:16.477+00:00"
  updatedAt: string;
}

export interface Comment {
  id: number;
  body: string;
  authorName: string;
  createdAt: string;
}

export interface StatusHistoryEntry {
  id: number;
  fromStatus: TicketStatus | null;
  toStatus: TicketStatus;
  changedBy: string;
  changedAt: string;
  note: string | null;
}

export interface TicketDetail {
  id: number;
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  resolutionNotes: string | null;
  createdAt: string;
  updatedAt: string;
  resolvedAt: string | null;
  client: Client;
  createdBy: string;
  allowedNextStatuses: TicketStatus[];
  comments: Comment[];
  history: StatusHistoryEntry[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface SearchHit {
  id: number;
  title: string;
  status: TicketStatus;
  priority: TicketPriority;
  category: TicketCategory;
  clientName: string;
  createdAt: string;
  matchedOn: 'id' | 'client' | 'keyword';
}

export interface SearchResult {
  query: string;
  mode: 'id' | 'fulltext' | 'like-fallback';
  items: SearchHit[];
}

export interface CreateTicketRequest {
  title: string;
  description: string;
  category: TicketCategory;
  priority: TicketPriority;
  clientName: string;
  clientEmail: string;
}

export interface ChangeStatusRequest {
  newStatus: TicketStatus;
  note?: string | null;
  resolutionNotes?: string | null;
}

export interface TriageSuggestion {
  category: TicketCategory;
  priority: TicketPriority;
  reason: string;
}
