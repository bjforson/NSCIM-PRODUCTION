"""
Size-aware LRU cache for decoded scanner images.

Cold reads pull raw bytes from Postgres (ASE) or a ~12 MB bundle of .img
files from a network share (FS6000), both of which take tens of seconds
in the worst case. Once decoded, the resulting `AseImage` / `Fs6000Image`
holds uint16 numpy arrays that we're happy to keep in memory for as long
as someone is actively inspecting them.

The cache is:

  - Keyed on (scanner, scan_id) string tuple
  - Size-aware: each entry reports its approximate memory footprint and
    the cache evicts the least-recently-used entry until total footprint
    is under the configured budget
  - Thread-safe via a module-level lock (the inspector routes are
    synchronous, so this is sufficient)
  - Observable via `cache_stats()` which returns hit/miss counters and
    current memory usage
  - Bounded by both entry count and total byte budget — whichever kicks
    in first wins

Defaults (override via env vars):
  - INSPECTOR_CACHE_BUDGET_MB  = 400    (total memory ceiling)
  - INSPECTOR_CACHE_MAX_ENTRIES = 64    (hard entry cap)

Invalidation: never. Scan blobs are immutable once produced.
"""
from __future__ import annotations

import os
import threading
from collections import OrderedDict
from dataclasses import dataclass, field
from typing import Any, Callable, Optional

import numpy as np


def _default_budget_bytes() -> int:
    mb = int(os.environ.get("INSPECTOR_CACHE_BUDGET_MB", "400"))
    return mb * 1024 * 1024


def _default_max_entries() -> int:
    return int(os.environ.get("INSPECTOR_CACHE_MAX_ENTRIES", "64"))


def _estimate_size_bytes(obj: Any, _seen: Optional[set] = None) -> int:
    """
    Approximate memory footprint of any decoded-image data in bytes.

    Recursively walks tuples/lists/dicts/dataclass-like objects and sums
    `np.ndarray.nbytes` for every array it finds. Everything else
    contributes a small constant. Deduplicates by id() so shared arrays
    don't get double-counted. Good enough for LRU accounting.
    """
    if _seen is None:
        _seen = set()
    if id(obj) in _seen:
        return 0
    _seen.add(id(obj))

    if obj is None:
        return 0
    if isinstance(obj, np.ndarray):
        return int(obj.nbytes)
    if isinstance(obj, (bytes, bytearray, memoryview)):
        return len(obj)
    if isinstance(obj, str):
        return 48 + len(obj)
    if isinstance(obj, (int, float, bool)):
        return 28
    if isinstance(obj, (tuple, list, set, frozenset)):
        return 64 + sum(_estimate_size_bytes(e, _seen) for e in obj)
    if isinstance(obj, dict):
        return 240 + sum(
            _estimate_size_bytes(k, _seen) + _estimate_size_bytes(v, _seen)
            for k, v in obj.items()
        )

    # Structured object (dataclass / class instance): walk __dict__ and
    # public attributes for any nested numpy/containers.
    total = 256
    walked = set()
    state = getattr(obj, "__dict__", None)
    if isinstance(state, dict):
        for k, v in state.items():
            walked.add(k)
            total += _estimate_size_bytes(v, _seen)
    for attr in dir(obj):
        if attr.startswith("_") or attr in walked:
            continue
        try:
            val = getattr(obj, attr)
        except Exception:
            continue
        if callable(val):
            continue
        if isinstance(val, np.ndarray):
            total += _estimate_size_bytes(val, _seen)
    return total


@dataclass
class _Entry:
    key: tuple
    value: Any
    size_bytes: int


@dataclass
class CacheStats:
    hits: int = 0
    misses: int = 0
    evictions: int = 0
    entries: int = 0
    bytes_used: int = 0
    budget_bytes: int = 0
    max_entries: int = 0

    def to_dict(self) -> dict:
        return {
            "hits": self.hits,
            "misses": self.misses,
            "evictions": self.evictions,
            "hit_ratio": (self.hits / (self.hits + self.misses)) if (self.hits + self.misses) else 0.0,
            "entries": self.entries,
            "bytes_used": self.bytes_used,
            "bytes_used_mb": round(self.bytes_used / (1024 * 1024), 1),
            "budget_bytes": self.budget_bytes,
            "budget_mb": round(self.budget_bytes / (1024 * 1024), 1),
            "max_entries": self.max_entries,
        }


class _SizedLruCache:
    def __init__(self, budget_bytes: int, max_entries: int):
        self._budget = budget_bytes
        self._max = max_entries
        self._items: "OrderedDict[tuple, _Entry]" = OrderedDict()
        self._bytes = 0
        self._lock = threading.Lock()
        self._hits = 0
        self._misses = 0
        self._evictions = 0

    def get_or_load(self, key: tuple, loader: Callable[[], Any]) -> Any:
        """
        Return the cached value for `key`, or call `loader()` to produce
        and insert it. The loader is invoked OUTSIDE the lock so slow
        filesystem reads don't block other requests for other scans.
        """
        with self._lock:
            hit = self._items.get(key)
            if hit is not None:
                self._items.move_to_end(key)
                self._hits += 1
                return hit.value
            self._misses += 1

        # Load outside the lock
        value = loader()
        size = _estimate_size_bytes(value)

        with self._lock:
            # Another request may have populated the same key while we were loading.
            existing = self._items.get(key)
            if existing is not None:
                self._items.move_to_end(key)
                return existing.value

            self._items[key] = _Entry(key=key, value=value, size_bytes=size)
            self._bytes += size
            self._evict_if_needed()
            return value

    def invalidate(self, key: tuple) -> bool:
        with self._lock:
            entry = self._items.pop(key, None)
            if entry is None:
                return False
            self._bytes -= entry.size_bytes
            return True

    def clear(self):
        with self._lock:
            self._items.clear()
            self._bytes = 0

    def stats(self) -> CacheStats:
        with self._lock:
            return CacheStats(
                hits=self._hits,
                misses=self._misses,
                evictions=self._evictions,
                entries=len(self._items),
                bytes_used=self._bytes,
                budget_bytes=self._budget,
                max_entries=self._max,
            )

    def _evict_if_needed(self):
        """Must be called with the lock held."""
        while (self._bytes > self._budget or len(self._items) > self._max) and self._items:
            _, victim = self._items.popitem(last=False)  # pop oldest
            self._bytes -= victim.size_bytes
            self._evictions += 1


# Module-level singleton; routes import and use `cache`.
cache = _SizedLruCache(
    budget_bytes=_default_budget_bytes(),
    max_entries=_default_max_entries(),
)


def get_cache() -> _SizedLruCache:
    return cache


def cache_stats() -> dict:
    return cache.stats().to_dict()
