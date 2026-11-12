import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "../api";
import DagGraph from "../components/DagGraph";
import ErrorBox from "../components/ErrorBox";
import StatusBadge from "../components/StatusBadge";
import { formatDuration, formatTime, isActiveRun, shortId } from "../format";

export default function RunDetail({ id }: { id: string }) {
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<string | undefined>();

  // Poll while the run is in flight; stop once it reaches a final state.
  const run = useQuery({
    queryKey: ["run", id],
    queryFn: () => api.run(id),
    refetchInterval: (q) => (isActiveRun(q.state.data?.status) ? 1500 : false),
  });

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["run", id] });
    void queryClient.invalidateQueries({ queryKey: ["runs"] });
    void queryClient.invalidateQueries({ queryKey: ["stats"] });
  };
  const cancel = useMutation({ mutationFn: () => api.cancelRun(id), onSuccess: refresh });
  const retry = useMutation({ mutationFn: () => api.retryRun(id), onSuccess: refresh });

  const data = run.data;
  const task = data?.tasks.find((t) => t.name === selected);
  const canCancel = data && (data.status === "Pending" || data.status === "Running");
  const canRetry = data?.status === "Failed";

  return (
    <>
      <p>
        <a href="#/runs">← Runs</a>
      </p>
      <ErrorBox error={run.error ?? cancel.error ?? retry.error} />

      {data && (
        <>
          <div className="title-row">
            <h1>
              {data.workflowName} <span className="mono muted">{shortId(data.id)}</span>
            </h1>
            <StatusBadge status={data.status} />
            <div className="spacer" />
            {canCancel && (
              <button className="danger" disabled={cancel.isPending} onClick={() => cancel.mutate()}>
                Cancel run
              </button>
            )}
            {canRetry && (
              <button className="primary" disabled={retry.isPending} onClick={() => retry.mutate()}>
                Retry failed tasks
              </button>
            )}
          </div>

          <p className="muted">
            Created {formatTime(data.createdAt)} · started {formatTime(data.startedAt)} · duration{" "}
            {formatDuration(data.startedAt, data.completedAt)}
            {isActiveRun(data.status) && " · live"}
          </p>

          <section className="panel">
            <h2>Graph</h2>
            <DagGraph tasks={data.tasks} selected={selected} onSelect={setSelected} />
          </section>

          {task && (
            <section className="panel">
              <h2>
                {task.name} <StatusBadge status={task.status} />
              </h2>
              <dl className="details">
                <dt>Handler</dt>
                <dd className="mono">{task.handler}</dd>
                <dt>Attempts</dt>
                <dd>
                  {task.attempt} of {task.maxAttempts}
                </dd>
                <dt>Depends on</dt>
                <dd>{task.dependsOn.length ? task.dependsOn.join(", ") : "-"}</dd>
                <dt>Worker</dt>
                <dd className="mono">{task.leaseOwner ?? "-"}</dd>
                <dt>Next attempt</dt>
                <dd>{formatTime(task.nextAttemptAt)}</dd>
                {task.error && (
                  <>
                    <dt>Error</dt>
                    <dd className="mono error-text">{task.error}</dd>
                  </>
                )}
                {task.output && (
                  <>
                    <dt>Output</dt>
                    <dd className="mono">{task.output}</dd>
                  </>
                )}
              </dl>
            </section>
          )}

          <section className="panel">
            <h2>Tasks</h2>
            <table>
              <thead>
                <tr>
                  <th>Task</th>
                  <th>Status</th>
                  <th>Attempts</th>
                  <th>Started</th>
                  <th>Duration</th>
                  <th>Error</th>
                </tr>
              </thead>
              <tbody>
                {data.tasks.map((t) => (
                  <tr key={t.id} className="clickable" onClick={() => setSelected(t.name)}>
                    <td>{t.name}</td>
                    <td>
                      <StatusBadge status={t.status} />
                    </td>
                    <td>
                      {t.attempt}/{t.maxAttempts}
                    </td>
                    <td>{formatTime(t.startedAt)}</td>
                    <td>{formatDuration(t.startedAt, t.completedAt)}</td>
                    <td className="error-text truncate">{t.error ?? ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}
    </>
  );
}
