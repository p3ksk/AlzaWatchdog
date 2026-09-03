import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AccountService } from '../../core/account.service';

/**
 * The one thing a first-time visitor must be told: the address bar is the only
 * copy of their account, so a closed tab is a lost account.
 *
 * Dismissing it writes a flag to localStorage. That is a boolean about this
 * browser's UI, not the key itself — the key deliberately stays out of storage
 * so that the URL remains the single source of truth.
 */
const DISMISSED_KEY = 'alza-watchdog.bookmark-notice-dismissed';

@Component({
  selector: 'app-bookmark-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <aside class="notice" role="note">
        <span class="text">
          <strong>⚠ Bookmark this page.</strong>
          The address is the only way back — nothing is saved in this browser, so
          closing the tab without it loses your lists for good.
        </span>
        <button type="button" (click)="dismiss()">Got it</button>
      </aside>
    }
  `,
  styleUrl: './bookmark-notice.component.scss',
})
export class BookmarkNoticeComponent {
  private readonly account = inject(AccountService);

  private readonly dismissed = signal(readDismissed());

  // Pointless before an account exists, and misleading on a dead URL.
  protected readonly visible = computed(() => !this.dismissed() && this.account.key() !== null);

  protected dismiss(): void {
    try {
      localStorage.setItem(DISMISSED_KEY, '1');
    } catch {
      // Private browsing can refuse writes; hiding it for this session is enough.
    }
    this.dismissed.set(true);
  }
}

function readDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISSED_KEY) === '1';
  } catch {
    return false;
  }
}
