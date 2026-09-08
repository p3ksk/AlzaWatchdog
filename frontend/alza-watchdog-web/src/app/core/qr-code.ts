/**
 * A dependency-free QR Code encoder — just enough of ISO/IEC 18004 to turn a
 * short URL into a grid of dark/light modules for rendering as SVG.
 *
 * This is a trimmed, ES-module port of Project Nayuki's reference
 * implementation (https://www.nayuki.io/page/qr-code-generator-library),
 * used under the MIT License:
 *
 *   Copyright (c) Project Nayuki.
 *
 *   Permission is hereby granted, free of charge, to any person obtaining a
 *   copy of this software and associated documentation files (the "Software"),
 *   to deal in the Software without restriction, including without limitation
 *   the rights to use, copy, modify, merge, publish, distribute, sublicense,
 *   and/or sell copies of the Software, and to permit persons to whom the
 *   Software is furnished to do so, subject to the following conditions:
 *   The above copyright notice and this permission notice shall be included in
 *   all copies or substantial portions of the Software.
 *   The Software is provided "as is", without warranty of any kind.
 *
 * Only what this app needs was kept: byte-mode segments (a URL is ASCII/UTF-8),
 * automatic version selection and full mask evaluation.
 */

export type EccLevel = 'L' | 'M' | 'Q' | 'H';

const MIN_VERSION = 1;
const MAX_VERSION = 40;

// Ordinal and format bits for each error-correction level, keyed by letter.
const ECC: Record<EccLevel, { ordinal: number; formatBits: number }> = {
  L: { ordinal: 0, formatBits: 1 },
  M: { ordinal: 1, formatBits: 0 },
  Q: { ordinal: 2, formatBits: 3 },
  H: { ordinal: 3, formatBits: 2 },
};

/** The encoded symbol: a square grid of modules, `true` meaning dark. */
export class QrCode {
  /** Side length in modules (21 for version 1, +4 per version). */
  readonly size: number;

  /** The mask pattern (0–7) that scored lowest and was applied. */
  readonly mask: number;

  private readonly modules: boolean[][] = [];
  private readonly isFunction: boolean[][] = [];

  /**
   * Encodes the given text as a QR Code, picking the smallest version between
   * `minVersion` and 40 that fits at the requested error-correction level.
   */
  static encodeText(text: string, ecl: EccLevel = 'M', minVersion = 1): QrCode {
    const data = utf8Bytes(text);
    const bb: number[] = [];
    appendBits(0x4, 4, bb); // Byte mode indicator.

    let version = minVersion;
    for (; ; version++) {
      const capacityBits = getNumDataCodewords(version, ecl) * 8;
      const charCountBits = version <= 9 ? 8 : 16;
      const usedBits = 4 + charCountBits + data.length * 8;
      if (usedBits <= capacityBits) {
        break;
      }
      if (version >= MAX_VERSION) {
        throw new RangeError('Text too long to encode as a QR Code');
      }
    }

    appendBits(data.length, version <= 9 ? 8 : 16, bb);
    for (const b of data) {
      appendBits(b, 8, bb);
    }

    const capacityBits = getNumDataCodewords(version, ecl) * 8;
    appendBits(0, Math.min(4, capacityBits - bb.length), bb); // Terminator.
    appendBits(0, (8 - (bb.length % 8)) % 8, bb); // Pad to a byte boundary.
    for (let pad = 0xec; bb.length < capacityBits; pad ^= 0xec ^ 0x11) {
      appendBits(pad, 8, bb);
    }

    const dataCodewords = new Array<number>(bb.length / 8).fill(0);
    bb.forEach((bit, i) => (dataCodewords[i >>> 3] |= bit << (7 - (i & 7))));

    return new QrCode(version, ecl, dataCodewords);
  }

  private constructor(
    readonly version: number,
    readonly errorCorrectionLevel: EccLevel,
    dataCodewords: readonly number[],
  ) {
    if (version < MIN_VERSION || version > MAX_VERSION) {
      throw new RangeError('Version out of range');
    }
    this.size = version * 4 + 17;

    for (let i = 0; i < this.size; i++) {
      this.modules.push(new Array<boolean>(this.size).fill(false));
      this.isFunction.push(new Array<boolean>(this.size).fill(false));
    }

    this.drawFunctionPatterns();
    this.drawCodewords(this.addEccAndInterleave(dataCodewords));

    // Try every mask, keep the one with the lowest penalty score.
    let bestMask = 0;
    let minPenalty = Infinity;
    for (let mask = 0; mask < 8; mask++) {
      this.applyMask(mask);
      this.drawFormatBits(mask);
      const penalty = this.getPenaltyScore();
      if (penalty < minPenalty) {
        bestMask = mask;
        minPenalty = penalty;
      }
      this.applyMask(mask); // Undo (XOR is its own inverse).
    }
    this.mask = bestMask;
    this.applyMask(bestMask);
    this.drawFormatBits(bestMask);
  }

