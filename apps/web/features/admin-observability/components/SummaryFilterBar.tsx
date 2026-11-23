import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import type { FilterOptions, SummaryFilters, Traffic } from "../services";
import { FilterSelect } from "./FilterSelect";
import { TRAFFIC } from "./labels";

/** Traffic and the run, model and prompt version; security and anomaly rules ignore them. */
export function SummaryFilterBar({
  value,
  options,
  onChange,
}: {
  value: SummaryFilters;
  options: FilterOptions | undefined;
  onChange: (value: SummaryFilters) => void;
}) {
  return (
    <div
      role="group"
      aria-label="Filtry metryk"
      className="flex flex-col gap-3"
    >
      <ToggleGroup
        aria-label="Ruch"
        variant="outline"
        size="sm"
        value={[value.traffic]}
        onValueChange={(next) =>
          next[0] &&
          // Another traffic has other runs: the run filter starts over.
          onChange({ ...value, traffic: next[0] as Traffic, runId: undefined })
        }
      >
        {(Object.keys(TRAFFIC) as Traffic[]).map((traffic) => (
          <ToggleGroupItem key={traffic} value={traffic}>
            {TRAFFIC[traffic]}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <FilterSelect
          id="filter-run"
          label="Przebieg (RunId)"
          value={value.runId}
          options={options?.runIds ?? []}
          onChange={(runId) => onChange({ ...value, runId })}
        />
        <FilterSelect
          id="filter-model"
          label="Model"
          value={value.model}
          options={options?.models ?? []}
          onChange={(model) => onChange({ ...value, model })}
        />
        <FilterSelect
          id="filter-prompt"
          label="Wersja promptu"
          value={value.promptVersion}
          options={options?.promptVersions ?? []}
          onChange={(promptVersion) => onChange({ ...value, promptVersion })}
        />
      </div>
    </div>
  );
}
