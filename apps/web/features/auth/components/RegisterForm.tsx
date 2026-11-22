"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm, useWatch } from "react-hook-form";
import {
  fieldA11y,
  fieldClass,
  FormAlert,
  FormField,
} from "@/components/form/FormField";
import { Input } from "@/components/ui/input";
import { ApiError, applyFieldErrors } from "@/lib/api/problem";
import { AutoLoginError, useRegister } from "../hooks";
import { authMessages, registerSchema, type RegisterValues } from "../schemas";
import { AuthHeader } from "./AuthHeader";
import { PasswordInput } from "./PasswordInput";
import { PasswordRequirements } from "./PasswordRequirements";
import { SubmitButton } from "./SubmitButton";

const FIELDS = ["displayName", "email", "password"] as const;

export function RegisterForm() {
  const router = useRouter();
  const registerAccount = useRegister();
  const form = useForm<RegisterValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      displayName: "",
      email: "",
      password: "",
      confirmPassword: "",
    },
  });
  const { errors, isSubmitting } = form.formState;
  const passwordValue = useWatch({ control: form.control, name: "password" });

  const onSubmit = form.handleSubmit(
    async ({ displayName, email, password }) => {
      try {
        await registerAccount.mutateAsync({ displayName, email, password });
        router.replace("/");
      } catch (error) {
        if (error instanceof AutoLoginError) {
          router.replace("/login?registered=1");
          return;
        }
        if (applyFieldErrors(error, form.setError, authMessages, FIELDS))
          return;
        const code = error instanceof ApiError ? error.code : undefined;
        form.setError("root", {
          message:
            (code && authMessages[code]) ??
            "Nie udało się utworzyć konta. Spróbuj ponownie.",
        });
      }
    },
  );

  const passwordA11y = fieldA11y("password", errors.password);

  return (
    <div className="flex flex-col gap-6">
      <AuthHeader
        title="Załóż konto"
        description="Dołącz do Fuel Club i zbieraj punkty za każde tankowanie."
      />

      <form onSubmit={onSubmit} className="flex flex-col gap-5" noValidate>
        <FormAlert message={errors.root?.message} />

        <FormField id="displayName" label="Imię" error={errors.displayName}>
          <Input
            autoComplete="given-name"
            placeholder="Jan"
            className={fieldClass}
            {...fieldA11y("displayName", errors.displayName)}
            {...form.register("displayName")}
          />
        </FormField>

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
            autoComplete="new-password"
            className={fieldClass}
            {...passwordA11y}
            aria-describedby={[
              passwordA11y["aria-describedby"],
              "password-rules",
            ]
              .filter(Boolean)
              .join(" ")}
            {...form.register("password")}
          />
          <PasswordRequirements id="password-rules" value={passwordValue} />
        </FormField>

        <FormField
          id="confirmPassword"
          label="Powtórz hasło"
          error={errors.confirmPassword}
        >
          <PasswordInput
            autoComplete="new-password"
            className={fieldClass}
            {...fieldA11y("confirmPassword", errors.confirmPassword)}
            {...form.register("confirmPassword")}
          />
        </FormField>

        <SubmitButton pending={isSubmitting} pendingLabel="Tworzenie konta...">
          Załóż konto
        </SubmitButton>
      </form>

      <p className="text-center text-sm text-muted-foreground">
        Masz już konto?{" "}
        <Link
          href="/login"
          className="font-semibold text-primary underline-offset-4 hover:underline"
        >
          Zaloguj się
        </Link>
      </p>
    </div>
  );
}
