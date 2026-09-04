import {
  ChangeDetectionStrategy, Component, ElementRef, afterNextRender,
  computed, inject, signal, viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { compactGuid } from '../../core/guid';
import { describeError } from '../../core/watchdog-api.service';
import { markBookmarkWarningSeen } from '../shell/bookmark-notice.component';

/** Mirrors AlzaUrl.TryParse on the server; the server remains the authority. */
const ALZA_PRODUCT_URL = /^(https?:\/\/)?(www\.)?alza\.sk\/.+-d\d+\.htm([?#].*)?$/i;

const EXAMPLE_URL = 'https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm';

type Step = 'intro' | 'product' | 'key';

/**
 * The landing page for someone with no account, and the wizard that gives them
 * one.
 *
 * Opening the site deliberately creates nothing. The account is minted together
 * with the first product, so a visitor who wanders off leaves no empty account
 * behind — and the warning about the link being the only way back is saved for
 * the end, when there is finally a link worth keeping.
 */
@Component({
  selector: 'app-welcome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './welcome.component.html',
  styleUrl: './welcome.component.scss',
})
export class WelcomeComponent {
  private readonly account = inject(AccountService);
  private readonly router = inject(Router);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly exampleUrl = EXAMPLE_URL;

  protected readonly step = signal<Step>('intro');
  protected readonly url = signal('');
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly copied = signal(false);
  protected readonly addedName = signal<string | null>(null);

  protected readonly accountUrl = computed(() => {
    const key = this.account.key();
    return key ? `${location.origin}/user/${compactGuid(key)}` : null;
  });

  protected readonly stepNumber = computed(() =>
    ({ intro: 1, product: 2, key: 3 })[this.step()],
  );

  constructor() {
    // The wizard is the page's whole purpose, so it opens by itself rather than
    // hiding behind a button someone has to find.
    afterNextRender(() => this.open());
  }

  protected open(): void {
    this.dialog().nativeElement.showModal();
  }

  /** Only the first two steps may be dismissed; the last one has a real link to protect. */
  protected cancel(event: Event): void {
    if (this.step() === 'key') {
      event.preventDefault();
    }
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  protected useExample(): void {
    this.url.set(this.exampleUrl);
    this.error.set(null);
  }

  protected toProduct(): void {
    this.error.set(null);
    this.step.set('product');
  }

  protected async submit(): Promise<void> {
    const url = this.url().trim();
    if (!url) {
      return;
    }

    if (!ALZA_PRODUCT_URL.test(url)) {
      this.error.set('Not an alza.sk product page. A product link ends in a code, like …-d10818009.htm');
      return;
    }

    this.working.set(true);
    this.error.set(null);

    try {
      const { lists } = await this.account.startWithFirstProduct(url);
      this.addedName.set(lists[0]?.name ?? null);

      // The wizard delivers the warning itself, so the banner would only repeat it.
      markBookmarkWarningSeen();
      this.step.set('key');
    } catch (error) {
      this.error.set(describeError(error, 'Could not start with that product.'));
    } finally {
      this.working.set(false);
    }
  }

  protected async copy(): Promise<void> {
    const value = this.accountUrl();
    if (!value) {
      return;
    }

    try {
      await navigator.clipboard.writeText(value);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.error.set('Your browser blocked clipboard access — select the link and copy it manually.');
    }
  }

  protected async finish(): Promise<void> {
    const key = this.account.key();
    const list = this.account.lists()[0];
    if (!key || !list) {
      return;
    }

    this.close();
    await this.router.navigate(['/user', compactGuid(key), 'list', compactGuid(list.id)]);
  }
}
