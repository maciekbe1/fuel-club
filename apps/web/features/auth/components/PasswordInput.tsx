"use client";

import { Eye, EyeOff } from "lucide-react";
import { useState, type ComponentProps } from "react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

export function PasswordInput({
  className,
  ...props
}: Omit<ComponentProps<"input">, "type">) {
  const [visible, setVisible] = useState(false);
  const Icon = visible ? EyeOff : Eye;

  return (
    <div className="relative">
      <Input
        type={visible ? "text" : "password"}
        className={cn("pr-12", className)}
        {...props}
      />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? "Ukryj hasło" : "Pokaż hasło"}
        aria-pressed={visible}
        className="absolute inset-y-0 right-0 grid w-12 place-items-center rounded-r-xl text-muted-foreground outline-none hover:text-foreground focus-visible:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <Icon className="size-5" aria-hidden />
      </button>
    </div>
  );
}
