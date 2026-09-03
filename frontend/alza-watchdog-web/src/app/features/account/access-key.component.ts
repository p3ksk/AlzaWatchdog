import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { compactGuid, keyFromInput } from '../../core/guid';

/**
 * The account key lives in the address bar and nowhere else — nothing is stored
 * in this browser. That is what makes a bookmark the way back in, and also means
 * this dialog is the last chance to copy the link before it is lost, which is
 * why it says so rather than politely showing a field.
 */
@Component({
  selector: 'app-access-key',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './access-key.component.html',
  styleUrl: './access-key.component.scss',
})
export class AccessKeyComponent {
  private readonly account = inject(AccountService);
  private readonly router = inject(Router);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly key = this.account.key;
  protected readonly copied = signal(false);
  protected readonly candidate = signal('');
  protected readonly error = signal<string | null>(null);

  protected readonly accountUrl = computed(() => {
    const key = this.key();
    return key ? `${location.origin}/user/${compactGuid(key)}` : null;
  });

  open(): void {
    this.reset();
    this.dialog().nativeElement.showModal();
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  /** A native dialog's backdrop clicks land on the dialog element itself. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }

  protected reset(): void {
    this.copied.set(false);
    this.candidate.set('');
    this.error.set(null);
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

  /** Opening another account is just navigation; there is no stored session to swap. */
  protected async openAccount(): Promise<void> {
    const raw = this.candidate().trim();
    if (!raw) {
      return;
    }

    // Accepts a whole pasted URL as readily as a bare key, in either GUID form.
    const key = keyFromInput(raw);

    this.error.set(null);
    this.close();
    await this.router.navigate(['/user', compactGuid(key)]);
  }
}
