// tracer-viewer/src/utils/format.ts

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function formatRelative(utcString: string | undefined | null): string {
  if (!utcString) return '—';
  const delta = Date.now() - new Date(utcString).getTime();
  const sec = Math.floor(delta / 1000);
  if (sec < 60) return `${sec}s ago`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const days = Math.floor(hr / 24);
  return `${days}d ago`;
}

export function formatDateRange(from: string, to: string): string {
  const f = new Date(from);
  const t = new Date(to);
  const date = f.toLocaleDateString();
  const fromTime = f.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const toTime = t.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  return `${date} ${fromTime}–${toTime}`;
}
