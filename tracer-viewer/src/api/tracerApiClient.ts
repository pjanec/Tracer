// tracer-viewer/src/api/tracerApiClient.ts
// Hand-authored stub matching the backend DTOs

export interface SessionDto {
  sessionId: string;
  scenarioId: string;
  label?: string;
  startUtc: string;
  endUtc?: string;
  status: string;
  participatingNodes: string[];
  eventCount: number;
}

export interface ScenarioPhaseDto {
  phaseName: string;
  startedAtUtc: string;
  endedAtUtc?: string;
  status: string;
}

export interface NotableEventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  notableLabel: string;
  severity?: string;
  entityId?: string;
  scenarioPhase?: string;
  payloadJson?: string;
}

export interface EventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
  publisherNode: string;
}

export interface ScenarioStateDto {
  currentPhase: string;
  sessionElapsed: string;
  totalEvents: number;
  totalNotables: number;
}

export interface NodeInfoDto {
  nodeId: string;
  eventsPublished: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

export interface TopologyDto {
  nodes: NodeInfoDto[];
  asOfUtc: string;
}

export interface LiveStatusDto {
  ingestedTotal: number;
  connectedSources: number;
  currentInterval: string;
}

export class TracerApiClient {
  async listSessions(from?: string, to?: string): Promise<SessionDto[]> {
    const params = new URLSearchParams();
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    const query = params.toString() ? `?${params.toString()}` : '';
    const res = await fetch(`/api/sessions${query}`);
    if (!res.ok) throw new Error(`listSessions: ${res.status}`);
    return res.json() as Promise<SessionDto[]>;
  }

  async getSession(sessionId: string): Promise<SessionDto | null> {
    const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getSession: ${res.status}`);
    return res.json() as Promise<SessionDto>;
  }

  async getScenarioPhases(sessionId: string): Promise<ScenarioPhaseDto[]> {
    const res = await fetch(`/api/scenario/phases?sessionId=${encodeURIComponent(sessionId)}`);
    if (!res.ok) throw new Error(`getScenarioPhases: ${res.status}`);
    return res.json() as Promise<ScenarioPhaseDto[]>;
  }

  async getScenarioNotables(
    sessionId: string,
    limit?: number,
    before?: string,
  ): Promise<NotableEventDto[]> {
    const params = new URLSearchParams({ sessionId });
    if (limit !== undefined) params.set('limit', String(limit));
    if (before) params.set('before', before);
    const res = await fetch(`/api/scenario/notables?${params.toString()}`);
    if (!res.ok) throw new Error(`getScenarioNotables: ${res.status}`);
    return res.json() as Promise<NotableEventDto[]>;
  }

  async getScenarioState(sessionId: string): Promise<ScenarioStateDto> {
    const res = await fetch(`/api/scenario/state?sessionId=${encodeURIComponent(sessionId)}`);
    if (!res.ok) throw new Error(`getScenarioState: ${res.status}`);
    return res.json() as Promise<ScenarioStateDto>;
  }

  async getEvent(eventId: string): Promise<EventDto | null> {
    const res = await fetch(`/api/events/${encodeURIComponent(eventId)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getEvent: ${res.status}`);
    return res.json() as Promise<EventDto>;
  }

  async getTopology(): Promise<TopologyDto> {
    const res = await fetch('/api/topology');
    if (!res.ok) throw new Error(`getTopology: ${res.status}`);
    return res.json() as Promise<TopologyDto>;
  }

  async getLiveStatus(): Promise<LiveStatusDto> {
    const res = await fetch('/api/live/status');
    if (!res.ok) throw new Error(`getLiveStatus: ${res.status}`);
    return res.json() as Promise<LiveStatusDto>;
  }
}

export const api = new TracerApiClient();
