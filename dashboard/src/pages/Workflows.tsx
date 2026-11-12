import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, type TaskDefinitionDto } from "../api";
import ErrorBox from "../components/ErrorBox";
import Pager from "../components/Pager";
import { formatTime } from "../format";
import { navigate } from "../router";

const PAGE_SIZE = 10;

const EXAMPLE = JSON.stringify(
  {
    name: "demo",
    tasks: [
      { name: "extract", handler: "echo", payload: '{"rows":42}' },
      { name: "transform", handler: "flaky", dependsOn: ["extract"], maxAttempts: 4, payload: "3" },
      { name: "load", handler: "sleep", dependsOn: ["transform"], payload: "1500" },
      { name: "notify", handler: "echo", dependsOn: ["load"], payload: "done" },
    ],
  },
  null,
  2,
);

export default function Workflows() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [json, setJson] = useState(EXAMPLE);
  const [formError, setFormError] = useState<string | null>(null);

  const workflows = useQuery({
    queryKey: ["workflows", { page }],
    queryFn: () => api.workflows({ page, pageSize: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });

  const create = useMutation({
    mutationFn: (body: { name: string; tasks: TaskDefinitionDto[] }) => api.createWorkflow(body),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["workflows"] }),
  });

  const start = useMutation({
    mutationFn: (workflowId: string) => api.startRun(workflowId),
    onSuccess: (run) => {
      void queryClient.invalidateQueries({ queryKey: ["runs"] });
      navigate(`#/runs/${run.id}`);
    },
  });

  const submit = () => {
    setFormError(null);
    try {
      const body = JSON.parse(json) as { name: string; tasks: TaskDefinitionDto[] };
      create.mutate(body);
    } catch {
      setFormError("That is not valid JSON.");
    }
  };

  return (
    <>
      <h1>Workflows</h1>
      <ErrorBox error={workflows.error ?? start.error} />

      <section className="panel">
        <h2>Registered</h2>
        {workflows.data?.items.length === 0 && <p className="muted">No workflows yet. Create one below.</p>}
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Version</th>
              <th>Tasks</th>
              <th>Created</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {workflows.data?.items.map((w) => (
              <tr key={w.id}>
                <td>{w.name}</td>
                <td>v{w.version}</td>
                <td>{w.tasks.length}</td>
                <td>{formatTime(w.createdAt)}</td>
                <td className="right">
                  <button className="primary" disabled={start.isPending} onClick={() => start.mutate(w.id)}>
                    Start run
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {workflows.data && (
          <Pager page={workflows.data.page} pageSize={PAGE_SIZE} total={workflows.data.total} onPage={setPage} />
        )}
      </section>

      <section className="panel">
        <h2>Create a workflow</h2>
        <p className="muted">
          Define tasks and their <code>dependsOn</code> links as JSON. Registering an existing name creates the next
          version.
        </p>
        <textarea value={json} onChange={(e) => setJson(e.target.value)} rows={16} spellCheck={false} />
        <ErrorBox error={formError ?? create.error} />
        {create.isSuccess && <p className="ok-text">Created {create.data.name} v{create.data.version}.</p>}
        <button className="primary" disabled={create.isPending} onClick={submit}>
          Create workflow
        </button>
      </section>
    </>
  );
}
