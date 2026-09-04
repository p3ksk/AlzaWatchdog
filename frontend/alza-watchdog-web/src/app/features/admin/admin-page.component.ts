import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { AccountService } from '../../core/account.service';
import { AccountImportResult, AdminItem, AdminStats, AdminUser, AdminWorker } from '../../core/models';
import { WatchdogApi, describeError } from '../../core/watchdog-api.service';
import { compactGuid } from '../../core/guid';
import { formatPrice, formatRelative } from '../../core/format';
import { PriceChartComponent } from '../items/price-chart.component';
import { TipComponent } from '../../shared/tip.component';
import { PaginatorComponent } from '../../shared/paginator.component';

type AdminTab = 'products' | 'accounts' | 'workers' | 'backup';

/**
 * Sort key putting the product due for a check soonest first.
 */
function nextCheckOrder(item: AdminItem): number {
  return item.lastCheckedAt ? new Date(item.lastCheckedAt).getTime() : 0;
}

/**
 * Lowercases and strips accents, so "cierny" finds "čierny". Product names come
 * from alza.sk in Slovak and nobody searching an admin table types the diacritics.
 */
function fold(value: string): string {
  return value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase();
}

@Component({
  selector: 'app-admin-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, PriceChartComponent, TipComponent, PaginatorComponent],
  templateUrl: './admin-page.component.html',
  styleUrl: './admin-page.component.scss',
})
export class AdminPageComponent {
  private readonly api = inject(WatchdogApi);
  private readonly account = inject(AccountService);

