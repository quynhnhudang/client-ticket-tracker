import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ChangeStatusRequest,
  Comment,
  CreateTicketRequest,
  PagedResult,
  SearchResult,
  TicketDetail,
  TicketPriority,
  TicketStatus,
  TicketSummary,
  TriageSuggestion,
} from './models';

export interface QueueQuery {
  status?: TicketStatus | null;
  priority?: TicketPriority | null;
  page?: number;
  pageSize?: number;
}

/**
 * One method per API endpoint. Relative URLs ("/api/...") work in both
 * environments: in development the Angular dev server proxies /api to Kestrel
 * (proxy.conf.json); in production nginx does the same.
 */
@Injectable({ providedIn: 'root' })
export class TicketsApi {
  private readonly http = inject(HttpClient);

  list(query: QueueQuery): Observable<PagedResult<TicketSummary>> {
    // HttpParams URL-encodes values; building the query string by hand wouldn't.
    let params = new HttpParams()
      .set('page', query.page ?? 1)
      .set('pageSize', query.pageSize ?? 25);
    if (query.status) params = params.set('status', query.status);
    if (query.priority) params = params.set('priority', query.priority);
    return this.http.get<PagedResult<TicketSummary>>('/api/tickets', { params });
  }

  search(q: string): Observable<SearchResult> {
    return this.http.get<SearchResult>('/api/tickets/search', { params: { q } });
  }

  get(id: number): Observable<TicketDetail> {
    return this.http.get<TicketDetail>(`/api/tickets/${id}`);
  }

  create(request: CreateTicketRequest): Observable<TicketDetail> {
    return this.http.post<TicketDetail>('/api/tickets', request);
  }

  changeStatus(id: number, request: ChangeStatusRequest): Observable<TicketDetail> {
    return this.http.post<TicketDetail>(`/api/tickets/${id}/status`, request);
  }

  addComment(id: number, body: string): Observable<Comment> {
    return this.http.post<Comment>(`/api/tickets/${id}/comments`, { body });
  }

  /** Is the optional LLM triage feature configured on the server? */
  triageEnabled(): Observable<{ enabled: boolean }> {
    return this.http.get<{ enabled: boolean }>('/api/triage/status');
  }

  suggestTriage(title: string, description: string): Observable<TriageSuggestion> {
    return this.http.post<TriageSuggestion>('/api/triage/suggest', { title, description });
  }
}
