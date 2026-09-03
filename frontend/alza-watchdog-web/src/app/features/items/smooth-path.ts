interface Point {
  x: number;
  y: number;
}

/**
 * Builds a smooth SVG path through the given points using monotone cubic
 * interpolation (Fritsch–Carlson).
 *
 * A plain Catmull-Rom spline would be simpler, but it overshoots: a run of
 * falling prices would be drawn dipping below the lowest price ever recorded,
 * and the chart would contradict the "LO" figure printed underneath it. Monotone
 * interpolation is guaranteed never to leave the range of the data, so the curve
 * stays honest while still being smooth.
 */
export function monotoneCubicPath(points: readonly Point[]): string {
  if (points.length < 2) {
    return '';
  }

  const n = points.length;
  const slopes = tangents(points);

  let path = `M${points[0].x.toFixed(2)},${points[0].y.toFixed(2)}`;

  for (let i = 0; i < n - 1; i++) {
    const p0 = points[i];
    const p1 = points[i + 1];
    const h = (p1.x - p0.x) / 3;

    const c1x = p0.x + h;
    const c1y = p0.y + slopes[i] * h;
    const c2x = p1.x - h;
    const c2y = p1.y - slopes[i + 1] * h;

    path +=
      ` C${c1x.toFixed(2)},${c1y.toFixed(2)}` +
      ` ${c2x.toFixed(2)},${c2y.toFixed(2)}` +
      ` ${p1.x.toFixed(2)},${p1.y.toFixed(2)}`;
  }

  return path;
}

/** Per-point tangents, clamped so the resulting curve cannot overshoot. */
function tangents(points: readonly Point[]): number[] {
  const n = points.length;
  const secants: number[] = [];

  for (let i = 0; i < n - 1; i++) {
    const dx = points[i + 1].x - points[i].x;
    secants.push(dx === 0 ? 0 : (points[i + 1].y - points[i].y) / dx);
  }

  const slopes: number[] = new Array(n);
  slopes[0] = secants[0];
  slopes[n - 1] = secants[n - 2];

  for (let i = 1; i < n - 1; i++) {
    // A local extremum must be flat, otherwise the curve bulges past it.
    slopes[i] = secants[i - 1] * secants[i] <= 0 ? 0 : (secants[i - 1] + secants[i]) / 2;
  }

  // Fritsch–Carlson: keep each tangent within three times its neighbouring
  // secants, which is the condition for the segment to stay monotone.
  for (let i = 0; i < n - 1; i++) {
    if (secants[i] === 0) {
      slopes[i] = 0;
      slopes[i + 1] = 0;
      continue;
    }

    const a = slopes[i] / secants[i];
    const b = slopes[i + 1] / secants[i];
    const magnitude = Math.hypot(a, b);

    if (magnitude > 3) {
      const scale = 3 / magnitude;
      slopes[i] = scale * a * secants[i];
      slopes[i + 1] = scale * b * secants[i];
    }
  }

  return slopes;
}
