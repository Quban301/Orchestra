export default function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof Error ? error.message : String(error);
  return (
    <div className="error-box" role="alert">
      {message}
    </div>
  );
}
