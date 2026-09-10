interface Point {
  x: number;
  y: number;
}

/**
 * A price is a step function: it holds a value until it changes. So the line is
 * drawn with horizontal and vertical segments only — `M x0 y0 H x1 V y1 …` —
 * where every vertical is a price change and every horizontal is how long that
 * price held. Interpolating between snapshots would invent prices that never
 * existed.
 */
export function steppedPath(points: readonly Point[]): string {
  if (points.length === 0) {
    return '';
  }

  let d = `M${r(points[0].x)},${r(points[0].y)}`;
  for (let i = 1; i < points.length; i++) {
    d += `H${r(points[i].x)}V${r(points[i].y)}`;
  }
  return d;
}

/** The stepped line carried out to `endX`, then closed down to `baselineY`. */
export function steppedAreaPath(points: readonly Point[], baselineY: number, endX: number): string {
  const line = steppedPath(points);
  if (!line) {
    return '';
  }

  const first = points[0];
  return `${line}H${r(endX)}V${r(baselineY)}H${r(first.x)}Z`;
}

function r(n: number): string {
  return n.toFixed(2);
}
