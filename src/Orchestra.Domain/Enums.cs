namespace Orchestra.Domain;

public enum RunStatus { Pending, Running, Succeeded, Failed, Cancelled }

public enum TaskRunStatus { Pending, Queued, Running, Succeeded, Failed, Skipped, Cancelled }
