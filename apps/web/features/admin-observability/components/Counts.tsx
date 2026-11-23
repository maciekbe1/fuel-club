export function Counts({
  label,
  items,
}: {
  label: string;
  items: [string, number][];
}) {
  return (
    <ul aria-label={label} className="flex flex-wrap gap-2 text-sm">
      {items.map(([key, count]) => (
        <li key={key} className="rounded-full border px-3 py-1">
          {key}: <span className="font-semibold tabular-nums">{count}</span>
        </li>
      ))}
    </ul>
  );
}
