import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { WatchList } from './models';
import { WatchdogApi } from './watchdog-api.service';

/**
 * Holds the account key taken from the URL and the lists it owns.
 *
 * Nothing is persisted. The address bar is the only copy of the key, which is
 * what makes a bookmark the way back in — and also means a lost URL is a lost
 * account, so the UI has to keep saying so.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly api = inject(WatchdogApi);

  private readonly _key = signal<string | null>(null);
  private readonly _lists = signal<WatchList[]>([]);
  private readonly _loadFailed = signal(false);
  private readonly _hasAlzaPlus = signal(false);
  private readonly _isAdmin = signal(false);

  readonly key = this._key.asReadonly();
  readonly lists = this._lists.asReadonly();
  /** Whether to count AlzaPlus+ members' prices as prices this person can pay. */
  readonly hasAlzaPlus = this._hasAlzaPlus.asReadonly();
  /** Granted by server configuration, not by anything the browser holds. */
  readonly isAdmin = this._isAdmin.asReadonly();
  /** The key in the URL does not belong to any account. */
  readonly loadFailed = this._loadFailed.asReadonly();

  readonly hasLists = computed(() => this._lists().length > 0);

  /**
   * Points the app at the account in the current URL. Called from the route
   * guard, synchronously, so the interceptor has the key before the first
   * request goes out.
   */
  use(key: string): void {
    if (this._key() === key) {
      return;
    }

    this._key.set(key);
    this._lists.set([]);
    this._loadFailed.set(false);
    this._hasAlzaPlus.set(false);
    this._isAdmin.set(false);
  }

  /** @returns the lists on the current key, or an empty array if it is unknown. */
  async loadLists(): Promise<WatchList[]> {
    const key = this._key();
    if (!key) {
      return [];
    }

    try {
      const account = await firstValueFrom(this.api.getAccount(key));
      this._lists.set(account.lists);
      this._hasAlzaPlus.set(account.hasAlzaPlus);
      this._isAdmin.set(account.isAdmin);
      this._loadFailed.set(false);
      return account.lists;
    } catch {
      this._lists.set([]);
      this._loadFailed.set(true);
      return [];
    }
  }

  /**
   * Creates the account together with its first product. Nothing exists until
   * this succeeds, so abandoning the wizard leaves no empty account behind.
   */
  async startWithFirstProduct(url: string): Promise<{ key: string; lists: WatchList[] }> {
    const account = await firstValueFrom(this.api.startWithFirstProduct(url));
    this._key.set(account.userId);
    this._lists.set(account.lists);
    this._hasAlzaPlus.set(account.hasAlzaPlus);
    this._isAdmin.set(account.isAdmin);
    this._loadFailed.set(false);
    return { key: account.userId, lists: account.lists };
  }

  /** Mints a brand new account. Only the root route does this. */
  async createAccount(): Promise<{ key: string; lists: WatchList[] }> {
    const account = await firstValueFrom(this.api.createAccount());
    this._key.set(account.userId);
    this._lists.set(account.lists);
    this._hasAlzaPlus.set(account.hasAlzaPlus);
    this._isAdmin.set(account.isAdmin);
    this._loadFailed.set(false);
    return { key: account.userId, lists: account.lists };
  }

  /**
   * Applied locally first: the switch only changes how prices already on screen
   * are presented, so waiting for the round trip would make it feel broken.
   */
  async setHasAlzaPlus(value: boolean): Promise<void> {
    const previous = this._hasAlzaPlus();
    this._hasAlzaPlus.set(value);

    try {
      await firstValueFrom(this.api.updateAccount(value));
    } catch {
      this._hasAlzaPlus.set(previous);
      throw new Error('Could not save that setting.');
    }
  }

  async createList(name: string): Promise<WatchList> {
    const list = await firstValueFrom(this.api.createList(name));
    this._lists.update((lists) => [...lists, list]);
    return list;
  }

  async renameList(listId: string, name: string): Promise<void> {
    const updated = await firstValueFrom(this.api.renameList(listId, name));
    this._lists.update((lists) => lists.map((l) => (l.id === listId ? updated : l)));
  }

  async deleteList(listId: string): Promise<void> {
    await firstValueFrom(this.api.deleteList(listId));
    this._lists.update((lists) => lists.filter((l) => l.id !== listId));
  }

  /** Keeps the navbar's item counts honest after an add or a remove. */
  adjustItemCount(listId: string, delta: number): void {
    this._lists.update((lists) =>
      lists.map((l) => (l.id === listId ? { ...l, itemCount: Math.max(0, l.itemCount + delta) } : l)),
    );
  }
}
