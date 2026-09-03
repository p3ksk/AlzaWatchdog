/**
 * URLs carry GUIDs in compact form — 32 hex digits, no dashes — because a
 * bookmarkable address that is already long does not need four more separators.
 *
 * The API always emits and accepts the canonical dashed form (.NET's Guid.TryParse
 * takes either), so the app keeps dashed ids internally and converts only at the
 * two edges: building a link, and reading a route parameter.
 */

const COMPACT = /^[0-9a-f]{32}$/i;
const DASHED = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Dashed id → the form that goes in a URL. */
export function compactGuid(id: string | null | undefined): string {
  return (id ?? '').replaceAll('-', '');
}

/**
 * Route parameter → the canonical dashed form the rest of the app compares
 * against. Anything that is not a bare 32-hex string is passed through untouched,
 * so a hand-edited or already-dashed URL still reaches the API and gets a proper
 * 401/404 rather than being mangled here.
 */
export function expandGuid(value: string | null | undefined): string {
  const raw = (value ?? '').trim();

  if (DASHED.test(raw)) {
    return raw.toLowerCase();
  }

  if (!COMPACT.test(raw)) {
    return raw;
  }

  return [
    raw.slice(0, 8),
    raw.slice(8, 12),
    raw.slice(12, 16),
    raw.slice(16, 20),
    raw.slice(20),
  ]
    .join('-')
    .toLowerCase();
}

/**
 * Pulls a key out of whatever someone pasted — a bare key in either form, or a
 * whole URL copied from the address bar.
 */
export function keyFromInput(raw: string): string {
  const trimmed = raw.trim();
  const candidate = trimmed.includes('/')
    ? (trimmed.split(/[/?#]/).filter(Boolean).pop() ?? trimmed)
    : trimmed;

  return expandGuid(candidate);
}
