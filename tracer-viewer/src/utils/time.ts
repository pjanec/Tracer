export function formatTime(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function formatDuration(iso: string): string {
  return iso;
}
