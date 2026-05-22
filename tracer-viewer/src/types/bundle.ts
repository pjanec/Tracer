// tracer-viewer/src/types/bundle.ts

export interface BundleLibraryEntryDto {
  bundleId: string;
  sessionId: string;
  label?: string;
  description?: string;
  tags: string[];
  isArchived: boolean;
  sessionStartUtc: string;
  sessionEndUtc: string;
  builtAtUtc: string;
  lastOpenedAtUtc?: string;
  sizeBytes: number;
}

export interface BundleLibraryListDto {
  entries: BundleLibraryEntryDto[];
}

export interface UpdateBundleMetadataDto {
  label?: string;
  description?: string;
  tags?: string[];
  isArchived?: boolean;
}
