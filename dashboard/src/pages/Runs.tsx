import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api, RUN_STATUSES } from "../api";
import ErrorBox from "../components/ErrorBox";
import Pager from "../components/Pager";
import RunsTable from "../components/RunsTable";

const PAGE_SIZE = 15;

export default function Runs() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");

  const runs = useQuery({
    queryKey: ["runs", { page, status }],
    queryFn: () => api.runs({ page, pageSize: PAGE_SIZE, status }),
    placeholderData: keepPreviousData,
    refetchInterval: 3000,
  });

  return (
    <>
      <h1>Runs</h1>
      <div className="toolbar">
        <label>
          Status{" "}
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All</option>
            {RUN_STATUSES.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </label>
      </div>

      <ErrorBox error={runs.error} />
      <section className="panel">
        <RunsTable runs={runs.data?.items ?? []} />
        {runs.data && <Pager page={runs.data.page} pageSize={PAGE_SIZE} total={runs.data.total} onPage={setPage} />}
      </section>
    </>
  );
}
