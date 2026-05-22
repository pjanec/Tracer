import { describe, it, expect } from 'vitest';
import {
  timelineFilterToSql,
  entityHistoryFilterToSql,
  latencyFilterToSql,
  topologyFilterToSql,
  gapFilterToSql,
} from '../../src/utils/showSqlGenerators';

describe('showSqlGenerators', () => {
  const FROM = '2026-01-01T10:00:00Z';
  const TO = '2026-01-01T11:00:00Z';

  it('timelineFilterToSql_IncludesFromAndToTimestamps', () => {
    const sql = timelineFilterToSql({ from: FROM, to: TO });
    expect(sql).toContain(`TIMESTAMP '${FROM}'`);
    expect(sql).toContain(`TIMESTAMP '${TO}'`);
  });

  it('timelineFilterToSql_IncludesTopicClauseWhenProvided', () => {
    const sql = timelineFilterToSql({ from: FROM, to: TO, topic: 'weapons.fire' });
    expect(sql).toContain("topic = 'weapons.fire'");
  });

  it('timelineFilterToSql_EscapesSingleQuoteInTopic', () => {
    const sql = timelineFilterToSql({ from: FROM, to: TO, topic: "weapon's.fire" });
    expect(sql).toContain("topic = 'weapon''s.fire'");
  });

  it('entityHistoryFilterToSql_IncludesEntityId', () => {
    const sql = entityHistoryFilterToSql('entity-42', FROM, TO);
    expect(sql).toContain("entity_id = 'entity-42'");
  });

  it('latencyFilterToSql_IncludesApproxQuantile', () => {
    const sql = latencyFilterToSql(FROM, TO);
    expect(sql).toContain('APPROX_QUANTILE');
  });

  it('topologyFilterToSql_IncludesGroupBy', () => {
    const sql = topologyFilterToSql(FROM, TO);
    expect(sql).toContain('GROUP BY');
  });

  it('gapFilterToSql_IncludesTopicClauseWhenProvided', () => {
    const sql = gapFilterToSql(FROM, TO, 'weapons.fire');
    expect(sql).toContain("topic = 'weapons.fire'");
  });
});
