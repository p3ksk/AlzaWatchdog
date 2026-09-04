export interface PriceSnapshot {
  price: number | null;
  plusPrice: number | null;
  couponPrice: number | null;
  availability: string | null;
  capturedAt: string;
}

export interface TrackedItem {
  id: string;
  productCode: string;
  url: string;
  name: string | null;
  /** Product image as a cached data URI, so the browser never hits the CDN directly. */
  imageDataUri: string | null;
  currentPrice: number | null;
  previousPrice: number | null;
  /** Cheaper price for AlzaPlus+ members, when this product offers one. */
  plusPrice: number | null;
  /** Price with a discount code applied, when this product offers one. */
  couponPrice: number | null;
  lowestPrice: number | null;
  highestPrice: number | null;
  currency: string | null;
  availability: string | null;
  lastCheckedAt: string | null;
  lastError: string | null;
  isActive: boolean;
  /** Position in the user's manual order. Zero-based; ties fall back to creation order. */
  sortOrder: number;
  createdAt: string;
  /** Oldest first. Enough to draw the card's chart without a second request. */
  history: PriceSnapshot[];
  /** Positive when the price rose, negative on a drop, null when unknown. */
  priceChange: number | null;
  /** The current price ties the cheapest ever recorded. */
  isAtLowest: boolean;
}

export interface WatchList {
  id: string;
  name: string;
  createdAt: string;
  itemCount: number;
}

export interface WatchListDetail {
  id: string;
  name: string;
  items: TrackedItem[];
}

export interface AccountResponse {
  userId: string;
  /** Whether this account holds an AlzaPlus+ membership. */
  hasAlzaPlus: boolean;
  /** Whether this key is listed under Admin:Keys in server configuration. */
  isAdmin: boolean;
  lists: WatchList[];
}

/** RFC 7807 body returned by the API for every failure. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

// --- Admin ----------------------------------------------------------------

export interface AdminStats {
  users: number;
  lists: number;
  items: number;
  distinctProducts: number;
  snapshots: number;
  inactiveItems: number;
}

export interface AdminUser {
  id: string;
  hasAlzaPlus: boolean;
  isAdmin: boolean;
  listCount: number;
  itemCount: number;
  snapshotCount: number;
  createdAt: string;
  lastSeenAt: string;
}

export interface AdminItem {
  id: string;
  userId: string;
  listId: string;
  listName: string;
  productCode: string;
  url: string;
  name: string | null;
  currency: string | null;
  lastPrice: number | null;
  lastPlusPrice: number | null;
  lastCouponPrice: number | null;
  lastAvailability: string | null;
  lastCheckedAt: string | null;
  /** Estimated next sweep time; null when the item is paused. */
  nextCheckAt: string | null;
  lastError: string | null;
  consecutiveFailures: number;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
  snapshots: PriceSnapshot[];
}

export interface ImportResult {
  itemsImported: number;
  itemsSkipped: number;
  snapshotsImported: number;
  notes: string[];
}

/** Result of an admin restoring whole accounts, as opposed to one user's products. */
export interface AccountImportResult {
  accountsImported: number;
  accountsSkipped: number;
  listsImported: number;
  itemsImported: number;
  snapshotsImported: number;
  notes: string[];
}

export interface AdminWorkerSetting {
  label: string;
  value: string;
  /** Explanation shown on hover, written by whichever worker reports the value. */
  hint: string | null;
}

export interface AdminWorker {
  name: string;
  description: string;
  enabled: boolean;
  /** The worker has not completed a pass yet this process. */
  idle: boolean;
  lastRunAt: string | null;
  nextRunAt: string | null;
  lastOutcome: string | null;
  runs: number;
  settings: AdminWorkerSetting[];
  /** Live queue depths, gathered per request rather than cached by the worker. */
  now: AdminWorkerSetting[];
}
