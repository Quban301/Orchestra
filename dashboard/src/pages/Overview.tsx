import { useQuery } from "@tanstack/react-query";
import { api, RUN_STATUSES } from "../api";
import ErrorBox from "../components/ErrorBox";
import RunsTable from "../components/RunsTable";

export default function Overview() {
  const stats = useQuery({ queryKey: ["stats"], queryFn: api.stats, refetchInterval: 3000 });
  const recent = useQuery({
    queryKey: ["runs", { page: 1, pageSize: 8 }],
    queryFn: () => api.runs({ page: 1, pageSize: 8 }),
    refetchInterval: 3000,
  });

  return (
    <>
      <h1>Overview</h1>
      <ErrorBox error={stats.error ?? recent.error} />

      <div className="cards">
        <div className="card">
          <div className="card-label">Workflows</div>
          <div className="card-value">{stats.data?.workflows ?? "-"}</div>
        </div>
        {RUN_STATUSES.map((s) => (
          <div key={s} className={`card s-${s}`}>
            <div className="card-label">
              <span className="dot" /> {s} runs
            </div>
            <div className="card-value">{stats.data?.runs[s] ?? "-"}</div>
          </div>
        ))}
      </div>

      <section className="panel">
        <h2>Recent runs</h2>
        <RunsTable runs={recent.data?.items ?? []} />
        <p>
          <a href="#/runs">All runs →</a>
        </p>
      </section>

      <section className="panel">
        <h2>Tasks by status</h2>
        <div className="chips">
          {Object.entries(stats.data?.tasks ?? {}).map(([status, count]) => (
            <span key={status} className={`chip s-${status}`}>
              <span className="dot" /> {status} <strong>{count}</strong>
            </span>
          ))}
        </div>
      </section>
    </>
  );
}
