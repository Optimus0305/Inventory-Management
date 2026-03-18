import { useState, useCallback } from 'react';
import type { Hold } from '../types';

export function useHolds() {
  const [holds, setHolds] = useState<Hold[]>([]);

  const addHold = useCallback((hold: Hold) => {
    setHolds((prev) => [hold, ...prev.filter((h) => h.holdId !== hold.holdId)]);
  }, []);

  const updateHold = useCallback((updated: Hold) => {
    setHolds((prev) =>
      prev.map((h) => (h.holdId === updated.holdId ? updated : h))
    );
  }, []);

  const removeHold = useCallback((holdId: string) => {
    setHolds((prev) => prev.filter((h) => h.holdId !== holdId));
  }, []);

  // Filter to only active holds (non-expired, non-released)
  const activeHolds = holds.filter((h) => h.status === 'active');

  return { holds, activeHolds, addHold, updateHold, removeHold };
}
