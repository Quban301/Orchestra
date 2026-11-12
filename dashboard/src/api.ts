// Typed client for the Orchestra REST API (see docs/api.md).

export type RunStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "Cancelled";
export type TaskStatus = "Pending" | "Queued" | "Running" | "Succeeded" | "Failed" | "Skipped" | "Cancelled";

export const RUN_STATUSES: RunStatus[] = ["Pending", "Running", "Succeeded", "Failed", "Cancelled"];

export interface TaskDefinitionDto {
  name: string;
  handler: string;
  dependsOn?: string[] | null;
  maxAttempts?: number | null;
  timeoutSeconds?: number | null;
  payload?: string | null;
}

export interface WorkflowDto {
  id: string;
  name: string;
  version: number;
  isActive: boolean;
  createdAt: string;
  tasks: TaskDefinitionDto[];
}

export interface TaskRunDto {
  id: string;
  name: string;
  handler: string;
  status: TaskStatus;
  dependsOn: string[];
  attempt: number;
  maxAttempts: number;
  error: string | null;
  output: string | null;
  leaseOwner: string | null;
  nextAttemptAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
}

export interface RunSummaryDto {
  id: string;
  workflowDefinitionId: string;
  workflowName: string;
  status: RunStatus;
  taskCount: number;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface RunDetailDto extends Omit<RunSummaryDto, "taskCount"> {
  tasks: TaskRunDto[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface StatsDto {
  workflows: number;
  runs: Record<string, number>;
  tasks: Record<string, number>;
}

// ---- settings (kept in localStorage; per-browser convenience only) ----

export interface Settings {
  baseUrl: string;
  apiKey: string;
}

const SETTINGS_KEY = "orchestra.settings";
const DEFAULT_SETTINGS: Settings = {
  baseUrl: "",
  apiKey: import.meta.env.DEV ? "dev-admin-key" : "",
};

export function loadSettings(): Settings {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY);
    if (raw) return { ...DEFAULT_SETTINGS, ...(JSON.parse(raw) as Partial<Settings>) };
  } catch {
    /* storage unavailable or corrupt: fall back to defaults */
  }
  return DEFAULT_SETTINGS;
}

export function saveSettings(settings: Settings): void {
  try {
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
  } catch {
    /* ignore */
  }
}

// ---- transport ----

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

function describeProblem(status: number, problem: Problem | undefined): string {
  if (problem?.errors) {
    const lines = Object.values(problem.errors).flat();
    if (lines.length) return lines.join(" ");
  }
  if (problem?.detail) return problem.detail;
  if (problem?.title) return problem.title;
  if (status === 401) return "Unauthorized: check your API key in Settings.";
  if (status === 403) return "Forbidden: this API key is read-only.";
  if (status === 429) return "Rate limit exceeded. Slow down and retry.";
  return `Request failed (${status}).`;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const { baseUrl, apiKey } = loadSettings();
  const res = await fetch(baseUrl.replace(/\/$/, "") + path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      "X-Api-Key": apiKey,
      ...(init.headers as Record<string, string> | undefined),
    },
  });

  if (!res.ok) {
    let problem: Problem | undefined;
    try {
      problem = (await res.json()) as Problem;
    } catch {
      /* no body */
    }
    throw new ApiError(res.status, describeProblem(res.status, problem));
  }
  return (await res.json()) as T;
}

function qs(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter(([, v]) => v !== undefined && v !== "");
  return entries.length ? "?" + new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString() : "";
}

export const api = {
  stats: () => request<StatsDto>("/api/v1/stats"),
  runs: (p: { page?: number; pageSize?: number; status?: string; workflowId?: string } = {}) =>
    request<PagedResult<RunSummaryDto>>("/api/v1/runs" + qs(p)),
  run: (id: string) => request<RunDetailDto>(`/api/v1/runs/${id}`),
  cancelRun: (id: string) => request<RunDetailDto>(`/api/v1/runs/${id}/cancel`, { method: "POST" }),
  retryRun: (id: string) => request<RunDetailDto>(`/api/v1/runs/${id}/retry`, { method: "POST" }),
  workflows: (p: { page?: number; pageSize?: number } = {}) =>
    request<PagedResult<WorkflowDto>>("/api/v1/workflows" + qs(p)),
  createWorkflow: (body: { name: string; tasks: TaskDefinitionDto[] }) =>
    request<WorkflowDto>("/api/v1/workflows", { method: "POST", body: JSON.stringify(body) }),
  startRun: (workflowId: string) =>
    request<RunSummaryDto>(`/api/v1/workflows/${workflowId}/runs`, { method: "POST" }),
};

export async function checkHealth(): Promise<boolean> {
  try {
    const { baseUrl } = loadSettings();
    const res = await fetch(baseUrl.replace(/\/$/, "") + "/health");
    return res.ok;
  } catch {
    return false;
  }
}
