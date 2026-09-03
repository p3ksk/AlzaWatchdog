import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TrackedItem } from './models';
import { AccountService } from './account.service';
import { WatchdogApi, describeError } from './watchdog-api.service';

/**
 * The items of whichever list is open. Each action patches only the row it
 * touched so the list never flickers while one card is working.
 */
@Injectable({ providedIn: 'root' })
export class ItemsStore {
  private readonly api = inject(WatchdogApi);
  private readonly account = inject(AccountService);

  private readonly _listId = signal<string | null>(null);
  private readonly _listName = signal<string>('');
  private readonly _items = signal<TrackedItem[]>([]);
  private readonly _loading = signal(false);
  private readonly _loaded = signal(false);
  private readonly _busyIds = signal<ReadonlySet<string>>(new Set());
  private readonly _error = signal<string | null>(null);
  private readonly _missing = signal(false);

  readonly listId = this._listId.asReadonly();
  readonly listName = this._listName.asReadonly();
  readonly items = this._items.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();
  /** The list id in the URL matches nothing on the server. */
  readonly missing = this._missing.asReadonly();

  readonly isEmpty = computed(() => this._loaded() && this._items().length === 0);
  readonly watchedCount = computed(() => this._items().length);
  readonly atLowestCount = computed(() => this._items().filter((i) => i.isAtLowest).length);
  readonly unavailableCount = computed(
    () => this._items().filter((i) => i.availability && i.availability !== 'InStock').length,
  );

  isBusy(id: string): boolean {
    return this._busyIds().has(id);
  }

  dismissError(): void {
    this._error.set(null);
  }

  async open(listId: string): Promise<void> {
    if (this._listId() !== listId) {
      // Switching lists must not leave the previous list's cards on screen.
      this._items.set([]);
      this._loaded.set(false);
      this._listId.set(listId);
    }

    this._loading.set(true);
    this._missing.set(false);

    try {
      const detail = await firstValueFrom(this.api.getList(listId));
      this._listName.set(detail.name);
      this._items.set(detail.items);
      this._error.set(null);
      this._loaded.set(true);
    } catch (error) {
      if (isNotFound(error)) {
        this._missing.set(true);
      } else {
        this._error.set(describeError(error, 'Could not load this list.'));
      }
    } finally {
      this._loading.set(false);
    }
  }

  setListName(name: string): void {
    this._listName.set(name);
  }

  /** @returns an error message, or null when the item was added. */
  async add(url: string): Promise<string | null> {
    const listId = this._listId();
    if (!listId) {
      return 'No list is open.';
    }

    try {
      const item = await firstValueFrom(this.api.addItem(listId, url));
      this._items.update((items) => [item, ...items]);
      this.account.adjustItemCount(listId, 1);
      return null;
    } catch (error) {
      return describeError(error, 'Could not add that item.');
    }
  }

  async resume(id: string): Promise<string | null> {
    const listId = this._listId();
    if (!listId) {
      return null;
    }

    this.markBusy(id, true);
    try {
      const item = await firstValueFrom(this.api.resumeItem(listId, id));
      this._items.update((items) => items.map((i) => (i.id === id ? item : i)));
      return null;
    } catch (error) {
      return describeError(error, 'Could not resume that item.');
    } finally {
      this.markBusy(id, false);
    }
  }

  async remove(id: string): Promise<string | null> {
    const listId = this._listId();
    if (!listId) {
      return null;
    }

    this.markBusy(id, true);
    try {
      await firstValueFrom(this.api.deleteItem(listId, id));
      this._items.update((items) => items.filter((i) => i.id !== id));
      this.account.adjustItemCount(listId, -1);
      return null;
    } catch (error) {
      this.markBusy(id, false);
      return describeError(error, 'Could not remove that item.');
    }
  }

  /** @returns an error message, or null when the new order was saved. */
  async reorder(orderedItemIds: string[]): Promise<string | null> {
    const listId = this._listId();
    if (!listId) {
      return null;
    }

    const previous = this._items().map((item) => item.id);
    this.applyOrder(orderedItemIds);

    try {
      await firstValueFrom(this.api.reorderItems(listId, orderedItemIds));
      return null;
    } catch (error) {
      this.applyOrder(previous);
      return describeError(error, 'Could not save that order.');
    }
  }

  private applyOrder(orderedItemIds: string[]): void {
    const byId = new Map(this._items().map((item) => [item.id, item]));

    const ordered = orderedItemIds
      .map((id) => byId.get(id))
      .filter((item): item is TrackedItem => item !== undefined)
      // Rewriting sortOrder is the point, not just reordering the array. The list
      // is rendered by sorting on that field, so an array whose items still carry
      // their old positions is re-sorted straight back and the drag appears to do
      // nothing. The index matches what the server stores (SortOrder = position),
      // which keeps the optimistic view identical to the next load.
      .map((item, index) => ({ ...item, sortOrder: index }));

    this._items.set(ordered);
  }

  private markBusy(id: string, busy: boolean): void {
    this._busyIds.update((ids) => {
      const next = new Set(ids);
      if (busy) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }
}

function isNotFound(error: unknown): boolean {
  return typeof error === 'object' && error !== null && (error as { status?: number }).status === 404;
}
