import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AccountService } from '../../core/account.service';
import { ThemeService } from '../../core/theme.service';
import { describeError } from '../../core/watchdog-api.service';
import { compactGuid } from '../../core/guid';

/**
 * The single bar across the top: identity, the lists, and the two actions.
 *
 * Each list is a plain link to its own address, which is the same URL a person
 * bookmarks — there is no separate "current list" state to keep in step with the
 * router.
 */
@Component({
  selector: 'app-navbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.scss',
})
export class NavbarComponent {
  private readonly account = inject(AccountService);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  readonly activeListId = input<string | null>(null);

  readonly watchRequested = output<void>();
  readonly keysRequested = output<void>();
  readonly backupRequested = output<void>();

  private readonly menu = viewChild<ElementRef<HTMLDetailsElement>>('menu');

  protected readonly lists = this.account.lists;
  protected readonly accountKey = this.account.key;
  protected readonly hasAlzaPlus = this.account.hasAlzaPlus;
  protected readonly isAdmin = this.account.isAdmin;
  protected readonly isDark = this.theme.isDark;

  protected readonly creating = signal(false);
  protected readonly newName = signal('');
  protected readonly working = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly canAct = computed(() => this.accountKey() !== null && this.lists().length > 0);

  protected listLink(listId: string): unknown[] {
    return ['/user', compactGuid(this.accountKey()), 'list', compactGuid(listId)];
  }

  protected adminLink(): unknown[] {
    return ['/user', compactGuid(this.accountKey()), 'admin'];
  }

  protected closeMenu(): void {
    this.menu()?.nativeElement.removeAttribute('open');
  }

  protected toggleTheme(): void {
    this.theme.toggle();
  }

  protected async toggleAlzaPlus(checked: boolean): Promise<void> {
    this.error.set(null);

    try {
      await this.account.setHasAlzaPlus(checked);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not save that setting.');
    }
  }

  protected startCreate(): void {
    this.error.set(null);
    this.newName.set('');
    this.creating.set(true);
  }

  protected cancelCreate(): void {
    this.creating.set(false);
  }

  protected async create(): Promise<void> {
    const name = this.newName().trim();
    if (!name) {
      return;
    }

    this.working.set(true);
    this.error.set(null);

    try {
      const list = await this.account.createList(name);
      this.creating.set(false);
      // Land on the new list straight away — creating one and then having to
      // click it would be a pointless second step.
      await this.router.navigate(this.listLink(list.id));
    } catch (error) {
      this.error.set(describeError(error, 'Could not create that list.'));
    } finally {
      this.working.set(false);
    }
  }
}
