import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ImportResult } from '../../core/models';
import { WatchdogApi, describeError } from '../../core/watchdog-api.service';

/**
 * Lets any account holder download their watched products and restore them from
 * a backup file. Scoped to the current account by the API, never admin-only.
 */
@Component({
  selector: 'app-backup-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-dialog.component.html',
  styleUrl: './backup-dialog.component.scss',
})
export class BackupDialogComponent {
  private readonly api = inject(WatchdogApi);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly exporting = signal(false);
  protected readonly importing = signal(false);
  protected readonly importResult = signal<ImportResult | null>(null);
  protected readonly importError = signal<string | null>(null);
  protected readonly importText = signal('');

  open(): void {
    this.reset();
    this.dialog().nativeElement.showModal();
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }

  protected reset(): void {
    this.exporting.set(false);
    this.importing.set(false);
    this.importResult.set(null);
    this.importError.set(null);
    this.importText.set('');
  }

  protected async exportProducts(): Promise<void> {
    this.exporting.set(true);

    try {
      const blob = await firstValueFrom(this.api.exportProducts());
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `alza-watchdog-${new Date().toISOString().slice(0, 10)}.json`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.importError.set(describeError(error, 'Could not export.'));
    } finally {
      this.exporting.set(false);
    }
  }

  protected async importSelectedFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file) {
      return;
    }

    try {
      this.importText.set(await file.text());
    } catch {
      this.importText.set('');
    }
  }

  protected async runImport(): Promise<void> {
    const text = this.importText().trim();
    if (!text) {
      return;
    }

    let bundle: unknown;
    try {
      bundle = JSON.parse(text);
    } catch {
      this.importError.set('That file is not valid JSON.');
      return;
    }

    this.importing.set(true);
    this.importResult.set(null);
    this.importError.set(null);

    try {
      const result = await firstValueFrom(this.api.importProducts(bundle));
      this.importResult.set(result);
      this.importText.set('');
    } catch (error) {
      this.importError.set(describeError(error, 'Could not import.'));
    } finally {
      this.importing.set(false);
    }
  }
}
