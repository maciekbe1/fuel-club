// @ts-check
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";

export default defineConfig({
  integrations: [
    starlight({
      title: "Fuel Club - dokumentacja",
      defaultLocale: "root",
      locales: { root: { label: "Polski", lang: "pl" } },
      lastUpdated: false,
      sidebar: [
        { label: "Start", slug: "index" },
        { label: "Nauka", items: [{ autogenerate: { directory: "nauka" } }] },
        { label: "Instrukcje", items: [{ autogenerate: { directory: "instrukcje" } }] },
        { label: "Referencja", items: [{ autogenerate: { directory: "referencja" } }] },
        { label: "Wyjaśnienia", items: [{ autogenerate: { directory: "wyjasnienia" } }] },
        { label: "Moduły", items: [{ autogenerate: { directory: "moduly" } }] },
        { label: "Decyzje (ADR)", items: [{ autogenerate: { directory: "decyzje" } }] },
      ],
    }),
  ],
});
