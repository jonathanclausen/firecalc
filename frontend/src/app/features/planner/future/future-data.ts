import { Dashboard } from '../../../core/api/planner-api';
import { ProjectionPoint, StartingPoint } from '../../../core/finance/projection';

/** Line colours for scenarios, in order (classes .line--s0 … in the line chart). */
export const SCENARIO_KEYS = ['s0', 's1', 's2', 's3', 's4'] as const;

/** Splits today's accounts the way a projection grows them. */
export function startingPoint(dashboard: Dashboard): StartingPoint {
  const start: StartingPoint = {
    investments: 0,
    savings: 0,
    homeValue: 0,
    homeLoan: 0,
    otherLoans: 0,
  };
  for (const a of dashboard.accounts) {
    switch (a.type) {
      case 'investment':
        start.investments += a.value;
        break;
      case 'savings':
      case 'cash':
        start.savings += a.value;
        break;
      case 'property':
        start.homeValue += a.homeValue ?? a.value;
        start.homeLoan += a.homeLoan ?? 0;
        break;
      case 'loan':
        start.otherLoans += -a.value;
        break;
    }
  }
  return start;
}

/** One point a year from today (plus the last), which reads better than months over decades. */
export function yearly(points: ProjectionPoint[]): ProjectionPoint[] {
  const picked = points.filter((_, i) => i % 12 === 0);
  const last = points[points.length - 1];
  if (last && picked[picked.length - 1] !== last) picked.push(last);
  return picked;
}
