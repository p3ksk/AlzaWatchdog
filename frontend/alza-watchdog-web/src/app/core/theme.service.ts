import { Injectable, signal } from '@angular/core';

type Theme = 'light' | 'dark';

const STORAGE_KEY = 'alza-watchdog-theme';

/**
 * The user's explicit light/dark choice, applied to the document root as a
 * `data-theme` attribute. Falls back to the OS preference when unset.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly _theme = signal<Theme | null>(this.readStored());

  readonly theme = this._theme.asReadonly();

  /** Whether dark mode is currently effective (explicit choice or OS default). */
  readonly isDark = signal(this.resolveDark(this._theme()));

  constructor() {
    this.apply(this._theme());
  }

  toggle(): void {
    const next: Theme | null = this.isDark() ? 'light' : 'dark';
    this._theme.set(next);
    this.apply(next);
    if (next) {
      localStorage.setItem(STORAGE_KEY, next);
    }
  }

  private readStored(): Theme | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'light' || value === 'dark' ? value : null;
    } catch {
      return null;
    }
  }

  private resolveDark(explicit: Theme | null): boolean {
    if (explicit) {
      return explicit === 'dark';
    }
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  private apply(explicit: Theme | null): void {
    this.isDark.set(this.resolveDark(explicit));
    const root = document.documentElement;
    if (explicit) {
      root.setAttribute('data-theme', explicit);
    } else {
      root.removeAttribute('data-theme');
    }
  }
}