  /** Returns whether the module at the given coordinates is dark. */
  getModule(x: number, y: number): boolean {
    return x >= 0 && x < this.size && y >= 0 && y < this.size && this.modules[y][x];
  }

  /** The full grid as rows of booleans, `true` meaning dark. */
  toRows(): boolean[][] {
    return this.modules.map((row) => row.slice());
  }

  // --- Function-pattern drawing ------------------------------------------------

  private drawFunctionPatterns(): void {
    for (let i = 0; i < this.size; i++) {
      this.setFunctionModule(6, i, i % 2 === 0);
      this.setFunctionModule(i, 6, i % 2 === 0);
    }

    this.drawFinderPattern(3, 3);
    this.drawFinderPattern(this.size - 4, 3);
    this.drawFinderPattern(3, this.size - 4);

    const alignPositions = this.getAlignmentPatternPositions();
    const numAlign = alignPositions.length;
    for (let i = 0; i < numAlign; i++) {
      for (let j = 0; j < numAlign; j++) {
        // Skip the three corners occupied by finder patterns.
        if (
          !(
            (i === 0 && j === 0) ||
            (i === 0 && j === numAlign - 1) ||
            (i === numAlign - 1 && j === 0)
          )
        ) {
          this.drawAlignmentPattern(alignPositions[i], alignPositions[j]);
        }
      }
    }

    this.drawFormatBits(0); // Dummy; real bits drawn once the mask is chosen.
    this.drawVersion();
  }

  private drawFormatBits(mask: number): void {
    const data = (ECC[this.errorCorrectionLevel].formatBits << 3) | mask;
    let rem = data;
    for (let i = 0; i < 10; i++) {
      rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
    }
    const bits = ((data << 10) | rem) ^ 0x5412;

    for (let i = 0; i <= 5; i++) {
      this.setFunctionModule(8, i, getBit(bits, i));
    }
    this.setFunctionModule(8, 7, getBit(bits, 6));
    this.setFunctionModule(8, 8, getBit(bits, 7));
    this.setFunctionModule(7, 8, getBit(bits, 8));
    for (let i = 9; i < 15; i++) {
      this.setFunctionModule(14 - i, 8, getBit(bits, i));
    }

    for (let i = 0; i < 8; i++) {
      this.setFunctionModule(this.size - 1 - i, 8, getBit(bits, i));
    }
    for (let i = 8; i < 15; i++) {
      this.setFunctionModule(8, this.size - 15 + i, getBit(bits, i));
    }
    this.setFunctionModule(8, this.size - 8, true); // Always-dark module.
  }

  private drawVersion(): void {
    if (this.version < 7) {
      return;
    }

    let rem = this.version;
    for (let i = 0; i < 12; i++) {
      rem = (rem << 1) ^ ((rem >>> 11) * 0x1f25);
    }
    const bits = (this.version << 12) | rem;

    for (let i = 0; i < 18; i++) {
      const bit = getBit(bits, i);
      const a = this.size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      this.setFunctionModule(a, b, bit);
      this.setFunctionModule(b, a, bit);
    }
  }

