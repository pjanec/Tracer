// src/types/filter.ts
// Filter types for the timeline composables, FilterPanel, EventInspector

export type FilterChipType = 'topic' | 'node' | 'traceId' | 'entityId' | 'playerId' | 'severity';

export interface FilterChipValue {
  key:   string;
  label: string;
  value: string;
  type:  FilterChipType;
}
