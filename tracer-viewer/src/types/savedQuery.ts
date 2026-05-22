// tracer-viewer/src/types/savedQuery.ts

export interface SavedQueryParameterDto {
  name: string;
  duckType: string;
  defaultValueText: string;
  description?: string;
}

export interface SavedQueryDto {
  savedQueryId: string;
  label: string;
  description?: string;
  sql: string;
  parameters: SavedQueryParameterDto[];
  tags: string[];
  isBuiltIn: boolean;
  isFavorite: boolean;
  author?: string;
  createdAtUtc: string;
  lastRunAtUtc?: string;
  runCount: number;
}

export interface CreateSavedQueryDto {
  label: string;
  description?: string;
  sql: string;
  parameters?: SavedQueryParameterDto[];
  tags?: string[];
  author?: string;
}

export interface UpdateSavedQueryDto {
  label?: string;
  description?: string;
  sql?: string;
  parameters?: SavedQueryParameterDto[];
  tags?: string[];
}

export interface SavedQueryListDto {
  queries: SavedQueryDto[];
}
