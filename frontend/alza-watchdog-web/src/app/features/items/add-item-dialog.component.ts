import { ChangeDetectionStrategy, Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ItemsStore } from '../../core/items.store';

/**
 * Mirrors AlzaUrl.TryParse on the server so an obvious mistake is caught before
 * it costs a round trip. The server remains the authority.
 */
const ALZA_PRODUCT_URL = /^(https?:\/\/)?(www\.)?alza\.sk\/.+-d\d+\.htm([?#].*)?$/i;

export const EXAMPLE_URL = 'https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm';

@Component({
  selector: 'app-add-item-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './add-item-dialog.component.html',
  styleUrl: './add-item-dialog.component.scss',
})
export class AddItemDialogComponent {
  private readonly store = inject(ItemsStore);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  readonly added = output<void>();

  protected readonly exampleUrl = EXAMPLE_URL;
  protected readonly listName = this.store.listName;

  protected readonly url = signal('');
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly addedName = signal<string | null>(null);

  open(): void {
    this.reset();
    this.dialog().nativeElement.showModal();
    this.field()?.nativeElement.focus();
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
    this.url.set('');
    this.error.set(null);
    this.addedName.set(null);
    this.working.set(false);
  }

  protected useExample(): void {
    this.url.set(this.exampleUrl);
    this.error.set(null);
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
    this.addedName.set(null);

    const failure = await this.store.add(url);

    if (failure) {
      this.error.set(failure);
    } else {
      // The dialog stays open with the field cleared: adding several products in
      // one sitting is the common case, and reopening it each time is friction.
      this.addedName.set(this.store.items()[0]?.name ?? 'Product');
      this.url.set('');
      this.added.emit();
      this.field()?.nativeElement.focus();
    }

    this.working.set(false);
  }
}