  private drawFinderPattern(x: number, y: number): void {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const dist = Math.max(Math.abs(dx), Math.abs(dy));
        const xx = x + dx;
        const yy = y + dy;
        if (xx >= 0 && xx < this.size && yy >= 0 && yy < this.size) {
          this.setFunctionModule(xx, yy, dist !== 2 && dist !== 4);
        }
      }
    }
  }

  private drawAlignmentPattern(x: number, y: number): void {
    for (let dy = -2; dy <= 2; dy++) {
      for (let dx = -2; dx <= 2; dx++) {
        this.setFunctionModule(x + dx, y + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
      }
    }
  }

  private setFunctionModule(x: number, y: number, isDark: boolean): void {
    this.modules[y][x] = isDark;
    this.isFunction[y][x] = true;
  }

  // --- Error correction and codeword placement -------------------------------

  private addEccAndInterleave(data: readonly number[]): number[] {
    const ver = this.version;
    const ecl = this.errorCorrectionLevel;
    const numBlocks = NUM_ERROR_CORRECTION_BLOCKS[ECC[ecl].ordinal][ver];
    const blockEccLen = ECC_CODEWORDS_PER_BLOCK[ECC[ecl].ordinal][ver];
    const rawCodewords = Math.floor(getNumRawDataModules(ver) / 8);
    const numShortBlocks = numBlocks - (rawCodewords % numBlocks);
    const shortBlockLen = Math.floor(rawCodewords / numBlocks);

    const blocks: number[][] = [];
    const rsDiv = reedSolomonComputeDivisor(blockEccLen);
    for (let i = 0, k = 0; i < numBlocks; i++) {
      const datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
      const dat = data.slice(k, k + datLen);
      k += datLen;
      const ecc = reedSolomonComputeRemainder(dat, rsDiv);
      if (i < numShortBlocks) {
        dat.push(0);
      }
      blocks.push(dat.concat(ecc));
    }

    const result: number[] = [];
    for (let i = 0; i < blocks[0].length; i++) {
      blocks.forEach((block, j) => {
        // The padding byte added to short blocks is skipped in interleaving.
        if (i !== shortBlockLen - blockEccLen || j >= numShortBlocks) {
          result.push(block[i]);
        }
      });
    }
    return result;
  }

  private drawCodewords(data: readonly number[]): void {
    let i = 0;
    for (let right = this.size - 1; right >= 1; right -= 2) {
      if (right === 6) {
        right = 5;
      }
      for (let vert = 0; vert < this.size; vert++) {
        for (let j = 0; j < 2; j++) {
          const x = right - j;
          const upward = ((right + 1) & 2) === 0;
          const y = upward ? this.size - 1 - vert : vert;
          if (!this.isFunction[y][x] && i < data.length * 8) {
            this.modules[y][x] = getBit(data[i >>> 3], 7 - (i & 7));
            i++;
          }
        }
      }
    }
  }

  // --- Masking and scoring --------------------------------------------------

  private applyMask(mask: number): void {
    for (let y = 0; y < this.size; y++) {
      for (let x = 0; x < this.size; x++) {
        let invert: boolean;
        switch (mask) {
          case 0:
            invert = (x + y) % 2 === 0;
            break;
          case 1:
            invert = y % 2 === 0;
            break;
          case 2:
            invert = x % 3 === 0;
            break;
          case 3:
            invert = (x + y) % 3 === 0;
            break;
          case 4:
            invert = (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0;
            break;
          case 5:
            invert = ((x * y) % 2) + ((x * y) % 3) === 0;
            break;
          case 6:
            invert = (((x * y) % 2) + ((x * y) % 3)) % 2 === 0;
            break;
          case 7:
            invert = (((x + y) % 2) + ((x * y) % 3)) % 2 === 0;
            break;
          default:
            throw new RangeError('Mask out of range');
        }
        if (!this.isFunction[y][x] && invert) {
          this.modules[y][x] = !this.modules[y][x];
        }
      }
    }
  }

  private getPenaltyScore(): number {
    let result = 0;
    const size = this.size;
    const mods = this.modules;

    // Adjacent modules in rows / columns having the same colour.
    for (let y = 0; y < size; y++) {
      let runColor = false;
      let runX = 0;
      const runHistory = [0, 0, 0, 0, 0, 0, 0];
      for (let x = 0; x < size; x++) {
        if (mods[y][x] === runColor) {
          runX++;
          if (runX === 5) {
            result += 3;
          } else if (runX > 5) {
            result++;
          }
        } else {
          this.finderPenaltyAddHistory(runX, runHistory);
          if (!runColor) {
            result += this.finderPenaltyCountPatterns(runHistory) * 40;
          }
          runColor = mods[y][x];
          runX = 1;
        }
      }
      result += this.finderPenaltyTerminateAndCount(runColor, runX, runHistory) * 40;
    }
    for (let x = 0; x < size; x++) {
      let runColor = false;
      let runY = 0;
      const runHistory = [0, 0, 0, 0, 0, 0, 0];
      for (let y = 0; y < size; y++) {
        if (mods[y][x] === runColor) {
          runY++;
          if (runY === 5) {
            result += 3;
          } else if (runY > 5) {
            result++;
          }
        } else {
          this.finderPenaltyAddHistory(runY, runHistory);
          if (!runColor) {
            result += this.finderPenaltyCountPatterns(runHistory) * 40;
          }
          runColor = mods[y][x];
          runY = 1;
        }
      }
      result += this.finderPenaltyTerminateAndCount(runColor, runY, runHistory) * 40;
    }

    // 2x2 blocks of the same colour.
    for (let y = 0; y < size - 1; y++) {
      for (let x = 0; x < size - 1; x++) {
        const c = mods[y][x];
        if (c === mods[y][x + 1] && c === mods[y + 1][x] && c === mods[y + 1][x + 1]) {
          result += 3;
        }
      }
    }

    // Balance of dark and light modules.
    let dark = 0;
    for (const row of mods) {
      for (const cell of row) {
        if (cell) {
          dark++;
        }
      }
    }
    const total = size * size;
    const k = Math.ceil(Math.abs(dark * 20 - total * 10) / total) - 1;
    result += k * 10;
    return result;
  }

  private finderPenaltyCountPatterns(runHistory: readonly number[]): number {
    const n = runHistory[1];
    const core =
      n > 0 &&
      runHistory[2] === n &&
      runHistory[3] === n * 3 &&
      runHistory[4] === n &&
      runHistory[5] === n;
    return (
      (core && runHistory[0] >= n * 4 && runHistory[6] >= n ? 1 : 0) +
      (core && runHistory[6] >= n * 4 && runHistory[0] >= n ? 1 : 0)
    );
  }

  private finderPenaltyTerminateAndCount(
    currentRunColor: boolean,
    currentRunLength: number,
    runHistory: number[],
  ): number {
    let runLength = currentRunLength;
    if (currentRunColor) {
      this.finderPenaltyAddHistory(runLength, runHistory);
      runLength = 0;
    }
    runLength += this.size;
    this.finderPenaltyAddHistory(runLength, runHistory);
    return this.finderPenaltyCountPatterns(runHistory);
  }

  private finderPenaltyAddHistory(currentRunLength: number, runHistory: number[]): void {
    if (runHistory[0] === 0) {
      currentRunLength += this.size; // Add light border to the first run.
    }
    runHistory.pop();
    runHistory.unshift(currentRunLength);
  }

  private getAlignmentPatternPositions(): number[] {
    if (this.version === 1) {
      return [];
    }
    const numAlign = Math.floor(this.version / 7) + 2;
    const step =
      this.version === 32
        ? 26
        : Math.ceil((this.version * 4 + 4) / (numAlign * 2 - 2)) * 2;
    const result = [6];
    for (let pos = this.size - 7; result.length < numAlign; pos -= step) {
      result.splice(1, 0, pos);
    }
    return result;
  }
}

// --- Module-level helpers ----------------------------------------------------

function appendBits(value: number, len: number, bb: number[]): void {
  for (let i = len - 1; i >= 0; i--) {
    bb.push((value >>> i) & 1);
  }
}

function getBit(value: number, i: number): boolean {
  return ((value >>> i) & 1) !== 0;
}

function utf8Bytes(str: string): number[] {
  return Array.from(new TextEncoder().encode(str));
}

function getNumRawDataModules(ver: number): number {
  let result = (16 * ver + 128) * ver + 64;
  if (ver >= 2) {
    const numAlign = Math.floor(ver / 7) + 2;
    result -= (25 * numAlign - 10) * numAlign - 55;
    if (ver >= 7) {
      result -= 36;
    }
  }
  return result;
}

function getNumDataCodewords(ver: number, ecl: EccLevel): number {
  return (
    Math.floor(getNumRawDataModules(ver) / 8) -
    ECC_CODEWORDS_PER_BLOCK[ECC[ecl].ordinal][ver] *
      NUM_ERROR_CORRECTION_BLOCKS[ECC[ecl].ordinal][ver]
  );
}

function reedSolomonComputeDivisor(degree: number): number[] {
  if (degree < 1 || degree > 255) {
    throw new RangeError('Degree out of range');
  }
  const result = new Array<number>(degree).fill(0);
  result[degree - 1] = 1;

  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < result.length; j++) {
      result[j] = reedSolomonMultiply(result[j], root);
      if (j + 1 < result.length) {
        result[j] ^= result[j + 1];
      }
    }
    root = reedSolomonMultiply(root, 0x02);
  }
  return result;
}

function reedSolomonComputeRemainder(data: readonly number[], divisor: readonly number[]): number[] {
  const result = new Array<number>(divisor.length).fill(0);
  for (const b of data) {
    const factor = b ^ result.shift()!;
    result.push(0);
    divisor.forEach((coef, i) => (result[i] ^= reedSolomonMultiply(coef, factor)));
  }
  return result;
}

function reedSolomonMultiply(x: number, y: number): number {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z & 0xff;
}

// Version: index 0 is padding and holds an illegal value.
const ECC_CODEWORDS_PER_BLOCK: readonly (readonly number[])[] = [
  // 0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40
  [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // L
  [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28], // M
  [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // Q
  [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30], // H
];

const NUM_ERROR_CORRECTION_BLOCKS: readonly (readonly number[])[] = [
  // 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40
  [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25], // L
  [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49], // M
  [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68], // Q
  [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81], // H
];