  protected readonly stats = signal<AdminStats | null>(null);
  protected readonly users = signal<AdminUser[]>([]);
  protected readonly items = signal<AdminItem[]>([]);
  protected readonly workers = signal<AdminWorker[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly denied = signal(false);

  protected readonly tab = signal<AdminTab>('products');

  protected readonly tabs: { key: AdminTab; label: string; count: boolean }[] = [
    { key: 'products', label: 'Products', count: true },
    { key: 'accounts', label: 'Accounts', count: true },
    { key: 'workers', label: 'Background workers', count: true },
    { key: 'backup', label: 'Backup', count: false },
  ];

  protected readonly expandedItem = signal<string | null>(null);
  protected readonly userFilter = signal<string | null>(null);

  protected readonly productQuery = signal('');
  protected readonly accountPage = signal(1);
  protected readonly productPage = signal(1);

  protected readonly accountPageSize = 25;
  protected readonly productPageSize = 20;

  protected readonly busy = signal(false);
  protected readonly importResult = signal<AccountImportResult | null>(null);

  /** Items narrowed to one account when a user row is selected. */
  protected readonly visibleItems = computed(() => {
    const filter = this.userFilter();
    return filter ? this.items().filter((i) => i.userId === filter) : this.items();
  });

  /**
   * One row per product rather than per tracking, ordered by how soon each is due
   * for its next price check. A product is stored once and shared, so listing it
   * once per watching list would show the same thing several times and disagree
   * with the "Products" figure above.
   */
  protected readonly visibleProducts = computed(() => {
    const groups = new Map<string, { item: AdminItem; trackedBy: AdminItem[] }>();

    for (const item of this.visibleItems()) {
      const group = groups.get(item.productCode);
      if (group) {
        group.trackedBy.push(item);
      } else {
        groups.set(item.productCode, { item, trackedBy: [item] });
      }
    }

    return [...groups.values()].sort((a, b) => nextCheckOrder(a.item) - nextCheckOrder(b.item));
  });

  /**
   * Products matching the search box: name, product code, or URL. The URL is
   * included so a link pasted from alza.sk finds the row it belongs to.
   */
  protected readonly matchingProducts = computed(() => {
    const query = fold(this.productQuery().trim());
    if (!query) {
      return this.visibleProducts();
    }

    return this.visibleProducts().filter(({ item }) =>
      fold(item.name ?? '').includes(query) ||
      `d${item.productCode}`.includes(query) ||
      fold(item.url).includes(query),
    );
  });

  protected readonly pagedUsers = computed(() =>
    this.page(this.users(), this.accountPageOf(this.users()), this.accountPageSize),
  );

  protected readonly pagedProducts = computed(() =>
    this.page(this.matchingProducts(), this.productPageOf(this.matchingProducts()), this.productPageSize),
  );

  /**
   * The requested page, held to what the list can actually show. A reload or an
   * import can shrink a list under a page number that was valid a moment ago.
   */
  private accountPageOf(rows: readonly unknown[]): number {
    return this.clamp(this.accountPage(), rows.length, this.accountPageSize);
  }

  private productPageOf(rows: readonly unknown[]): number {
    return this.clamp(this.productPage(), rows.length, this.productPageSize);
  }

  private clamp(page: number, total: number, size: number): number {
    return Math.min(Math.max(page, 1), Math.max(1, Math.ceil(total / size)));
  }

  private page<T>(rows: readonly T[], page: number, size: number): T[] {
    return rows.slice((page - 1) * size, page * size);
  }

  protected readonly accountPageNumber = computed(() => this.accountPageOf(this.users()));
  protected readonly productPageNumber = computed(() => this.productPageOf(this.matchingProducts()));

  /** A new search starts at the top, not on whatever page was open before. */
  protected searchProducts(query: string): void {
    this.productQuery.set(query);
    this.productPage.set(1);
  }

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const [stats, users, items, workers] = await Promise.all([
        firstValueFrom(this.api.getAdminStats()),
        firstValueFrom(this.api.getAdminUsers()),
        firstValueFrom(this.api.getAdminItems()),
        firstValueFrom(this.api.getAdminWorkers()),
      ]);

      this.stats.set(stats);
      this.users.set(users);
      this.items.set(items);
      this.workers.set(workers);
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

  /**
   * Explanations for the parts of a worker card the frontend draws itself. The
   * per-setting hints come from the worker that reports them instead, so they
   * stay with the value they describe.
   */
  protected readonly tips = {
    running: 'The worker is scheduled and will act at the time shown. Turn it off in configuration, not here.',
    disabled: 'Switched off in configuration. It still reports its settings, but it will never act.',
    idle: 'The process has restarted recently. Figures appear once the first pass finishes.',
    lastRun: 'when the last pass finished.',
    nextRun: 'when the worker is due to wake. It sleeps until the earliest piece of work is actually due, so this moves as work is added.',
    passes: 'Completed passes since the process last started. It resets on restart — it is not a lifetime total.',
    outcome: 'What the last completed pass reported. A block means Cloudflare refused a request and the sweep stopped early.',
    items: 'Rows on this account\'s lists. The same product put on two lists counts twice here, because that is two things the account is watching.',
    snaps: 'Price readings recorded for the products this account watches. Products are shared, so a product on two of its lists is still counted once — and the same history can appear under several accounts, which is why this column does not add up to the total above.',
  };

  protected ago(iso: string | null): string {
    return formatRelative(iso);
  }

  /** Absolute timestamp for a tooltip, since "in 4 hours" alone is hard to plan around. */
  protected when(iso: string | null): string {
    return iso ? `${new Date(iso).toLocaleString()} — ` : '';
  }

  /** Blocks and failures are worth colouring; "checked 4 products" is not. */
  protected isBadOutcome(outcome: string): boolean {
    return /blocked|failed/i.test(outcome);
  }

  protected toggleItem(id: string): void {
    this.expandedItem.update((current) => (current === id ? null : id));
  }

  protected count(tab: AdminTab): number {
    switch (tab) {
      case 'products': return new Set(this.items().map((i) => i.productCode)).size;
      case 'accounts': return this.users().length;
      case 'workers': return this.workers().length;
      default: return 0;
    }
  }

  /**
   * Filtering from the accounts table only makes sense if you are then shown the
   * products, so this follows the user across.
   */
  protected filterByUser(id: string): void {
    const next = this.userFilter() === id ? null : id;
    this.userFilter.set(next);

    this.productPage.set(1);

    if (next !== null) {
      this.tab.set('products');
    }
  }

  protected clearFilter(): void {
    this.userFilter.set(null);
    this.productPage.set(1);
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
