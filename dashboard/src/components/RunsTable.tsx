import type { RunSummaryDto } from "../api";
import { formatDuration, formatTime, shortId } from "../format";
import StatusBadge from "./StatusBadge";

export default function RunsTable({ runs }: { runs: RunSummaryDto[] }) {
  if (runs.length === 0) return <p className="muted">No runs yet.</p>;
  return (
    <table>
      <thead>
        <tr>
          <th>Run</th>
          <th>Workflow</th>
          <th>Status</th>
          <th>Tasks</th>
          <th>Started</th>
          <th>Duration</th>
        </tr>
      </thead>
      <tbody>
        {runs.map((r) => (
          <tr key={r.id} className="clickable" onClick={() => (window.location.hash = `#/runs/${r.id}`)}>
            <td className="mono">
              <a href={`#/runs/${r.id}`}>{shortId(r.id)}</a>
            </td>
            <td>{r.workflowName}</td>
            <td>
              <StatusBadge status={r.status} />
            </td>
            <td>{r.taskCount}</td>
            <td>{formatTime(r.startedAt ?? r.createdAt)}</td>
            <td>{formatDuration(r.startedAt, r.completedAt)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
