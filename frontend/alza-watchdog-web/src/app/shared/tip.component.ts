import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';

/**
 * Wraps a label in a hover/focus explanation.
 *
 * The text is a real element rather than a `title` attribute: the native tooltip
 * takes a second to appear, cannot be styled to match the terminal, and never
 * shows on a keyboard focus. Keeping it in the DOM also means a screen reader
 * reads the explanation straight after the label it belongs to.
 *
 * With no text it renders as a bare wrapper, so callers can pass an optional hint
 * without guarding it. There is deliberately only one `<ng-content>`: projected
 * content has a single home, so putting one in each branch of an `@if` leaves the
 * label to vanish into the branch that is not rendered.
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

  protected readonly flip = signal(false);

  /**
   * A bubble on the last column of a grid would run off the screen, so it is
   * measured as it opens and anchored to whichever edge leaves it on the page.
   */
  protected place(target: EventTarget | null): void {
    const tip = target as HTMLElement | null;
    const bubble = tip?.querySelector('.bubble');
    if (!tip || !bubble) return;

    // Measured from the anchor, not from the bubble: the bubble has already moved
    // if this tip is flipped, so measuring it would flip back and forth on every
    // second hover.
    this.flip.set(tip.getBoundingClientRect().left + bubble.scrollWidth + 8 > window.innerWidth);
  }
}
