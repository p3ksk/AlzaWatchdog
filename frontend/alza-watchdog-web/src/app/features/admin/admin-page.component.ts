import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { AccountService } from '../../core/account.service';
import { AccountImportResult, AdminItem, AdminStats, AdminUser } from '../../core/models';
import { WatchdogApi, describeError } from '../../core/watchdog-api.service';
import { compactGuid } from '../../core/guid';
import { formatPrice, formatRelative } from '../../core/format';
import { PriceChartComponent } from '../items/price-chart.component';

@Component({
  selector: 'app-admin-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, PriceChartComponent],
  templateUrl: './admin-page.component.html',
  styleUrl: './admin-page.component.scss',
})
export class AdminPageComponent {
  private readonly api = inject(WatchdogApi);
  private readonly account = inject(AccountService);

  protected readonly stats = signal<AdminStats | null>(null);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly items = signal<AdminItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly denied = signal(false);

  protected readonly expandedItem = signal<string | null>(null);
  protected readonly userFilter = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly importResult = signal<AccountImportResult | null>(null);

  /** Items narrowed to one account when a user row is selected. */
  protected readonly visibleItems = computed(() => {
    const filter = this.userFilter();
    return filter ? this.items().filter((i) => i.userId === filter) : this.items();
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const [stats, users, items] = await Promise.all([
        firstValueFrom(this.api.getAdminStats()),
        firstValueFrom(this.api.getAdminUsers()),
        firstValueFrom(this.api.getAdminItems()),
      ]);

      this.stats.set(stats);
      this.users.set(users);
      this.items.set(items);
      this.denied.set(false);
    } catch (error) {
      // 401/403 means this key is simply not an admin — a different message from
      // "the server is broken", and the common case when someone guesses the URL.
      const status = (error as { status?: number })?.status;
      this.denied.set(status === 401 || status === 403);
      this.error.set(describeError(error, 'Could not load admin data.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected shortId(id: string): string {
    return compactGuid(id).slice(0, 10);
  }

  protected price(value: number | null, currency: string | null): string {
    return formatPrice(value, currency);
  }

  protected ago(iso: string | null): string {
    return formatRelative(iso);
  }

  protected toggleItem(id: string): void {
    this.expandedItem.update((current) => (current === id ? null : id));
  }

  protected filterByUser(id: string): void {
    this.userFilter.update((current) => (current === id ? null : id));
  }

  protected clearFilter(): void {
    this.userFilter.set(null);
  }

  protected async exportAll(): Promise<void> {
    await this.download(undefined, 'alza-watchdog-all.json');
  }

  protected async exportUser(id: string): Promise<void> {
    await this.download(id, `alza-watchdog-user-${compactGuid(id)}.json`);
  }

  private async download(userId: string | undefined, filename: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      const json = await firstValueFrom(this.api.adminBackupExport(userId));
      const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));

      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = filename;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.error.set(describeError(error, 'Could not export.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async importFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.importResult.set(null);

    try {
      const bundle = JSON.parse(await file.text());
      this.importResult.set(await firstValueFrom(this.api.adminBackupImport(bundle)));
      await this.load();
    } catch (error) {
      this.error.set(
        error instanceof SyntaxError
          ? 'That file is not valid JSON.'
          : describeError(error, 'Could not import that bundle.'),
      );
    } finally {
      this.busy.set(false);
      // Clearing lets the same file be picked again after a fix.
      input.value = '';
    }
  }
}
