/** Test-only cashier created by the API's BootstrapCashier on the e2e database. Not a real secret. */
export const CASHIER = {
  email: "kasjer-e2e@example.test",
  password: "E2e-Cashier-Password-1!",
  station: "E2E-01",
} as const;

/** Test-only support agent created by the API's BootstrapSupport on the e2e database. Not a real secret. */
export const SUPPORT = {
  email: "konsultant-e2e@example.test",
  password: "E2e-Support-Password-1!",
} as const;

/** Test-only admin created by the API's BootstrapAdmin on the e2e database. Not a real secret. */
export const ADMIN = {
  email: "admin-e2e@example.test",
  password: "E2e-Admin-Password-1!",
} as const;
