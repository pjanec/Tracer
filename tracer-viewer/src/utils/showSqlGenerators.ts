// tracer-viewer/src/utils/showSqlGenerators.ts

function sqlEscape(s: string): string { return s.replace(/'/g, "''"); }

export interface TimelineFilterForSql {
  from: string;
  to: string;
  topic?: string;
  publisherNode?: string;
  subscriberNode?: string;
  traceId?: string;
  entityId?: string;
}

export function timelineFilterToSql(f: TimelineFilterForSql): string {
  const clauses = [
    `publish_wallclock >= TIMESTAMP '${f.from}'`,
    `publish_wallclock < TIMESTAMP '${f.to}'`,
  ];
  if (f.topic) clauses.push(`topic = '${sqlEscape(f.topic)}'`);
  if (f.publisherNode) clauses.push(`publisher_node = '${sqlEscape(f.publisherNode)}'`);
  if (f.subscriberNode) clauses.push(`subscriber_node = '${sqlEscape(f.subscriberNode)}'`);
  if (f.entityId) clauses.push(`entity_id = '${sqlEscape(f.entityId)}'`);
  return `SELECT publish_wallclock, publisher_node, topic, event_id\nFROM events\nWHERE ${clauses.join('\n  AND ')}\nORDER BY publish_wallclock\nLIMIT 1000;`;
}

export function entityHistoryFilterToSql(entityId: string, from: string, to: string): string {
  return `SELECT event_id, topic, publisher_node, publish_wallclock\nFROM events\nWHERE entity_id = '${sqlEscape(entityId)}'\n  AND publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'\nORDER BY publish_wallclock;`;
}

export function latencyFilterToSql(from: string, to: string, topic?: string): string {
  const clauses = [
    `publish_wallclock >= TIMESTAMP '${from}'`,
    `publish_wallclock < TIMESTAMP '${to}'`,
    `publisher_node != subscriber_node`,
  ];
  if (topic) clauses.push(`topic = '${sqlEscape(topic)}'`);
  return `SELECT topic, publisher_node, subscriber_node,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.5) AS p50_ms,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.99) AS p99_ms\nFROM events\nWHERE ${clauses.join('\n  AND ')}\nGROUP BY topic, publisher_node, subscriber_node\nORDER BY p99_ms DESC;`;
}

export function gapFilterToSql(from: string, to: string, topic?: string): string {
  const topicClause = topic ? `\n  AND topic = '${sqlEscape(topic)}'` : '';
  return `SELECT topic, publisher_node, subscriber_node, sequence_number\nFROM events\nWHERE publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'${topicClause}\nORDER BY topic, publisher_node, subscriber_node, sequence_number;`;
}

export function topologyFilterToSql(from: string, to: string): string {
  return `SELECT publisher_node, subscriber_node, topic, COUNT(*) AS message_count\nFROM events\nWHERE publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'\nGROUP BY publisher_node, subscriber_node, topic\nORDER BY message_count DESC;`;
}
