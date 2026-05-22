// tracer-viewer/src/types/sql.ts

export interface SqlColumnInfoDto {
  name: string;
  duckType: string;
}

export interface SqlTableInfoDto {
  name: string;
  columns: SqlColumnInfoDto[];
}

export interface SqlSchemaDto {
  tables: SqlTableInfoDto[];
  refreshedAtUtc: string;
  dialectNotes: string[];
}

export interface SqlExecuteResultDto {
  state: 'Succeeded' | 'Failed' | 'Timeout' | 'Rejected';
  columns?: SqlColumnInfoDto[];
  rows?: (unknown | null)[][];
  errorMessage?: string;
  elapsedMs: number;
  truncated: boolean;
}

export interface SqlExplainResultDto {
  planText: string;
}

export interface ViewSqlTemplateResultDto {
  viewType: string;
  sql: string;
}

export interface SqlExecuteRequestDto {
  sql: string;
  parameters?: Record<string, unknown>;
  timeoutSeconds?: number;
  maxRows?: number;
}

export interface SqlExplainRequestDto {
  sql: string;
}
