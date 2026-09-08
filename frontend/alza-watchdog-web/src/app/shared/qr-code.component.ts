import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { EccLevel, QrCode } from '../core/qr-code';

/**
 * Renders a string as a QR Code, drawn as one crisp SVG path so it scales to
 * any size without blurring. Colours are fixed dark-on-light rather than
 * themed: an inverted or low-contrast code is a code some scanners refuse.
 */
@Component({
  selector: 'app-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.viewBox]="'0 0 ' + extent() + ' ' + extent()"
      [attr.aria-label]="label()"
      role="img"
      xmlns="http://www.w3.org/2000/svg"
      shape-rendering="crispEdges"
    >
      <rect [attr.width]="extent()" [attr.height]="extent()" fill="#f4ecd8" />
      <path [attr.d]="path()" fill="#1b160e" />
    </svg>
  `,
  styleUrl: './qr-code.component.scss',
})
export class QrCodeComponent {
  /** The text to encode — typically a URL. */
  readonly value = input.required<string>();

  /** Error-correction level; higher tolerates more damage at the cost of density. */
  readonly ecc = input<EccLevel>('M');

  /** Quiet-zone width in modules. The spec asks for 4; scanners need it. */
  readonly quietZone = input(4);

  readonly label = input('QR code');

  private readonly code = computed(() => QrCode.encodeText(this.value(), this.ecc()));

  /** Side length of the whole drawing, code plus quiet zone on both sides. */
  protected readonly extent = computed(() => this.code().size + this.quietZone() * 2);

  protected readonly path = computed(() => {
    const code = this.code();
    const offset = this.quietZone();
    let d = '';
    for (let y = 0; y < code.size; y++) {
      for (let x = 0; x < code.size; x++) {
        if (code.getModule(x, y)) {
          d += `M${x + offset} ${y + offset}h1v1h-1z`;
        }
      }
    }
    return d;
  });
}
