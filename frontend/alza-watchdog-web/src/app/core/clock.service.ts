import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/**
 * A coarse ticking clock so "checked 2 hours ago" labels age on their own.
 * Ten seconds is frequent enough that a label is never visibly stale and rare
 * enough that idle tabs stay quiet.
 */
@Injectable({ providedIn: 'root' })
export class ClockService {
  private readonly _now = signal(Date.now());

  readonly now = this._now.asReadonly();

  constructor() {
    const handle = setInterval(() => this._now.set(Date.now()), 10_000);
    inject(DestroyRef).onDestroy(() => clearInterval(handle));
  }
}
