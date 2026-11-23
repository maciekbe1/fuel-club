"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { FormAlert } from "@/components/form/FormAlert";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Input } from "@/components/ui/input";
import { ApiError, applyFieldErrors } from "@/lib/api/problem";
import { useLogin } from "../hooks";
import { authMessages, loginSchema, type LoginValues } from "../schemas";
import { AuthHeader } from "./AuthHeader";
import { PasswordInput } from "./PasswordInput";
import { SubmitButton } from "./SubmitButton";

const FIELDS = ["email", "password"] as const;

export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const next = searchParams.get("next");
  const registered = searchParams.get("registered") === "1";
  const login = useLogin();
  const form = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });
  const { errors, isSubmitting } = form.formState;

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await login.mutateAsync(values);
      router.replace(
        next?.startsWith("/") && !next.startsWith("//") ? next : "/",
      );
    } catch (error) {
      if (applyFieldErrors(error, form.setError, authMessages, FIELDS)) return;
      const code = error instanceof ApiError ? error.code : undefined;
      form.setError("root", {
        message:
          (code && authMessages[code]) ??
          "Nie udało się zalogować. Spróbuj ponownie.",
      });
    }
  });

  return (
    <div className="flex flex-col gap-6">
      <AuthHeader
        title="Witaj ponownie"
        description="Zaloguj się, aby sprawdzić punkty i kupony."
      />

      {registered && (
        <p role="status" className="rounded-xl bg-muted p-4 text-sm">
          Konto zostało utworzone. Zaloguj się, aby kontynuować.
        </p>
      )}

      <form onSubmit={onSubmit} className="flex flex-col gap-5" noValidate>
        <FormAlert message={errors.root?.message} />

        <FormField id="email" label="E-mail" error={errors.email}>
          <Input
            type="email"
            inputMode="email"
            autoComplete="email"
            placeholder="jan@example.com"
            className={fieldClass}
            {...fieldA11y("email", errors.email)}
            {...form.register("email")}
          />
        </FormField>

        <FormField id="password" label="Hasło" error={errors.password}>
          <PasswordInput
            autoComplete="current-password"
            className={fieldClass}
            {...fieldA11y("password", errors.password)}
            {...form.register("password")}
          />
        </FormField>

        <SubmitButton pending={isSubmitting} pendingLabel="Logowanie...">
          Zaloguj się
        </SubmitButton>
      </form>

      <p className="text-center text-sm text-muted-foreground">
        Nie masz konta?{" "}
        <Link
          href="/rejestracja"
          className="font-semibold text-primary underline-offset-4 hover:underline"
        >
          Załóż konto
        </Link>
      </p>
    </div>
  );
}
