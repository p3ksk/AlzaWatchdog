import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AccountImportResult,
  AccountResponse,
  AdminItem,
  AdminStats,
  AdminUser,
  ImportResult,
  ProblemDetails,
  TrackedItem,
  WatchList,
  WatchListDetail,
} from './models';

@Injectable({ providedIn: 'root' })
export class WatchdogApi {
  private readonly http = inject(HttpClient);

  createAccount(): Observable<AccountResponse> {
    return this.http.post<AccountResponse>('/api/users', {});
  }

  getAccount(userId: string): Observable<AccountResponse> {
    return this.http.get<AccountResponse>(`/api/users/${userId}`);
  }

  updateAccount(hasAlzaPlus: boolean): Observable<AccountResponse> {
    return this.http.patch<AccountResponse>('/api/users', { hasAlzaPlus });
  }

  getLists(): Observable<WatchList[]> {
    return this.http.get<WatchList[]>('/api/lists');
  }

  createList(name: string): Observable<WatchList> {
    return this.http.post<WatchList>('/api/lists', { name });
  }

  renameList(listId: string, name: string): Observable<WatchList> {
    return this.http.patch<WatchList>(`/api/lists/${listId}`, { name });
  }

  deleteList(listId: string): Observable<void> {
    return this.http.delete<void>(`/api/lists/${listId}`);
  }

  getList(listId: string): Observable<WatchListDetail> {
    return this.http.get<WatchListDetail>(`/api/lists/${listId}`);
  }

  addItem(listId: string, url: string): Observable<TrackedItem> {
    return this.http.post<TrackedItem>(`/api/lists/${listId}/items`, { url });
  }

  resumeItem(listId: string, itemId: string): Observable<TrackedItem> {
    return this.http.post<TrackedItem>(`/api/lists/${listId}/items/${itemId}/resume`, {});
  }

  deleteItem(listId: string, itemId: string): Observable<void> {
    return this.http.delete<void>(`/api/lists/${listId}/items/${itemId}`);
  }

  reorderItems(listId: string, orderedItemIds: string[]): Observable<void> {
    return this.http.put<void>(`/api/lists/${listId}/items/order`, { orderedItemIds });
  }

  getAdminStats(): Observable<AdminStats> {
    return this.http.get<AdminStats>('/api/admin/stats');
  }

  getAdminUsers(): Observable<AdminUser[]> {
    return this.http.get<AdminUser[]>('/api/admin/users');
  }

  getAdminItems(): Observable<AdminItem[]> {
    return this.http.get<AdminItem[]>('/api/admin/items');
  }

  exportProducts(): Observable<Blob> {
    return this.http.get('/api/export', { responseType: 'blob' });
  }

  importProducts(bundle: unknown): Observable<ImportResult> {
    return this.http.post<ImportResult>('/api/import', bundle);
  }

  /** Whole-account backup, for admins — distinct from a user's own product export. */
  adminBackupExport(userId?: string): Observable<string> {
    const url = userId ? `/api/admin/backup?userId=${userId}` : '/api/admin/backup';
    return this.http.get(url, { responseType: 'text' });
  }

  adminBackupImport(bundle: unknown): Observable<AccountImportResult> {
    return this.http.post<AccountImportResult>('/api/admin/backup', bundle);
  }
}

/** Turns an API failure into the message the user should see. */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | null;
    if (problem?.detail) {
      return problem.detail;
    }
    if (problem?.title) {
      return problem.title;
    }
    if (error.status === 0) {
      return 'Could not reach the watchdog API.';
    }
  }
  return fallback;
}
