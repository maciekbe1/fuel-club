import { Label } from "@/components/ui/label";

/** A labelled native select; the empty option means "every value". */
export function FilterSelect({
  id,
  label,
  value,
  options,
  onChange,
}: {
  id: string;
  label: string;
  value: string | undefined;
  options: string[];
  onChange: (value: string | undefined) => void;
}) {
  // A chosen value missing in this window stays selectable, so the filter is visible.
  const values =
    value && !options.includes(value) ? [value, ...options] : options;
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <Label htmlFor={id} className="text-xs text-muted-foreground">
        {label}
      </Label>
      <select
        id={id}
        className="h-9 min-w-0 rounded-xl border bg-background px-2 text-sm"
        value={value ?? ""}
        onChange={(event) => onChange(event.target.value || undefined)}
      >
        <option value="">Wszystkie</option>
        {values.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </div>
  );
}
