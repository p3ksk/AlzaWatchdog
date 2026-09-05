import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';

/**
 * Wraps a label in a hover/focus explanation. A real element rather than `title`,
 * which is slow, unstyleable and invisible to a keyboard.
 *
 * Only one `<ng-content>`: projected content has a single home, so one in each
 * branch of an `@if` leaves the label to vanish into the branch not rendered.
 */
@Component({
  selector: 'app-tip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="tip"
      [class.bare]="!text()"
      [class.plain]="!underline()"
      [class.flip]="flip()"
      [class.below]="below()"
      [attr.tabindex]="text() ? 0 : null"
      (pointerenter)="place($event.target)"
      (focus)="place($event.target)"
    ><ng-content />@if (text()) {<span class="bubble" role="tooltip">{{ text() }}</span>}</span>
  `,
  styleUrl: './tip.component.scss',
})
export class TipComponent {
  readonly text = input<string | null | undefined>(null);

  /** Off when the wrapped element already looks interactive, such as a badge. */
  readonly underline = input(true);

  /**
   * Opens downwards. Needed inside a scrolling container: a table that scrolls
   * sideways clips vertically too, so a bubble above the header row is invisible.
   */
  readonly below = input(false);

  protected readonly flip = signal(false);

  /** Anchors to whichever edge keeps the bubble on the page. */
  protected place(target: EventTarget | null): void {
    const tip = target as HTMLElement | null;
    const bubble = tip?.querySelector('.bubble');
    if (!tip || !bubble) return;

    // From the anchor, not the bubble: a flipped bubble has already moved, so
    // measuring it would flip back on every second hover.
    this.flip.set(tip.getBoundingClientRect().left + bubble.scrollWidth + 8 > window.innerWidth);
  }
}
