import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ItemsStore } from './core/items.store';
import { AddItemDialogComponent } from './features/items/add-item-dialog.component';
import { AccessKeyComponent } from './features/account/access-key.component';
import { BackupDialogComponent } from './features/account/backup-dialog.component';
import { NavbarComponent } from './features/shell/navbar.component';
import { BookmarkNoticeComponent } from './features/shell/bookmark-notice.component';

/**
 * Owns the navbar and the two popups it opens. Keeping the dialogs here rather
 * than inside the routed page means they survive a list switch and there is only
 * ever one of each in the document.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    NavbarComponent,
    BookmarkNoticeComponent,
    AddItemDialogComponent,
    AccessKeyComponent,
    BackupDialogComponent,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly store = inject(ItemsStore);

  private readonly addDialog = viewChild.required(AddItemDialogComponent);
  private readonly keysDialog = viewChild.required(AccessKeyComponent);
  private readonly backupDialog = viewChild.required(BackupDialogComponent);

  protected readonly activeListId = computed(() => this.store.listId());

  protected openAdd(): void {
    this.addDialog().open();
  }

  protected openKeys(): void {
    this.keysDialog().open();
  }

  protected openBackup(): void {
    this.backupDialog().open();
  }
}
