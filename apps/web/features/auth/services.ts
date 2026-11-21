import { api, unwrap } from "@/lib/api/client";
import { resetCsrfToken } from "@/lib/api/csrf";
import type { components } from "@/lib/api/schema";

export type Me = components["schemas"]["MeResponse"];
export type LoginInput = components["schemas"]["LoginRequest"];
export type RegisterInput = components["schemas"]["RegisterRequest"];

export const authService = {
  me: () => unwrap(api.GET("/api/auth/me")),

  async login(body: LoginInput) {
    await unwrap(api.POST("/api/auth/login", { body }));
    resetCsrfToken();
  },

  async logout() {
    await unwrap(api.POST("/api/auth/logout"));
    resetCsrfToken();
  },

  register: (body: RegisterInput) => unwrap(api.POST("/api/auth/register", { body })),
};

export const isAdmin = (me: Me | undefined) => me?.roles.includes("Admin") ?? false;
