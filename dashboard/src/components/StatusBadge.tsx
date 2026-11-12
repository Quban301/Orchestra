export default function StatusBadge({ status }: { status: string }) {
  return (
    <span className={`badge s-${status}`}>
      <span className="dot" />
      {status}
    </span>
  );
}
